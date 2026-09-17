using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;

namespace PowerForge;

/// <summary>
/// Analyzes a PowerShell script (file or code) and reports referenced commands that are not declared locally,
/// optionally returning inlineable helper function definitions sourced from approved modules.
/// </summary>
public sealed class MissingFunctionsAnalyzer
{
    private readonly Dictionary<string, CommandInfo?> _currentSessionCommandCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, CommandInfo?> _moduleScopeCommandCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Analyzes a script file or code and returns a typed report with resolved command references and
    /// inlineable helper definitions.
    /// </summary>
    /// <param name="filePath">Path to a script file to analyze.</param>
    /// <param name="code">PowerShell code to analyze (used when <paramref name="filePath"/> is not provided).</param>
    /// <param name="options">Options controlling analysis behavior.</param>
    public MissingFunctionsReport Analyze(string? filePath, string? code, MissingFunctionsOptions? options = null)
    {
        options ??= new MissingFunctionsOptions();

        var approved = new HashSet<string>(
            (options.ApprovedModules ?? Array.Empty<string>())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var ignore = new HashSet<string>(
            (options.IgnoreFunctions ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var known = new HashSet<string>(
            (options.KnownFunctions ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return AnalyzeInternal(
            filePath: filePath,
            code: code,
            knownFunctions: known,
            approvedModules: approved,
            ignoreFunctions: ignore,
            includeFunctionsRecursively: options.IncludeFunctionsRecursively);
    }

    private MissingFunctionsReport AnalyzeInternal(
        string? filePath,
        string? code,
        HashSet<string> knownFunctions,
        HashSet<string> approvedModules,
        HashSet<string> ignoreFunctions,
        bool includeFunctionsRecursively)
    {
        var parsed = ParseInput(filePath, code);

        var declaredFunctions = parsed.FunctionNames;
        var excludeFunctions = new HashSet<string>(knownFunctions, StringComparer.OrdinalIgnoreCase);
        foreach (var fn in declaredFunctions) excludeFunctions.Add(fn);

        var commandNames = parsed.CommandNames.Where(n => !ignoreFunctions.Contains(n)).ToArray();
        var filteredNames = commandNames
            .Where(n => !excludeFunctions.Contains(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var listCommands = new List<MissingFunctionCommand>();
        foreach (var name in filteredNames)
        {
            var info = ResolveCommand(name, approvedModules);
            if (string.Equals(info.Source, "Microsoft.PowerShell.Core", StringComparison.OrdinalIgnoreCase))
                continue;

            listCommands.Add(info);
        }

        var functionsTop = BuildInlineFunctions(listCommands, approvedModules);
        var combinedSummary = new List<MissingFunctionCommand>(listCommands);
        var combinedSummaryFiltered = new List<MissingFunctionCommand>(listCommands);
        var combinedFunctions = new List<string>(functionsTop);

        if (functionsTop.Count > 0)
        {
            var ignoreNext = new HashSet<string>(ignoreFunctions, StringComparer.OrdinalIgnoreCase);
            foreach (var n in listCommands.Select(o => o.Name).Where(s => !string.IsNullOrWhiteSpace(s)))
                ignoreNext.Add(n);

            var nested = AnalyzeInternal(
                filePath: null,
                code: string.Join(Environment.NewLine, functionsTop),
                knownFunctions: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                approvedModules: approvedModules,
                ignoreFunctions: ignoreNext,
                includeFunctionsRecursively: includeFunctionsRecursively);

            combinedSummary.AddRange(nested.Summary);
            combinedSummaryFiltered.AddRange(nested.SummaryFiltered);

            if (includeFunctionsRecursively)
                combinedFunctions.AddRange(nested.Functions);
        }

        return new MissingFunctionsReport(
            summary: combinedSummary.ToArray(),
            summaryFiltered: combinedSummaryFiltered.ToArray(),
            functions: combinedFunctions.ToArray(),
            functionsTopLevelOnly: functionsTop.ToArray());
    }

    private static List<string> BuildInlineFunctions(IEnumerable<MissingFunctionCommand> commands, HashSet<string> approvedModules)
    {
        var output = new List<string>();
        if (approvedModules.Count == 0)
            return output;

        foreach (var c in commands)
        {
            var name = c.Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var source = c.Source;
            if (string.IsNullOrWhiteSpace(source))
                continue;
            source = source.Trim();
            if (source.Length == 0)
                continue;
            if (!approvedModules.Contains(source))
                continue;

            var sb = c.ScriptBlock;
            if (sb is null)
                continue;

            output.Add($"function {name} {{ {sb} }}");
        }

        return output;
    }

    private static ParsedInput ParseInput(string? filePath, string? code)
    {
        string? effectiveFilePath = null;
        ScriptBlockAst ast;
        Token[] tokens;
        ParseError[] errors;

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            effectiveFilePath = Path.GetFullPath(filePath!.Trim().Trim('"'));
            ast = Parser.ParseFile(effectiveFilePath, out tokens, out errors);
        }
        else
        {
            var text = code ?? string.Empty;
            ast = Parser.ParseInput(text, out tokens, out errors);
        }

        var functionDeclarationsByName = ast.FindAll(a => a is FunctionDefinitionAst, searchNestedScriptBlocks: true)
            .Cast<FunctionDefinitionAst>()
            .Where(f => !string.IsNullOrWhiteSpace(f.Name))
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var declaredFunctions = ast.FindAll(a => a is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .Select(f => f.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var commandNames = ExtractCommandNames(ast, functionDeclarationsByName).ToArray();

        return new ParsedInput(effectiveFilePath, declaredFunctions, commandNames);
    }

    private static IEnumerable<string> ExtractCommandNames(
        ScriptBlockAst ast,
        IReadOnlyDictionary<string, FunctionDefinitionAst[]> functionDeclarationsByName)
    {
        var adCmdlets = new HashSet<string>(new[]
        {
            "Get-ADComputer", "Get-ADUser", "Get-ADObject", "Get-ADDomainController", "Get-ADReplicationSubnet"
        }, StringComparer.OrdinalIgnoreCase);

        var reserved = new HashSet<string>(new[]
        {
            "if", "elseif", "else", "switch", "for", "foreach", "while", "do", "until",
            "try", "catch", "finally", "throw", "trap", "break", "continue", "return",
            "function", "filter", "workflow", "configuration", "class", "enum", "data",
            "param", "begin", "process", "end", "in", "using"
        }, StringComparer.OrdinalIgnoreCase);

        var redirection = new HashSet<string>(new[] { ">", ">>", "2>", "2>>", "|" }, StringComparer.OrdinalIgnoreCase);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allCommands = ast.FindAll(a => a is CommandAst, searchNestedScriptBlocks: true).Cast<CommandAst>();
        foreach (var cmd in allCommands)
        {
            var excludedByParent = false;
            for (Ast? node = cmd.Parent; node != null; node = node.Parent)
            {
                if (node is not CommandAst parentCmd)
                    continue;
                var parentName = parentCmd.GetCommandName();
                if (!string.IsNullOrWhiteSpace(parentName) && adCmdlets.Contains(parentName))
                {
                    excludedByParent = true;
                    break;
                }
            }
            if (excludedByParent)
                continue;

            var name = cmd.GetCommandName();
            if (string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    if (cmd.CommandElements.Count > 0 && cmd.CommandElements[0] is StringConstantExpressionAst s)
                        name = s.Value;
                }
                catch
                {
                    name = null;
                }
            }

            if (name is null)
                continue;

            name = name.Trim();
            if (name.Length == 0)
                continue;
            if (name.StartsWith("$", StringComparison.Ordinal))
                continue;
            if (reserved.Contains(name) || redirection.Contains(name))
                continue;
            if (!LooksLikeCommandName(name))
                continue;
            if (IsFunctionDeclaredInVisibleScope(cmd, name, functionDeclarationsByName))
                continue;

            set.Add(name);
        }

        return set.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsFunctionDeclaredInVisibleScope(
        CommandAst command,
        string commandName,
        IReadOnlyDictionary<string, FunctionDefinitionAst[]> functionDeclarationsByName)
    {
        if (!functionDeclarationsByName.TryGetValue(commandName, out var declarations))
            return false;

        foreach (var declaration in declarations)
        {
            var declarationScope = FindContainingScriptBlock(declaration);
            if (declarationScope is null || !IsUnconditionalDeclaration(declaration, declarationScope))
                continue;

            if (!TryGetDeferredEntryFunction(command, declaration, declarationScope, out var deferredEntryFunction))
                continue;

            // A function is visible to recursive calls in its own body once invoked.
            if (ReferenceEquals(deferredEntryFunction, declaration))
                return true;

            if (deferredEntryFunction is not null)
            {
                // Source position inside a nested function does not describe execution order.
                // The declaration is available unless that function is invoked directly while
                // the containing scope is still initializing and before the declaration runs.
                if (!IsInvokedBeforeDeclaration(deferredEntryFunction, declaration, declarationScope))
                    return true;

                continue;
            }

            // Commands executed directly while the containing scope initializes require the
            // declaration to have run first.
            if (declaration.Extent.EndOffset <= command.Extent.StartOffset)
                return true;
        }

        return false;
    }

    private static bool IsUnconditionalDeclaration(
        FunctionDefinitionAst declaration,
        ScriptBlockAst declarationScope)
    {
        // A direct named-block statement always executes when its containing script block
        // runs. Declarations nested in if/switch/loop/try blocks are path-dependent and
        // must remain visible to strict missing-command validation.
        return declaration.Parent is NamedBlockAst namedBlock &&
               ReferenceEquals(namedBlock.Parent, declarationScope);
    }

    private static bool TryGetDeferredEntryFunction(
        CommandAst command,
        FunctionDefinitionAst declaration,
        ScriptBlockAst declarationScope,
        out FunctionDefinitionAst? deferredEntryFunction)
    {
        deferredEntryFunction = null;

        for (Ast? current = command; current is not null; current = current.Parent)
        {
            if (current is ScriptBlockExpressionAst scriptBlockExpression &&
                IsEscapingOrIsolatedScriptBlock(scriptBlockExpression))
            {
                return false;
            }

            if (current is FunctionDefinitionAst function)
            {
                if (ReferenceEquals(function, declaration))
                {
                    deferredEntryFunction = declaration;
                    return true;
                }

                if (ReferenceEquals(FindContainingScriptBlock(function), declarationScope))
                    deferredEntryFunction = function;
            }

            if (ReferenceEquals(current, declarationScope))
                return true;
        }

        return false;
    }

    private static bool IsInvokedBeforeDeclaration(
        FunctionDefinitionAst deferredEntryFunction,
        FunctionDefinitionAst declaration,
        ScriptBlockAst declarationScope)
    {
        var invocations = declarationScope.FindAll(
                ast => ast is CommandAst command &&
                       string.Equals(command.GetCommandName(), deferredEntryFunction.Name, StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: true)
            .Cast<CommandAst>();

        foreach (var invocation in invocations)
        {
            if (invocation.Extent.StartOffset >= declaration.Extent.EndOffset)
                continue;

            if (ExecutesWhileScopeInitializes(invocation, declarationScope))
                return true;
        }

        return false;
    }

    private static bool ExecutesWhileScopeInitializes(CommandAst command, ScriptBlockAst declarationScope)
    {
        for (Ast? current = command.Parent; current is not null; current = current.Parent)
        {
            if (current is FunctionDefinitionAst)
                return false;

            if (current is ScriptBlockExpressionAst scriptBlockExpression &&
                IsEscapingOrIsolatedScriptBlock(scriptBlockExpression))
            {
                return false;
            }

            if (ReferenceEquals(current, declarationScope))
                return true;
        }

        return false;
    }

    private static bool IsEscapingOrIsolatedScriptBlock(ScriptBlockExpressionAst scriptBlockExpression)
    {
        // Assigned, returned, emitted, or otherwise materialized script blocks can outlive
        // the local function scope. Only script blocks passed directly to a command can be
        // considered inline, and known asynchronous/remote commands remain boundaries.
        if (scriptBlockExpression.Parent is not CommandAst invocation)
            return true;

        var invocationName = NormalizeInvocationName(invocation.GetCommandName());
        if (invocationName.Length == 0)
            return false; // call and dot invocation operators execute the block inline

        if (string.Equals(invocationName, "Start-Job", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Start-ThreadJob", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Start-RSJob", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-ObjectEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-EngineEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-CimIndicationEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-WmiEvent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(invocationName, "Invoke-Command", StringComparison.OrdinalIgnoreCase))
        {
            return HasAnyParameter(
                invocation,
                "ComputerName",
                "ConnectionUri",
                "Session",
                "HostName",
                "SSHConnection",
                "VMId",
                "VMName",
                "ContainerId");
        }

        return string.Equals(invocationName, "ForEach-Object", StringComparison.OrdinalIgnoreCase) &&
               HasAnyParameter(invocation, "Parallel");
    }

    private static string NormalizeInvocationName(string? invocationName)
    {
        if (string.IsNullOrWhiteSpace(invocationName))
            return string.Empty;

        var normalized = invocationName!.Trim();
        var moduleSeparator = normalized.LastIndexOf('\\');
        if (moduleSeparator >= 0 && moduleSeparator + 1 < normalized.Length)
            normalized = normalized.Substring(moduleSeparator + 1);

        if (string.Equals(normalized, "sajb", StringComparison.OrdinalIgnoreCase))
            return "Start-Job";
        if (string.Equals(normalized, "icm", StringComparison.OrdinalIgnoreCase))
            return "Invoke-Command";
        if (string.Equals(normalized, "%", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "foreach", StringComparison.OrdinalIgnoreCase))
        {
            return "ForEach-Object";
        }

        return normalized;
    }

    private static bool HasAnyParameter(CommandAst invocation, params string[] parameterNames)
    {
        foreach (var parameter in invocation.CommandElements.OfType<CommandParameterAst>())
        {
            var actualName = parameter.ParameterName;
            if (string.IsNullOrWhiteSpace(actualName))
                continue;

            if (parameterNames.Any(name => name.StartsWith(actualName, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static ScriptBlockAst? FindContainingScriptBlock(Ast ast)
    {
        for (Ast? current = ast.Parent; current is not null; current = current.Parent)
        {
            if (current is ScriptBlockAst scriptBlock)
                return scriptBlock;
        }

        return null;
    }

    private MissingFunctionCommand ResolveCommand(string name, HashSet<string> approvedModules)
    {
        var isAlias = false;

        try
        {
            var cmd = GetCommandFromCurrentSessionCached(name);
            if (cmd is null)
                throw new CommandNotFoundException($"The term '{name}' is not recognized as a name of a cmdlet, function, script file, or executable program.");

            if (string.Equals(cmd.Source, "PSPublishModule", StringComparison.OrdinalIgnoreCase) &&
                !approvedModules.Contains(cmd.Source))
            {
                throw new InvalidOperationException("Command comes from PSPublishModule which is not an approved module.");
            }

            if (cmd is AliasInfo alias)
            {
                isAlias = true;
                var def = alias.Definition;
                var resolved = GetCommandFromCurrentSessionCached(def);
                if (resolved != null)
                    cmd = resolved;
            }

            return new MissingFunctionCommand(
                name: cmd.Name,
                source: cmd.Source ?? string.Empty,
                commandType: cmd.CommandType == 0 ? string.Empty : cmd.CommandType.ToString(),
                isAlias: isAlias,
                isPrivate: false,
                error: string.Empty,
                scriptBlock: (cmd as FunctionInfo)?.ScriptBlock);
        }
        catch (Exception ex)
        {
            var resolution = new MissingFunctionCommand(
                name: name,
                source: string.Empty,
                commandType: string.Empty,
                isAlias: isAlias,
                isPrivate: false,
                error: ex.Message,
                scriptBlock: null);

            if (approvedModules.Count == 0)
                return resolution;

            foreach (var modName in approvedModules)
            {
                try
                {
                    var cmd = GetCommandFromModuleScopeCached(modName, name);
                    if (cmd is null)
                        continue;

                    resolution = new MissingFunctionCommand(
                        name: cmd.Name,
                        source: cmd.Source ?? string.Empty,
                        commandType: cmd.CommandType == 0 ? string.Empty : cmd.CommandType.ToString(),
                        isAlias: isAlias,
                        isPrivate: true,
                        error: string.Empty,
                        scriptBlock: (cmd as FunctionInfo)?.ScriptBlock);
                    break;
                }
                catch
                {
                    // keep trying other modules
                }
            }

            return resolution;
        }
    }

    private CommandInfo? GetCommandFromCurrentSessionCached(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (_currentSessionCommandCache.TryGetValue(name, out var cached))
            return cached;

        var resolved = GetCommandFromCurrentSession(name);
        _currentSessionCommandCache[name] = resolved;
        return resolved;
    }

    private CommandInfo? GetCommandFromModuleScopeCached(string moduleName, string commandName)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(commandName))
            return null;

        var key = moduleName.Trim() + "|" + commandName.Trim();
        if (_moduleScopeCommandCache.TryGetValue(key, out var cached))
            return cached;

        var resolved = GetCommandFromModuleScope(moduleName, commandName);
        _moduleScopeCommandCache[key] = resolved;
        return resolved;
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

    private static PowerShell CreatePowerShell()
    {
        if (Runspace.DefaultRunspace is null)
            return PowerShell.Create();
        return PowerShell.Create(RunspaceMode.CurrentRunspace);
    }

    private static CommandInfo? GetCommandFromCurrentSession(string name)
    {
        using var ps = CreatePowerShell();
        ps.AddCommand("Get-Command")
            .AddParameter("Name", name)
            .AddParameter("CommandType", CommandTypes.All)
            .AddParameter("ErrorAction", "Stop")
            .AddParameter("Verbose", false);

        var results = ps.Invoke();
        if (ps.HadErrors || results.Count == 0)
            return null;

        return results[0].BaseObject as CommandInfo;
    }

    private static CommandInfo? GetCommandFromModuleScope(string moduleName, string commandName)
    {
        using var ps = CreatePowerShell();
        var script = EmbeddedScripts.Load("Scripts/Analysis/Get-CommandFromModuleScope.ps1");
        ps.AddScript(script).AddArgument(moduleName).AddArgument(commandName);
        var results = ps.Invoke();
        if (ps.HadErrors || results.Count == 0)
            return null;

        return results[0].BaseObject as CommandInfo;
    }

    private sealed class ParsedInput
    {
        public ParsedInput(string? filePath, string[] functionNames, string[] commandNames)
        {
            FilePath = filePath;
            FunctionNames = functionNames;
            CommandNames = commandNames;
        }

        public string? FilePath { get; }
        public string[] FunctionNames { get; }
        public string[] CommandNames { get; }
    }
}
