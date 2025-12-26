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

        var declaredFunctions = ast.FindAll(a => a is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .Select(f => f.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var commandNames = ExtractCommandNames(ast).ToArray();

        return new ParsedInput(effectiveFilePath, declaredFunctions, commandNames);
    }

    private static IEnumerable<string> ExtractCommandNames(ScriptBlockAst ast)
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
                    else if (cmd.CommandElements.Count > 0)
                        name = cmd.CommandElements[0].Extent?.Text;
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
            if (reserved.Contains(name) || redirection.Contains(name))
                continue;

            set.Add(name);
        }

        return set.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
    }

    private MissingFunctionCommand ResolveCommand(string name, HashSet<string> approvedModules)
    {
        var isAlias = false;

        try
        {
            var cmd = GetCommandFromCurrentSession(name);
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
                var resolved = GetCommandFromCurrentSession(def);
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
                    var cmd = GetCommandFromModuleScope(modName, name);
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

    private static CommandInfo? GetCommandFromCurrentSession(string name)
    {
        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
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
        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        var script = @"
param($moduleName, $commandName)
$m = Import-Module -Name $moduleName -PassThru -ErrorAction Stop -Verbose:$false
& $m { param($c) Get-Command $c -ErrorAction Stop -Verbose:$false } $commandName
";
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
