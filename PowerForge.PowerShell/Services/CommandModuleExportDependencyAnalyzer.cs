using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

internal static class CommandModuleExportDependencyAnalyzer
{
    internal static IReadOnlyDictionary<string, string[]> Analyze(
        IEnumerable<string> scriptFiles,
        IReadOnlyDictionary<string, string[]>? commandModuleDependencies,
        IReadOnlyList<string>? exportedFunctions,
        ILogger? logger = null)
    {
        var configured = NormalizeConfiguredDependencies(commandModuleDependencies);
        if (configured.Count == 0)
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var exported = new HashSet<string>(
            (exportedFunctions ?? Array.Empty<string>())
                .Where(static function => !string.IsNullOrWhiteSpace(function))
                .Select(static function => function.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (exported.Count == 0)
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var functions = ParseFunctions(scriptFiles, logger);
        var commandSourceCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in configured)
        {
            var moduleName = module.Key;
            var configuredCommands = module.Value;
            var configuredCommandSet = new HashSet<string>(configuredCommands, StringComparer.OrdinalIgnoreCase);
            var localFunctionNames = new HashSet<string>(functions.Keys, StringComparer.OrdinalIgnoreCase);
            var directModuleHits = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var function in functions.Values)
            {
                directModuleHits[function.Name] = FunctionDirectlyDependsOnModule(
                    function,
                    moduleName,
                    configuredCommandSet,
                    localFunctionNames,
                    commandSourceCache,
                    logger);
            }

            var conditionalFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dependencyMemo = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var command in configuredCommands)
            {
                if (exported.Contains(command))
                    conditionalFunctions.Add(command);
            }

            foreach (var functionName in exported)
            {
                if (!functions.TryGetValue(functionName, out var function))
                    continue;

                if (DependsOnModule(function, functions, directModuleHits, new HashSet<string>(StringComparer.OrdinalIgnoreCase), dependencyMemo))
                    conditionalFunctions.Add(functionName);
            }

            var ordered = conditionalFunctions
                .OrderBy(static function => function, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ordered.Length > 0)
                result[moduleName] = ordered;
        }

