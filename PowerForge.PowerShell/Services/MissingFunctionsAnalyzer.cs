using System;
using System.Collections;
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

    private readonly Dictionary<string, ModuleScopeSession> _moduleScopeSessions =
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

        var approvedSourceCandidates = (options.ApprovedModuleSources ?? Array.Empty<ApprovedModuleSource>())
            .Where(static source => source is not null &&
                                    !string.IsNullOrWhiteSpace(source.Name) &&
                                    !string.IsNullOrWhiteSpace(source.ModuleBasePath))
            .ToArray();
        var approvedSources = approvedSourceCandidates
            .GroupBy(static source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var approvedSourceOrder = approvedSourceCandidates
            .Select(static source => source.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => approvedSources[name])
            .ToArray();

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

        try
        {
            return AnalyzeInternal(
                filePath: filePath,
                code: code,
                knownFunctions: known,
                approvedModules: approved,
                approvedModuleSources: approvedSources,
                approvedModuleSourceOrder: approvedSourceOrder,
                requireApprovedModuleSources: options.RequireApprovedModuleSources,
                ignoreFunctions: ignore,
                includeFunctionsRecursively: options.IncludeFunctionsRecursively);
        }
        finally
        {
            DisposeModuleScopeSessions();
        }
    }

    private MissingFunctionsReport AnalyzeInternal(
        string? filePath,
        string? code,
        HashSet<string> knownFunctions,
        HashSet<string> approvedModules,
        IReadOnlyDictionary<string, ApprovedModuleSource> approvedModuleSources,
        IReadOnlyList<ApprovedModuleSource> approvedModuleSourceOrder,
        bool requireApprovedModuleSources,
        HashSet<string> ignoreFunctions,
        bool includeFunctionsRecursively,
        ApprovedModuleSource? preferredApprovedModuleSource = null)
    {
        var parsed = ParseInput(filePath, code, approvedModuleSources);

        var consumerFunctionsForNestedAnalysis = new HashSet<string>(knownFunctions, StringComparer.OrdinalIgnoreCase);
        foreach (var functionName in parsed.FunctionNames)
            consumerFunctionsForNestedAnalysis.Add(functionName);

        var commandNames = parsed.CommandNames.Where(n => !ignoreFunctions.Contains(n)).ToArray();
        var filteredNames = commandNames
            .Where(n => !knownFunctions.Contains(n) || ApprovedDonorDefinesCommand(
                n,
                approvedModuleSources,
                approvedModuleSourceOrder,
                preferredApprovedModuleSource))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var listCommands = new List<MissingFunctionCommand>();
        foreach (var name in filteredNames)
        {
            var info = ResolveCommand(
                name,
                approvedModules,
                approvedModuleSources,
                approvedModuleSourceOrder,
                requireApprovedModuleSources,
                preferredApprovedModuleSource);
            if (string.Equals(info.Source, "Microsoft.PowerShell.Core", StringComparison.OrdinalIgnoreCase))
                continue;

            listCommands.Add(info);
        }

        var functionsTop = BuildInlineFunctions(listCommands, approvedModules);
        var combinedSummary = new List<MissingFunctionCommand>(listCommands);
        var combinedSummaryFiltered = new List<MissingFunctionCommand>(listCommands);
        var combinedFunctions = new List<string>(functionsTop);
        var analysisComplete = !parsed.HasDynamicCommandInvocation;
        var nonInlineableApprovedModules = new HashSet<string>(
            parsed.NonInlineableApprovedModules,
            StringComparer.OrdinalIgnoreCase);

        if (functionsTop.Count > 0)
        {
            var ignoreNext = new HashSet<string>(ignoreFunctions, StringComparer.OrdinalIgnoreCase);
            foreach (var n in listCommands.Select(o => o.Name).Where(s => !string.IsNullOrWhiteSpace(s)))
                ignoreNext.Add(n);

            // Analyze each donor's functions in that donor's scope. Otherwise an unqualified
            // private helper can be stolen by an earlier approved module that happens to use
            // the same command name.
            var inlineableCommands = listCommands
                .Where(command => command.ScriptBlock is not null &&
                                  !string.IsNullOrWhiteSpace(command.Source) &&
                                  approvedModules.Contains(command.Source))
                .GroupBy(command => command.Source, StringComparer.OrdinalIgnoreCase);
            foreach (var donorGroup in inlineableCommands)
            {
                approvedModuleSources.TryGetValue(donorGroup.Key, out var donorSource);
                var donorFunctions = BuildInlineFunctions(donorGroup, approvedModules);
                var nested = AnalyzeInternal(
                    filePath: null,
                    code: string.Join(Environment.NewLine, donorFunctions),
                    // Inlined donor functions execute in the completed consumer module, so references to
                    // consumer-local functions are already satisfied even though those declarations are not
                    // repeated in this recursive analysis fragment.
                    knownFunctions: consumerFunctionsForNestedAnalysis,
                    approvedModules: approvedModules,
                    approvedModuleSources: approvedModuleSources,
                    approvedModuleSourceOrder: approvedModuleSourceOrder,
                    requireApprovedModuleSources: requireApprovedModuleSources,
                    ignoreFunctions: ignoreNext,
                    includeFunctionsRecursively: includeFunctionsRecursively,
                    preferredApprovedModuleSource: donorSource);

                combinedSummary.AddRange(nested.Summary);
                combinedSummaryFiltered.AddRange(nested.SummaryFiltered);
                analysisComplete &= nested.AnalysisComplete;
                nonInlineableApprovedModules.UnionWith(nested.NonInlineableApprovedModules);

                if (includeFunctionsRecursively)
                    combinedFunctions.AddRange(nested.Functions);
            }
        }

        return new MissingFunctionsReport(
            summary: combinedSummary.ToArray(),
            summaryFiltered: combinedSummaryFiltered.ToArray(),
            functions: combinedFunctions.ToArray(),
            functionsTopLevelOnly: functionsTop.ToArray(),
            analysisComplete: analysisComplete,
            nonInlineableApprovedModules: nonInlineableApprovedModules
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
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

    private static ParsedInput ParseInput(
        string? filePath,
        string? code,
        IReadOnlyDictionary<string, ApprovedModuleSource> approvedModuleSources)
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
            .GroupBy(
                f => NestedFunctionVisibilityAnalyzer.NormalizeDeclaredFunctionName(f.Name),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var declaredFunctions = ast.FindAll(a => a is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .Select(f => NestedFunctionVisibilityAnalyzer.NormalizeDeclaredFunctionName(f.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var visibilityAnalyzer = new NestedFunctionVisibilityAnalyzer(ast, functionDeclarationsByName);
        var allCommands = ast.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .ToArray();
        var commandNames = ExtractCommandNames(allCommands, visibilityAnalyzer).ToArray();
        var hasDynamicCommandInvocation = allCommands.Any(static command =>
            (string.IsNullOrWhiteSpace(command.GetCommandName()) &&
             (command.CommandElements.Count == 0 || command.CommandElements[0] is not StringConstantExpressionAst)) ||
            string.Equals(command.GetCommandName(), "Invoke-Expression", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command.GetCommandName(), "iex", StringComparison.OrdinalIgnoreCase));
        if (!hasDynamicCommandInvocation)
        {
            hasDynamicCommandInvocation = ast.FindAll(static node =>
                    node is InvokeMemberExpressionAst invocation && IsRuntimeCodeInvocation(invocation),
                    searchNestedScriptBlocks: true)
                .Any();
        }

        return new ParsedInput(
            effectiveFilePath,
            declaredFunctions,
            commandNames,
            hasDynamicCommandInvocation,
            ApprovedModuleRuntimeReferenceAnalyzer.Find(ast, commandNames, approvedModuleSources));
    }

    private static bool IsRuntimeCodeInvocation(InvokeMemberExpressionAst invocation)
    {
        var memberName = invocation.Member?.Extent?.Text?.Trim(' ', '\'', '"');
        return string.Equals(memberName, "Invoke", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(memberName, "InvokeReturnAsIs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(memberName, "InvokeWithContext", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(memberName, "InvokeScript", StringComparison.OrdinalIgnoreCase) ||
               (string.Equals(memberName, "Create", StringComparison.OrdinalIgnoreCase) &&
                 invocation.Expression?.Extent?.Text?.IndexOf("scriptblock", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string? GetModuleQualifier(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return null;

        var separator = commandName.IndexOf('\\');
        return separator > 0 ? commandName.Substring(0, separator).Trim() : null;
    }

    private static string GetUnqualifiedCommandName(string commandName)
    {
        var separator = commandName.IndexOf('\\');
        return separator >= 0 && separator < commandName.Length - 1
            ? commandName.Substring(separator + 1)
            : commandName;
    }

    private static IEnumerable<string> ExtractCommandNames(
        IEnumerable<CommandAst> allCommands,
        NestedFunctionVisibilityAnalyzer visibilityAnalyzer)
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
            if (visibilityAnalyzer.IsDeclaredInVisibleScope(cmd, name))
                continue;

            set.Add(name);
        }

        return set.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
    }

    private MissingFunctionCommand ResolveCommand(
        string name,
        HashSet<string> approvedModules,
        IReadOnlyDictionary<string, ApprovedModuleSource> approvedModuleSources,
        IReadOnlyList<ApprovedModuleSource> approvedModuleSourceOrder,
        bool requireApprovedModuleSources,
        ApprovedModuleSource? preferredApprovedModuleSource)
    {
        var isAlias = false;

        try
        {
            var qualifier = GetModuleQualifier(name);
            var lookupName = GetUnqualifiedCommandName(name);
            if (qualifier is not null && approvedModuleSources.TryGetValue(qualifier, out var qualifiedSource))
            {
                var qualifiedCommand = GetCommandFromModuleScopeCached(qualifiedSource, lookupName);
                if (qualifiedCommand is null)
                {
                    throw new CommandNotFoundException(
                        $"The command '{name}' was not found in the selected module '{qualifiedSource.Name}' {qualifiedSource.Version ?? "(version unknown)"} at '{qualifiedSource.ModuleBasePath}'.");
                }

                return CreateResolution(qualifiedCommand, isAlias: qualifiedCommand is AliasInfo, isPrivate: true);
            }

            if (qualifier is null && approvedModuleSources.Count > 0)
            {
                foreach (var source in EnumerateApprovedModuleSources(
                             approvedModuleSourceOrder,
                             preferredApprovedModuleSource))
                {
                    var donorCommand = GetCommandFromModuleScopeCached(source, lookupName);
                    if (donorCommand is null)
                        continue;

                    return CreateResolution(donorCommand, isAlias: donorCommand is AliasInfo, isPrivate: true);
                }
            }

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

            if (requireApprovedModuleSources &&
                !string.IsNullOrWhiteSpace(cmd.Source) &&
                approvedModules.Contains(cmd.Source) &&
                !approvedModuleSources.ContainsKey(cmd.Source))
            {
                throw new InvalidOperationException(
                    $"Approved module '{cmd.Source}' has no concrete source binding for deterministic helper analysis.");
            }

            if (!string.IsNullOrWhiteSpace(cmd.Source) &&
                approvedModuleSources.TryGetValue(cmd.Source, out var selectedSource))
            {
                var selectedCommand = GetCommandFromModuleScopeCached(selectedSource, name);
                if (selectedCommand is null)
                {
                    throw new CommandNotFoundException(
                        $"The command '{name}' was not found in the selected module '{selectedSource.Name}' {selectedSource.Version ?? "(version unknown)"} at '{selectedSource.ModuleBasePath}'.");
                }

                cmd = selectedCommand;
            }

            return CreateResolution(cmd, isAlias, isPrivate: false);
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
                if (approvedModuleSources.ContainsKey(modName))
                    continue;

                if (requireApprovedModuleSources && !approvedModuleSources.ContainsKey(modName))
                {
                    resolution = new MissingFunctionCommand(
                        name: name,
                        source: modName,
                        commandType: string.Empty,
                        isAlias: isAlias,
                        isPrivate: true,
                        error: $"Approved module '{modName}' has no concrete source binding for deterministic helper analysis.",
                        scriptBlock: null);
                    continue;
                }

                try
                {
                    var source = approvedModuleSources.TryGetValue(modName, out var selectedSource)
                        ? selectedSource
                        : new ApprovedModuleSource(modName, version: null, moduleBasePath: string.Empty);
                    var cmd = GetCommandFromModuleScopeCached(source, name);
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
                catch (Exception moduleScopeException)
                {
                    resolution = new MissingFunctionCommand(
                        name: name,
                        source: modName,
                        commandType: string.Empty,
                        isAlias: isAlias,
                        isPrivate: true,
                        error: moduleScopeException.Message,
                        scriptBlock: null);
                }
            }

            return resolution;
        }
    }

    private bool ApprovedDonorDefinesCommand(
        string name,
        IReadOnlyDictionary<string, ApprovedModuleSource> approvedModuleSources,
        IReadOnlyList<ApprovedModuleSource> approvedModuleSourceOrder,
        ApprovedModuleSource? preferredApprovedModuleSource)
    {
        var qualifier = GetModuleQualifier(name);
        var lookupName = GetUnqualifiedCommandName(name);
        if (qualifier is not null)
        {
            return approvedModuleSources.TryGetValue(qualifier, out var qualifiedSource) &&
                   GetCommandFromModuleScopeCached(qualifiedSource, lookupName) is not null;
        }

        return EnumerateApprovedModuleSources(approvedModuleSourceOrder, preferredApprovedModuleSource).Any(source =>
            GetCommandFromModuleScopeCached(source, lookupName) is not null);
    }

    private static IEnumerable<ApprovedModuleSource> EnumerateApprovedModuleSources(
        IReadOnlyList<ApprovedModuleSource> sources,
        ApprovedModuleSource? preferred)
    {
        if (preferred is not null)
            yield return preferred;

        foreach (var source in sources ?? Array.Empty<ApprovedModuleSource>())
        {
            if (preferred is not null &&
                string.Equals(source.Name, preferred.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(source.ModuleBasePath, preferred.ModuleBasePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return source;
        }
    }

    private static MissingFunctionCommand CreateResolution(CommandInfo command, bool isAlias, bool isPrivate)
        => new(
            name: command.Name,
            source: command.Source ?? string.Empty,
            commandType: command.CommandType == 0 ? string.Empty : command.CommandType.ToString(),
            isAlias: isAlias,
            isPrivate: isPrivate,
            error: string.Empty,
            scriptBlock: (command as FunctionInfo)?.ScriptBlock);

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

    private CommandInfo? GetCommandFromModuleScopeCached(ApprovedModuleSource source, string commandName)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Name) || string.IsNullOrWhiteSpace(commandName))
            return null;

        var key = source.Name.Trim() + "|" + (source.Version ?? string.Empty) + "|" + source.ModuleBasePath + "|" + commandName.Trim();
        if (_moduleScopeCommandCache.TryGetValue(key, out var cached))
            return cached;

        var resolved = GetCommandFromModuleScope(source, commandName);
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

    private static bool CommandBelongsToModule(CommandInfo? command, string moduleName)
        => command is not null &&
           (string.Equals(command.Source, moduleName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));

    private CommandInfo? GetCommandFromModuleScope(ApprovedModuleSource source, string commandName)
    {
        var session = GetOrCreateModuleScopeSession(source);
        using var ps = PowerShell.Create();
        ps.Runspace = session.Runspace;
        var script = EmbeddedScripts.Load("Scripts/Analysis/Get-CommandFromModuleScope.ps1");
        ps.AddScript(script).AddArgument(session.ModuleBase).AddArgument(commandName);
        var results = ps.Invoke();
        if (ps.HadErrors || results.Count == 0)
            return null;

        var command = results[0].BaseObject as CommandInfo;
        return CommandBelongsToModule(command, source.Name) ? command : null;
    }

    private ModuleScopeSession GetOrCreateModuleScopeSession(ApprovedModuleSource source)
    {
        var key = source.Name.Trim() + "|" + (source.Version ?? string.Empty) + "|" + source.ModuleBasePath + "|" + (source.ModuleSearchRoot ?? string.Empty);
        if (_moduleScopeSessions.TryGetValue(key, out var existing))
            return existing;

        var initialSessionState = InitialSessionState.CreateDefault();
        initialSessionState.AuthorizationManager = new AuthorizationManager("PowerForge");
        var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        try
        {
            if (!string.IsNullOrWhiteSpace(source.ModuleSearchRoot))
            {
                using var pathPowerShell = PowerShell.Create();
                pathPowerShell.Runspace = runspace;
                pathPowerShell
                    .AddScript("$env:PSModulePath = $args[0] + [IO.Path]::PathSeparator + $env:PSModulePath")
                    .AddArgument(source.ModuleSearchRoot);
                pathPowerShell.Invoke();
                if (pathPowerShell.HadErrors)
                    throw new InvalidOperationException($"Approved module search root '{source.ModuleSearchRoot}' could not be added to PSModulePath.");
            }

            using var ps = PowerShell.Create();
            ps.Runspace = runspace;
            var moduleReference = ResolveModuleReference(source);
            var imported = ps.AddCommand("Import-Module")
                .AddParameter("Name", moduleReference)
                .AddParameter("PassThru", true)
                .AddParameter("Force", true)
                .AddParameter("ErrorAction", "Stop")
                .AddParameter("Verbose", false)
                .Invoke()
                .Select(static result => result.BaseObject)
                .OfType<PSModuleInfo>()
                .LastOrDefault();
            if (ps.HadErrors || imported is null)
                throw new InvalidOperationException($"Approved module '{source.Name}' could not be imported from '{moduleReference}'.");
            if (!string.Equals(imported.Name, source.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Imported module '{imported.Name}' does not match expected module '{source.Name}'.");
            if (!string.IsNullOrWhiteSpace(source.Guid) &&
                (!System.Guid.TryParse(source.Guid, out var expectedGuid) || imported.Guid != expectedGuid))
            {
                throw new InvalidOperationException(
                    $"Imported module '{source.Name}' GUID '{imported.Guid}' does not match expected GUID '{source.Guid}'.");
            }
            if (!string.IsNullOrWhiteSpace(source.Version) && !ModuleVersionMatches(imported, source.Version!))
            {
                throw new InvalidOperationException(
                    $"Imported module '{source.Name}' version '{FormatImportedModuleVersion(imported)}' does not match expected version '{source.Version}'.");
            }

            var created = new ModuleScopeSession(runspace, imported.ModuleBase);
            _moduleScopeSessions[key] = created;
            return created;
        }
        catch
        {
            runspace.Dispose();
            throw;
        }
    }

    private static bool ModuleVersionMatches(PSModuleInfo module, string expectedVersion)
    {
        var expected = (expectedVersion ?? string.Empty).Trim();
        var metadataSeparator = expected.IndexOf('+');
        if (metadataSeparator >= 0)
            expected = expected.Substring(0, metadataSeparator);

        var prereleaseSeparator = expected.IndexOf('-');
        var expectedBase = prereleaseSeparator >= 0 ? expected.Substring(0, prereleaseSeparator) : expected;
        var expectedPrerelease = prereleaseSeparator >= 0 ? expected.Substring(prereleaseSeparator + 1) : string.Empty;
        if (!Version.TryParse(expectedBase, out var parsedExpected) || module.Version is null || module.Version != parsedExpected)
            return false;

        return string.Equals(GetModulePrerelease(module), expectedPrerelease, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatImportedModuleVersion(PSModuleInfo module)
    {
        var baseVersion = module.Version?.ToString() ?? string.Empty;
        var prerelease = GetModulePrerelease(module);
        return string.IsNullOrWhiteSpace(prerelease) ? baseVersion : $"{baseVersion}-{prerelease}";
    }

    private static string GetModulePrerelease(PSModuleInfo module)
    {
        if (module.PrivateData is not IDictionary privateData)
            return string.Empty;
        var psData = GetDictionaryValue(privateData, "PSData") as IDictionary;
        var prerelease = psData is null ? null : GetDictionaryValue(psData, "Prerelease")?.ToString();
        return (prerelease ?? string.Empty).Trim().TrimStart('-');
    }

    private static object? GetDictionaryValue(IDictionary dictionary, string key)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            if (string.Equals(entry.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return null;
    }

    private static string ResolveModuleReference(ApprovedModuleSource source)
    {
        if (string.IsNullOrWhiteSpace(source.ModuleBasePath))
            return source.Name;

        foreach (var extension in new[] { ".psd1", ".psm1", ".dll" })
        {
            var modulePath = Path.Combine(source.ModuleBasePath, source.Name + extension);
            if (File.Exists(modulePath))
                return modulePath;
        }

        throw new FileNotFoundException(
            $"The selected approved module '{source.Name}' entry point was not found under '{source.ModuleBasePath}'. Expected {source.Name}.psd1, {source.Name}.psm1, or {source.Name}.dll.",
            source.ModuleBasePath);
    }

    private void DisposeModuleScopeSessions()
    {
        foreach (var session in _moduleScopeSessions.Values)
            session.Dispose();
        _moduleScopeSessions.Clear();
        _moduleScopeCommandCache.Clear();
        _currentSessionCommandCache.Clear();
    }

    private sealed class ModuleScopeSession : IDisposable
    {
        internal Runspace Runspace { get; }
        internal string ModuleBase { get; }

        internal ModuleScopeSession(Runspace runspace, string moduleBase)
        {
            Runspace = runspace;
            ModuleBase = moduleBase;
        }

        public void Dispose() => Runspace.Dispose();
    }

    private sealed class ParsedInput
    {
        public ParsedInput(
            string? filePath,
            string[] functionNames,
            string[] commandNames,
            bool hasDynamicCommandInvocation,
            string[] nonInlineableApprovedModules)
        {
            FilePath = filePath;
            FunctionNames = functionNames;
            CommandNames = commandNames;
            HasDynamicCommandInvocation = hasDynamicCommandInvocation;
            NonInlineableApprovedModules = nonInlineableApprovedModules ?? Array.Empty<string>();
        }

        public string? FilePath { get; }
        public string[] FunctionNames { get; }
        public string[] CommandNames { get; }
        public bool HasDynamicCommandInvocation { get; }
        public string[] NonInlineableApprovedModules { get; }
    }
}
