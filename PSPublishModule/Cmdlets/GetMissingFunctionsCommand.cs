using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;

namespace PSPublishModule;

/// <summary>
/// Analyzes a script or scriptblock and reports functions/commands it calls that are not present.
/// </summary>
[Cmdlet(VerbsCommon.Get, "MissingFunctions", DefaultParameterSetName = ParameterSetFile)]
public sealed class GetMissingFunctionsCommand : PSCmdlet
{
    private const string ParameterSetFile = "File";
    private const string ParameterSetCode = "Code";

    /// <summary>Path to a script file to analyze for missing function dependencies. Alias: Path.</summary>
    [Parameter(ParameterSetName = ParameterSetFile)]
    [Alias("Path")]
    public string? FilePath { get; set; }

    /// <summary>ScriptBlock to analyze instead of a file. Alias: ScriptBlock.</summary>
    [Parameter(ParameterSetName = ParameterSetCode)]
    [Alias("ScriptBlock")]
    public ScriptBlock? Code { get; set; }

    /// <summary>Known function names to treat as already available (exclude from missing list).</summary>
    [Parameter]
    public string[] Functions { get; set; } = Array.Empty<string>();

    /// <summary>Return only a flattened summary list of functions used (objects with Name/Source), not inlined definitions.</summary>
    [Parameter]
    public SwitchParameter Summary { get; set; }

    /// <summary>Return a hashtable with Summary (objects), SummaryFiltered (objects), and Functions (inlineable text).</summary>
    [Parameter]
    public SwitchParameter SummaryWithCommands { get; set; }

    /// <summary>Module names that are allowed sources for pulling inline helper function definitions.</summary>
    [Parameter]
    public string[] ApprovedModules { get; set; } = Array.Empty<string>();

    /// <summary>Function names to ignore when computing the missing set.</summary>
    [Parameter]
    public string[] IgnoreFunctions { get; set; } = Array.Empty<string>();

    /// <summary>Executes the analysis.</summary>
    protected override void ProcessRecord()
    {
        if (string.IsNullOrWhiteSpace(FilePath) && Code is null)
            return;

        var approved = new HashSet<string>(ApprovedModules.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()), StringComparer.OrdinalIgnoreCase);
        var ignore = new HashSet<string>(IgnoreFunctions.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        var functions = new HashSet<string>(Functions.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);

        var res = Analyze(FilePath, Code, functions, approved, ignore, includeFunctionsRecursively: SummaryWithCommands.IsPresent);

        if (SummaryWithCommands.IsPresent)
        {
            var ht = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Summary"] = res.Summary.ToArray(),
                ["SummaryFiltered"] = res.SummaryFiltered.ToArray(),
                ["Functions"] = res.Functions.ToArray()
            };
            WriteObject(ht, enumerateCollection: false);
            return;
        }

        if (Summary.IsPresent)
        {
            foreach (var o in res.SummaryFiltered)
                WriteObject(o, enumerateCollection: false);
            return;
        }

