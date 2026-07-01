using System.Collections;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;

namespace PowerForge;

/// <summary>
/// Runtime used by locally scoped benchmark DSL helper functions.
/// </summary>
public static class PowerShellBenchmarkDslRuntime
{
    private static readonly AsyncLocal<PowerShellBenchmarkDslContext?> Current = new();

    /// <summary>
    /// Evaluates a script block with benchmark DSL helper functions.
    /// </summary>
    /// <param name="scriptBlock">Benchmark script block.</param>
    /// <param name="scriptRoot">Optional script root exposed as <c>$PSScriptRoot</c>.</param>
    /// <returns>Declared benchmark suites.</returns>
    public static PowerShellBenchmarkSuite[] Evaluate(ScriptBlock scriptBlock, string? scriptRoot = null)
    {
        if (scriptBlock is null) throw new ArgumentNullException(nameof(scriptBlock));
        var previous = Current.Value;
        var context = new PowerShellBenchmarkDslContext();
        Current.Value = context;
        Runspace? createdRunspace = null;
        var previousRunspace = Runspace.DefaultRunspace;
        AliasSnapshot? compareAlias = null;
        try
        {
            if (Runspace.DefaultRunspace is null)
            {
                createdRunspace = RunspaceFactory.CreateRunspace();
                createdRunspace.Open();
                Runspace.DefaultRunspace = createdRunspace;
            }

            compareAlias = RemoveAlias("compare");
            var variables = new List<PSVariable>
            {
                new("ErrorActionPreference", ActionPreference.Stop),
                new("PSNativeCommandUseErrorActionPreference", true)
            };
            if (!string.IsNullOrWhiteSpace(scriptRoot))
                variables.Add(new PSVariable("PSScriptRoot", scriptRoot));
            scriptBlock.InvokeWithContext(CreateFunctions(), variables, Array.Empty<object>());
            return context.Suites.ToArray();
        }
        finally
        {
            RestoreAlias(compareAlias);
            Runspace.DefaultRunspace = previousRunspace;
            createdRunspace?.Dispose();
            Current.Value = previous;
        }
    }

    /// <summary>
    /// Adds a benchmark suite from the local DSL.
    /// </summary>
    /// <param name="name">Suite name.</param>
    /// <param name="outputRoot">Output root.</param>
    /// <param name="scriptBlock">Suite body.</param>
    public static void Benchmark(string name, string? outputRoot, ScriptBlock scriptBlock)
    {
        var context = RequireContext();
        var suite = new PowerShellBenchmarkSuite
        {
            Name = RequireName(name, "benchmark name"),
            OutputRoot = string.IsNullOrWhiteSpace(outputRoot) ? Path.Combine("Build", "Benchmarks", RequireName(name, "benchmark name")) : outputRoot!.Trim()
        };
        context.Suites.Add(suite);
        context.SuiteStack.Push(suite);
        try
        {
            InvokeDslBlock(scriptBlock);
        }
        finally
        {
            context.SuiteStack.Pop();
        }
    }

    /// <summary>
    /// Evaluates a cases block.
    /// </summary>
    /// <param name="scriptBlock">Cases body.</param>
    public static void Cases(ScriptBlock scriptBlock) => InvokeDslBlock(scriptBlock);