        return result;
    }

    private static Dictionary<string, string[]> NormalizeConfiguredDependencies(
        IReadOnlyDictionary<string, string[]>? commandModuleDependencies)
    {
        var configured = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (commandModuleDependencies is null || commandModuleDependencies.Count == 0)
            return configured;

        foreach (var dependency in commandModuleDependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.Key))
                continue;

            configured[dependency.Key.Trim()] = (dependency.Value ?? Array.Empty<string>())
                .Where(static command => !string.IsNullOrWhiteSpace(command))
                .Select(static command => command.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return configured;
    }

    private static Dictionary<string, FunctionDependencyInfo> ParseFunctions(IEnumerable<string> scriptFiles, ILogger? logger)
    {
        var functions = new Dictionary<string, FunctionDependencyInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in scriptFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                continue;

            try
            {
                Token[] tokens;
                ParseError[] errors;
                var ast = Parser.ParseFile(file, out tokens, out errors);
                if (errors is { Length: > 0 })
                {
                    if (logger?.IsVerbose == true)
                        logger.Verbose($"Conditional export dependency analyzer skipped '{file}' because PowerShell parsing returned {errors.Length} error(s).");
                    continue;
                }

                var functionAsts = ast.FindAll(
                        static node => node is FunctionDefinitionAst,
                        searchNestedScriptBlocks: false)
                    .Cast<FunctionDefinitionAst>();

                foreach (var functionAst in functionAsts)
                {
                    if (string.IsNullOrWhiteSpace(functionAst.Name))
                        continue;

                    var commandNames = ExtractCommandNames(functionAst)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    functions[functionAst.Name] = new FunctionDependencyInfo(
                        functionAst.Name,
                        commandNames,
                        functionAst.Extent.Text);
                }
            }
            catch (Exception ex)
            {
                // Best effort; parse errors should not make packaging fail.
                if (logger?.IsVerbose == true)
                    logger.Verbose($"Conditional export dependency analyzer could not parse '{file}': {ex.Message}");
            }
        }

        foreach (var function in functions.Values)
        {
            function.LocalCalls = function.CommandNames
                .Where(functions.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return functions;
    }

    private static IEnumerable<string> ExtractCommandNames(FunctionDefinitionAst functionAst)
    {
        var commands = functionAst.FindAll(
                static node => node is CommandAst,
                searchNestedScriptBlocks: true)
            .Cast<CommandAst>();

        foreach (var command in commands)
        {
            var name = command.GetCommandName();
            if (string.IsNullOrWhiteSpace(name) &&
                command.CommandElements.Count > 0 &&
                command.CommandElements[0] is StringConstantExpressionAst stringCommand)
            {
                name = stringCommand.Value;
            }

            if (string.IsNullOrWhiteSpace(name))
                continue;

            name = name.Trim();
            if (name.Length == 0)
                continue;
            if (name.StartsWith("$", StringComparison.Ordinal))
                continue;
            if (!LooksLikeCommandName(name))
                continue;

            yield return name;
        }
    }

    private static bool FunctionDirectlyDependsOnModule(
        FunctionDependencyInfo function,
        string moduleName,
        HashSet<string> configuredCommands,
        HashSet<string> localFunctionNames,
        Dictionary<string, string?> commandSourceCache,
        ILogger? logger)
    {
        foreach (var command in function.CommandNames)
        {
            if (configuredCommands.Count > 0 && configuredCommands.Contains(command))
                return true;

            if (localFunctionNames.Contains(command))
                continue;

            // Explicit command lists are authoritative; naming heuristics only fill in module-only declarations.
            if (configuredCommands.Count == 0 && CommandLooksLikeModuleCommand(moduleName, command))
                return true;

            var source = ResolveCommandSource(command, commandSourceCache, logger);
            if (!string.IsNullOrWhiteSpace(source) &&
                string.Equals(source, moduleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (configuredCommands.Count == 0 && TextLooksLikeModuleDependency(moduleName, function.Text))
            return true;

        return false;
    }

    private static bool DependsOnModule(
        FunctionDependencyInfo function,
        IReadOnlyDictionary<string, FunctionDependencyInfo> functions,
        IReadOnlyDictionary<string, bool> directModuleHits,
        HashSet<string> visited,
        Dictionary<string, bool> memo)
    {
        if (memo.TryGetValue(function.Name, out var cached))
            return cached;

        if (!visited.Add(function.Name))
            return false;

        var depends = false;
        if (directModuleHits.TryGetValue(function.Name, out var direct) && direct)
        {
            depends = true;
        }
        else
        {
            foreach (var localCall in function.LocalCalls)
            {
                if (!functions.TryGetValue(localCall, out var dependency))
                    continue;
                if (DependsOnModule(dependency, functions, directModuleHits, visited, memo))
                {
                    depends = true;
                    break;
                }
            }
        }

        visited.Remove(function.Name);
        memo[function.Name] = depends;
        return depends;
    }

    private static string? ResolveCommandSource(string commandName, Dictionary<string, string?> cache, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return null;

        if (cache.TryGetValue(commandName, out var cached))
            return cached;

        try
        {
            // Best-effort fallback: Get-Command reflects the build host, so explicit command names
            // and stable naming heuristics are preferred when available.
            using var ps = PowerShell.Create();
            ps.AddCommand("Get-Command")
                .AddParameter("Name", commandName)
                .AddParameter("CommandType", CommandTypes.All)
                .AddParameter("ErrorAction", "SilentlyContinue")
                .AddParameter("Verbose", false);

            var result = ps.Invoke();
            var command = result.Count > 0 ? result[0].BaseObject as CommandInfo : null;
            var source = command?.Source;
            if (logger?.IsVerbose == true)
            {
                var sourceLabel = string.IsNullOrWhiteSpace(source) ? "<unresolved>" : source;
                logger.Verbose($"Conditional export dependency analyzer used Get-Command fallback for '{commandName}' -> '{sourceLabel}'.");
            }

            cache[commandName] = source;
            return source;
        }
        catch
        {
            cache[commandName] = null;
            return null;
        }
    }

    private static bool CommandLooksLikeModuleCommand(string moduleName, string commandName)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(commandName))
            return false;

        var dash = commandName.IndexOf("-", StringComparison.Ordinal);
        if (dash <= 0 || dash >= commandName.Length - 1)
            return false;

        var noun = commandName.Substring(dash + 1);
        // ActiveDirectory uses AD-prefixed nouns (Get-ADUser), while DNS/DHCP commands use longer module-name prefixes.
        if (string.Equals(moduleName, "ActiveDirectory", StringComparison.OrdinalIgnoreCase))
            return noun.StartsWith("AD", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(moduleName, "DnsServer", StringComparison.OrdinalIgnoreCase))
            return noun.StartsWith("DnsServer", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(moduleName, "DhcpServer", StringComparison.OrdinalIgnoreCase))
            return noun.StartsWith("DhcpServer", StringComparison.OrdinalIgnoreCase);

        return noun.StartsWith(moduleName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TextLooksLikeModuleDependency(string moduleName, string text)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(text))
            return false;

        // These module-only text checks intentionally cover the historical simple forms.
        // AST command analysis handles ordinary command calls; this catches type/provider/import hints.
        if (string.Equals(moduleName, "ActiveDirectory", StringComparison.OrdinalIgnoreCase) &&
            (text.IndexOf("Microsoft.ActiveDirectory.Management", StringComparison.OrdinalIgnoreCase) >= 0 ||
             text.IndexOf("Import-Module ActiveDirectory", StringComparison.OrdinalIgnoreCase) >= 0 ||
             text.IndexOf("PsProvider ActiveDirectory", StringComparison.OrdinalIgnoreCase) >= 0 ||
             text.IndexOf("PSProvider ActiveDirectory", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        if (string.Equals(moduleName, "DnsServer", StringComparison.OrdinalIgnoreCase) &&
            text.IndexOf("Import-Module DnsServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (string.Equals(moduleName, "DhcpServer", StringComparison.OrdinalIgnoreCase) &&
            text.IndexOf("Import-Module DhcpServer", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeCommandName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var value = name.Trim();
        if (value.Length == 0) return false;
        if (value.IndexOfAny(new[] { '{', '}', '(', ')', ';', '|', '"', '\'' }) >= 0) return false;
        if (value.Any(char.IsWhiteSpace)) return false;
        return true;
    }

    private sealed class FunctionDependencyInfo
    {
        internal FunctionDependencyInfo(string name, string[] commandNames, string text)
        {
            Name = name;
            CommandNames = commandNames;
            Text = text;
            LocalCalls = Array.Empty<string>();
        }

        internal string Name { get; }
        internal string[] CommandNames { get; }
        internal string Text { get; }
        // Resolved after all files are parsed, because local calls can point to functions in later files.
        internal string[] LocalCalls { get; set; }
    }
}