        foreach (var line in res.FunctionsTopLevelOnly)
            WriteObject(line, enumerateCollection: false);
    }

    private AnalysisResult Analyze(string? filePath, ScriptBlock? code, HashSet<string> knownFunctions, HashSet<string> approvedModules, HashSet<string> ignoreFunctions, bool includeFunctionsRecursively)
    {
        var parsed = ParseInput(filePath, code);

        var declaredFunctions = parsed.FunctionNames;
        var excludeFunctions = new HashSet<string>(knownFunctions, StringComparer.OrdinalIgnoreCase);
        foreach (var fn in declaredFunctions) excludeFunctions.Add(fn);

        var commandNames = parsed.CommandNames.Where(n => !ignoreFunctions.Contains(n)).ToArray();
        var filteredNames = commandNames.Where(n => !excludeFunctions.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

        var listCommands = new List<PSObject>();
        foreach (var name in filteredNames)
        {
            var info = ResolveCommand(name, approvedModules);
            if (string.Equals(info.Source, "Microsoft.PowerShell.Core", StringComparison.OrdinalIgnoreCase))
                continue;

            listCommands.Add(ToPsCustomObject(info));
        }

        var functionsTop = BuildInlineFunctions(listCommands, approvedModules);
        var combinedSummary = new List<PSObject>(listCommands);
        var combinedSummaryFiltered = new List<PSObject>(listCommands);
        var combinedFunctions = new List<string>(functionsTop);

        if (functionsTop.Count > 0)
        {
            var ignoreNext = new HashSet<string>(ignoreFunctions, StringComparer.OrdinalIgnoreCase);
            foreach (var n in listCommands.Select(o => o.Properties["Name"]?.Value?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)))
                ignoreNext.Add(n!);

            var nested = Analyze(
                filePath: null,
                code: ScriptBlock.Create(string.Join(Environment.NewLine, functionsTop)),
                knownFunctions: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                approvedModules: approvedModules,
                ignoreFunctions: ignoreNext,
                includeFunctionsRecursively: includeFunctionsRecursively);

            // SummaryWithCommands includes nested functions, Summary does not.
            combinedSummary.AddRange(nested.Summary);
            combinedSummaryFiltered.AddRange(nested.SummaryFiltered);

            if (includeFunctionsRecursively)
                combinedFunctions.AddRange(nested.Functions);
        }

        return new AnalysisResult
        {
            Summary = combinedSummary,
            SummaryFiltered = combinedSummaryFiltered,
            Functions = combinedFunctions,
            FunctionsTopLevelOnly = functionsTop
        };
    }

    private static List<string> BuildInlineFunctions(IEnumerable<PSObject> commands, HashSet<string> approvedModules)
    {
        var output = new List<string>();
        if (approvedModules.Count == 0)
            return output;

        foreach (var c in commands)
        {
            var name = c.Properties["Name"]?.Value?.ToString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var source = c.Properties["Source"]?.Value?.ToString();
            if (source is null)
                continue;
            source = source.Trim();
            if (source.Length == 0)
                continue;
            if (!approvedModules.Contains(source))
                continue;

            var sb = c.Properties["ScriptBlock"]?.Value as ScriptBlock;
            if (sb is null)
                continue;

            output.Add($"function {name} {{ {sb} }}");
        }

        return output;
    }

    private ParsedInput ParseInput(string? filePath, ScriptBlock? code)
    {
        string? effectiveFilePath = null;
        ScriptBlockAst ast;
        Token[] tokens;
        ParseError[] errors;

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            effectiveFilePath = System.IO.Path.GetFullPath(filePath!.Trim().Trim('"'));
            ast = Parser.ParseFile(effectiveFilePath, out tokens, out errors);
        }
        else
        {
            var text = code?.ToString() ?? string.Empty;
            ast = Parser.ParseInput(text, out tokens, out errors);
        }

        // Collect declared top-level functions (mirrors legacy FindAll(..., $false))
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
            // Filter-out AD cmdlet filter scriptblocks to avoid including filter variables as commands.
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

    private CommandResolution ResolveCommand(string name, HashSet<string> approvedModules)
    {
        var isAlias = false;

        try
        {
            var cmd = InvokeCommand.GetCommand(name, CommandTypes.All);
            if (cmd is null)
                throw new CommandNotFoundException($"The term '{name}' is not recognized as a name of a cmdlet, function, script file, or executable program.");

            // Exclude PSPublishModule commands unless explicitly approved to avoid self-shadowing.
            if (string.Equals(cmd.Source, "PSPublishModule", StringComparison.OrdinalIgnoreCase) &&
                !approvedModules.Contains(cmd.Source))
            {
                throw new InvalidOperationException("Command comes from PSPublishModule which is not an approved module.");
            }

            if (cmd is AliasInfo alias)
            {
                isAlias = true;
                var def = alias.Definition;
                var resolved = InvokeCommand.GetCommand(def, CommandTypes.All);
                if (resolved != null)
                    cmd = resolved;
            }

            return new CommandResolution
            {
                Name = cmd.Name,
                Source = cmd.Source ?? string.Empty,
                CommandType = cmd.CommandType,
                IsAlias = isAlias,
                IsPrivate = false,
                Error = string.Empty,
                ScriptBlock = (cmd as FunctionInfo)?.ScriptBlock
            };
        }
        catch (Exception ex)
        {
            var resolution = new CommandResolution
            {
                Name = name,
                Source = string.Empty,
                CommandType = 0,
                IsAlias = isAlias,
                IsPrivate = false,
                Error = ex.Message,
                ScriptBlock = null
            };

            if (approvedModules.Count == 0)
                return resolution;

            // Try to resolve inside approved modules including private functions.
            foreach (var modName in approvedModules)
            {
                try
                {
                    var cmd = GetCommandFromModuleScope(modName, name);
                    if (cmd is null)
                        continue;

                    resolution.Name = cmd.Name;
                    resolution.Source = cmd.Source ?? string.Empty;
                    resolution.CommandType = cmd.CommandType;
                    resolution.IsPrivate = true;
                    resolution.Error = string.Empty;
                    resolution.ScriptBlock = (cmd as FunctionInfo)?.ScriptBlock;
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

    private CommandInfo? GetCommandFromModuleScope(string moduleName, string commandName)
    {
        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        var script = @"
param($moduleName, $commandName)
$m = Import-Module -Name $moduleName -PassThru -ErrorAction Stop -Verbose:$false
& $m { param($c) Get-Command $c -ErrorAction Stop -Verbose:$false } $commandName
";
        ps.AddScript(script).AddArgument(moduleName).AddArgument(commandName);
        var results = ps.Invoke();
        if (ps.HadErrors)
            return null;

        if (results.Count == 0)
            return null;

        var cmd = results[0].BaseObject as CommandInfo;
        return cmd;
    }

    private static PSObject ToPsCustomObject(CommandResolution r)
    {
        var o = NewPsCustomObject();
        o.Properties.Add(new PSNoteProperty("Name", r.Name));
        o.Properties.Add(new PSNoteProperty("Source", r.Source));
        o.Properties.Add(new PSNoteProperty("CommandType", r.CommandType == 0 ? string.Empty : r.CommandType.ToString()));
        o.Properties.Add(new PSNoteProperty("IsAlias", r.IsAlias));
        o.Properties.Add(new PSNoteProperty("IsPrivate", r.IsPrivate));
        o.Properties.Add(new PSNoteProperty("Error", r.Error));
        o.Properties.Add(new PSNoteProperty("ScriptBlock", r.ScriptBlock));
        return o;
    }

    private static PSObject NewPsCustomObject()
    {
        var t = typeof(PSObject).Assembly.GetType("System.Management.Automation.PSCustomObject");
        if (t is null)
            return new PSObject();

        var inst = Activator.CreateInstance(t, nonPublic: true);
        return inst is PSObject pso ? pso : PSObject.AsPSObject(inst);
    }

    private sealed class AnalysisResult
    {
        public List<PSObject> Summary { get; set; } = new List<PSObject>();
        public List<PSObject> SummaryFiltered { get; set; } = new List<PSObject>();
        public List<string> Functions { get; set; } = new List<string>();
        public List<string> FunctionsTopLevelOnly { get; set; } = new List<string>();
    }

    private sealed class CommandResolution
    {
        public string Name { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public CommandTypes CommandType { get; set; }
        public bool IsAlias { get; set; }
        public bool IsPrivate { get; set; }
        public string Error { get; set; } = string.Empty;
        public ScriptBlock? ScriptBlock { get; set; }
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