    /// <summary>
    /// Adds one case.
    /// </summary>
    /// <param name="name">Case name.</param>
    /// <param name="values">Case values.</param>
    public static void Case(string name, Hashtable? values)
    {
        var suite = RequireSuite();
        var item = new PowerShellBenchmarkCase { Name = RequireName(name, "case name") };
        if (values is not null)
        {
            foreach (DictionaryEntry entry in values)
                item.Values[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = entry.Value;
        }

        suite.Cases.Add(item);
    }

    /// <summary>
    /// Adds generated cases from a script block.
    /// </summary>
    /// <param name="scriptBlock">Case source script block.</param>
    public static void From(ScriptBlock scriptBlock)
    {
        var suite = RequireSuite();
        foreach (var value in InvokeStrict(scriptBlock))
        {
            var item = ConvertToCase(value);
            suite.Cases.Add(item);
        }
    }

    /// <summary>
    /// Adds an axis.
    /// </summary>
    /// <param name="name">Axis name.</param>
    /// <param name="values">Axis values.</param>
    public static void Axis(string name, object?[] values)
    {
        var suite = RequireSuite();
        var axis = new PowerShellBenchmarkAxis { Name = RequireName(name, "axis name") };
        axis.Values.AddRange(Flatten(values));
        suite.Axes.Add(axis);
    }

    /// <summary>
    /// Sets suite setup block.
    /// </summary>
    /// <param name="scriptBlock">Setup block.</param>
    public static void Setup(ScriptBlock scriptBlock) => RequireSuite().Setup = scriptBlock;

    /// <summary>
    /// Sets suite data block.
    /// </summary>
    /// <param name="scriptBlock">Data block.</param>
    public static void Data(ScriptBlock scriptBlock) => RequireSuite().Data = scriptBlock;

    /// <summary>
    /// Sets suite skip block.
    /// </summary>
    /// <param name="scriptBlock">Skip block.</param>
    public static void Skip(ScriptBlock scriptBlock) => RequireSuite().Skip = scriptBlock;

    /// <summary>
    /// Sets suite validation block.
    /// </summary>
    /// <param name="scriptBlock">Validation block.</param>
    public static void Validate(ScriptBlock scriptBlock) => RequireSuite().Validate = scriptBlock;

    /// <summary>
    /// Adds an engine.
    /// </summary>
    /// <param name="name">Engine name.</param>
    /// <param name="scriptBlock">Engine body.</param>
    public static void Engine(string name, ScriptBlock scriptBlock)
    {
        var context = RequireContext();
        var suite = RequireSuite();
        var engine = new PowerShellBenchmarkEngine { Name = RequireName(name, "engine name") };
        suite.Engines.Add(engine);
        context.EngineStack.Push(engine);
        try
        {
            InvokeDslBlock(scriptBlock);
        }
        finally
        {
            context.EngineStack.Pop();
        }
    }

    /// <summary>
    /// Adds an operation handler to the current engine.
    /// </summary>
    /// <param name="name">Operation name.</param>
    /// <param name="scriptBlock">Operation block.</param>
    public static void Operation(string name, ScriptBlock scriptBlock)
    {
        var context = RequireContext();
        if (context.EngineStack.Count == 0)
            throw new InvalidOperationException("operation can only be used inside engine.");
        context.EngineStack.Peek().Operations[RequireName(name, "operation name")] = scriptBlock;
    }

    /// <summary>
    /// Adds a custom metric.
    /// </summary>
    /// <param name="name">Metric name.</param>
    /// <param name="scriptBlock">Metric block.</param>
    public static void Metric(string name, ScriptBlock scriptBlock)
        => RequireSuite().Metrics.Add(new PowerShellBenchmarkMetric { Name = RequireName(name, "metric name"), ScriptBlock = scriptBlock });

    /// <summary>
    /// Adds a comparison definition.
    /// </summary>
    /// <param name="dimension">Dimension name.</param>
    /// <param name="baseline">Baseline value.</param>
    /// <param name="metric">Metric names.</param>
    public static void Compare(string dimension, string baseline, string[]? metric)
        => RequireSuite().Comparisons.Add(new PowerShellBenchmarkComparison
        {
            Dimension = string.IsNullOrWhiteSpace(dimension) ? "Engine" : dimension.Trim(),
            Baseline = baseline ?? string.Empty,
            Metrics = metric is { Length: > 0 } ? metric : new[] { "MedianMs" }
        });

    /// <summary>
    /// Adds a README or Markdown block target.
    /// </summary>
    /// <param name="path">Document path.</param>
    /// <param name="block">Block id.</param>
    /// <param name="renderer">Renderer name.</param>
    public static void Readme(string path, string block, string? renderer)
        => RequireSuite().ReadmeBlocks.Add(new PowerShellBenchmarkReadmeBlock
        {
            Path = path ?? string.Empty,
            BlockId = block ?? string.Empty,
            Renderer = string.IsNullOrWhiteSpace(renderer) ? "SummaryTable" : renderer!.Trim()
        });

    /// <summary>
    /// Sets requested artifacts.
    /// </summary>
    /// <param name="values">Artifact names.</param>
    public static void Artifacts(object?[] values)
    {
        var kinds = BenchmarkArtifactKind.None;
        foreach (var value in Flatten(values))
        {
            if (Enum.TryParse<BenchmarkArtifactKind>(Convert.ToString(value, CultureInfo.InvariantCulture), ignoreCase: true, out var kind))
                kinds |= kind;
        }

        RequireSuite().Artifacts = kinds;
    }

    private static IEnumerable<PSObject> InvokeStrict(ScriptBlock scriptBlock)
    {
        var variables = new List<PSVariable>
        {
            new("ErrorActionPreference", ActionPreference.Stop),
            new("PSNativeCommandUseErrorActionPreference", true)
        };
        return scriptBlock.InvokeWithContext(functionsToDefine: null, variablesToDefine: variables, args: Array.Empty<object>());
    }

    private static Hashtable CreateFunctions()
        => new()
        {
            ["benchmark"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Alias('out')] [string] $OutputRoot, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Benchmark($Name, $OutputRoot, $ScriptBlock)"),
            ["cases"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Cases($ScriptBlock)"),
            ["case"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [hashtable] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Case($Name, $Values)"),
            ["from"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::From($ScriptBlock)"),
            ["axis"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Axis($Name, $Values)"),
            ["setup"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Setup($ScriptBlock)"),
            ["data"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Data($ScriptBlock)"),
            ["skip"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Skip($ScriptBlock)"),
            ["validate"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Validate($ScriptBlock)"),
            ["engine"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Engine($Name, $ScriptBlock)"),
            ["operation"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Operation($Name, $ScriptBlock)"),
            ["metric"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Metric($Name, $ScriptBlock)"),
            ["compare"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Dimension, [string] $Baseline, [string[]] $Metric) [PowerForge.PowerShellBenchmarkDslRuntime]::Compare($Dimension, $Baseline, $Metric)"),
            ["readme"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Path, [string] $Block, [string] $Renderer) [PowerForge.PowerShellBenchmarkDslRuntime]::Readme($Path, $Block, $Renderer)"),
            ["artifacts"] = ScriptBlock.Create("param([Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Artifacts($Values)"),
            ["New-BenchmarkSuite"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Alias('out')] [string] $OutputRoot, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Benchmark($Name, $OutputRoot, $ScriptBlock)"),
            ["Add-BenchmarkCases"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Cases($ScriptBlock)"),
            ["Add-BenchmarkCase"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [hashtable] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Case($Name, $Values)"),
            ["Add-BenchmarkCaseSource"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::From($ScriptBlock)"),
            ["Add-BenchmarkAxis"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Axis($Name, $Values)"),
            ["Set-BenchmarkSetup"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Setup($ScriptBlock)"),
            ["Set-BenchmarkDataFactory"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Data($ScriptBlock)"),
            ["Add-BenchmarkEngine"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Engine($Name, $ScriptBlock)"),
            ["Add-BenchmarkOperation"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Operation($Name, $ScriptBlock)"),
            ["Add-BenchmarkSkipRule"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Skip($ScriptBlock)"),
            ["Add-BenchmarkValidation"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Validate($ScriptBlock)"),
            ["Add-BenchmarkMetric"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Metric($Name, $ScriptBlock)"),
            ["Add-BenchmarkComparison"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Dimension, [string] $Baseline, [string[]] $Metric) [PowerForge.PowerShellBenchmarkDslRuntime]::Compare($Dimension, $Baseline, $Metric)"),
            ["Add-BenchmarkReadmeBlock"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Path, [string] $Block, [string] $Renderer) [PowerForge.PowerShellBenchmarkDslRuntime]::Readme($Path, $Block, $Renderer)"),
            ["Set-BenchmarkArtifacts"] = ScriptBlock.Create("param([Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Artifacts($Values)")
        };

    private static PowerShellBenchmarkCase ConvertToCase(PSObject value)
    {
        var item = new PowerShellBenchmarkCase();
        var baseObject = value.BaseObject;
        if (baseObject is Hashtable table)
        {
            foreach (DictionaryEntry entry in table)
                item.Values[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = entry.Value;
        }
        else
        {
            foreach (var property in value.Properties.Where(p => p.IsGettable))
                item.Values[property.Name] = property.Value;
        }

        item.Name = item.Values.TryGetValue("Name", out var name) || item.Values.TryGetValue("Scenario", out name)
            ? Convert.ToString(name, CultureInfo.InvariantCulture) ?? "Case"
            : "Case";
        return item;
    }

    private static IEnumerable<object?> Flatten(IEnumerable<object?> values)
    {
        foreach (var value in values ?? Array.Empty<object?>())
        {
            if (value is string || value is null)
            {
                yield return value;
                continue;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    yield return item;
            }
            else
            {
                yield return value;
            }
        }
    }

    private static PowerShellBenchmarkDslContext RequireContext()
        => Current.Value ?? throw new InvalidOperationException("Benchmark DSL helpers can only be used while evaluating a benchmark spec.");

    private static void InvokeDslBlock(ScriptBlock scriptBlock)
        => scriptBlock.InvokeWithContext(CreateFunctions(), new List<PSVariable>(), Array.Empty<object>());

    private static AliasSnapshot? RemoveAlias(string name)
    {
        using var getter = PowerShell.Create(RunspaceMode.CurrentRunspace);
        var alias = getter.AddCommand("Get-Alias")
            .AddArgument(name)
            .AddParameter("ErrorAction", "SilentlyContinue")
            .Invoke<AliasInfo>()
            .FirstOrDefault();
        if (alias is null) return null;

        using var remover = PowerShell.Create(RunspaceMode.CurrentRunspace);
        remover.AddCommand("Remove-Item")
            .AddArgument("Alias:" + name)
            .AddParameter("Force")
            .AddParameter("ErrorAction", "SilentlyContinue")
            .Invoke();
        return new AliasSnapshot(alias.Name, alias.Definition, alias.Options);
    }

    private static void RestoreAlias(AliasSnapshot? snapshot)
    {
        if (snapshot is null) return;
        using var setter = PowerShell.Create(RunspaceMode.CurrentRunspace);
        setter.AddCommand("Set-Alias")
            .AddParameter("Name", snapshot.Name)
            .AddParameter("Value", snapshot.Definition)
            .AddParameter("Option", snapshot.Options)
            .AddParameter("Force")
            .AddParameter("ErrorAction", "SilentlyContinue")
            .Invoke();
    }

    private static PowerShellBenchmarkSuite RequireSuite()
    {
        var context = RequireContext();
        if (context.SuiteStack.Count == 0)
            throw new InvalidOperationException("This benchmark DSL command can only be used inside benchmark.");
        return context.SuiteStack.Peek();
    }

    private static string RequireName(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{label} is required.");
        return value!.Trim();
    }

    private sealed class PowerShellBenchmarkDslContext
    {
        internal List<PowerShellBenchmarkSuite> Suites { get; } = new();
        internal Stack<PowerShellBenchmarkSuite> SuiteStack { get; } = new();
        internal Stack<PowerShellBenchmarkEngine> EngineStack { get; } = new();
    }

    private sealed class AliasSnapshot
    {
        internal AliasSnapshot(string name, string definition, ScopedItemOptions options)
        {
            Name = name;
            Definition = definition;
            Options = options;
        }

        internal string Name { get; }
        internal string Definition { get; }
        internal ScopedItemOptions Options { get; }
    }
}
