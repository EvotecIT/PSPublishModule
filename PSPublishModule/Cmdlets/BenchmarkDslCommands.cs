using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Base type for benchmark declaration commands.
/// </summary>
public abstract class BenchmarkDslCommand : PSCmdlet
{
    /// <summary>
    /// Converts an enum value to the runtime string representation.
    /// </summary>
    /// <typeparam name="T">Enum type.</typeparam>
    /// <param name="value">Enum value.</param>
    /// <returns>Enum name.</returns>
    protected static string NameOf<T>(T value) where T : struct, Enum
        => value.ToString();

    /// <summary>
    /// Captures the caller scope before forwarding a script block to the reusable benchmark runtime.
    /// </summary>
    /// <param name="scriptBlock">User supplied script block.</param>
    /// <returns>Closed script block.</returns>
    protected static ScriptBlock CloseOverCallerScope(ScriptBlock scriptBlock)
        => scriptBlock.GetNewClosure();

    /// <summary>
    /// Captures caller-defined helper functions visible to the benchmark spec.
    /// </summary>
    /// <returns>Function definitions keyed by name.</returns>
    protected Dictionary<string, string> CaptureCallerFunctions()
    {
        var functions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in InvokeCommand.GetCommands("*", CommandTypes.Function, nameIsPattern: true))
        {
            if (command is not FunctionInfo function) continue;
            if (BenchmarkDslCommandNames.Contains(function.Name)) continue;
            if (function.Name.IndexOf(':') >= 0) continue;
            if (!string.IsNullOrWhiteSpace(function.Source)) continue;
            if (!string.IsNullOrWhiteSpace(function.ModuleName)) continue;
            if ((function.Options & ScopedItemOptions.Constant) != 0 || (function.Options & ScopedItemOptions.ReadOnly) != 0) continue;
            if (string.IsNullOrWhiteSpace(function.Definition)) continue;
            if (!functions.ContainsKey(function.Name))
                functions[function.Name] = function.Definition;
        }

        return functions;
    }

    private static readonly HashSet<string> BenchmarkDslCommandNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "benchmark",
        "cases",
        "case",
        "caseSource",
        "from",
        "axis",
        "setup",
        "data",
        "skip",
        "validate",
        "policy",
        "profile",
        "cleanup",
        "engine",
        "operation",
        "metric",
        "compare",
        "comparison",
        "readme",
        "artifacts",
        "input",
        "inputInt",
        "inputBool",
        "New-BenchmarkSuite",
        "Add-BenchmarkCases",
        "Add-BenchmarkCase",
        "Add-BenchmarkCaseSource",
        "Add-BenchmarkAxis",
        "Set-BenchmarkSetup",
        "Set-BenchmarkDataFactory",
        "Set-BenchmarkPolicy",
        "Set-BenchmarkProfile",
        "Set-BenchmarkCleanup",
        "Add-BenchmarkEngine",
        "Add-BenchmarkOperation",
        "Add-BenchmarkSkipRule",
        "Add-BenchmarkValidation",
        "Add-BenchmarkMetric",
        "Add-BenchmarkComparison",
        "Add-BenchmarkReadmeBlock",
        "Set-BenchmarkArtifacts",
        "Get-BenchmarkInput"
    };
}

/// <summary>
/// Declares a PowerShell benchmark suite.
/// </summary>
[Cmdlet(VerbsCommon.New, "BenchmarkSuite")]
[Alias("benchmark")]
public sealed class NewBenchmarkSuiteCommand : BenchmarkDslCommand
{
    /// <summary>Suite name.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Output root for benchmark artifacts.</summary>
    [Parameter]
    [Alias("out")]
    public string? OutputRoot { get; set; }

    /// <summary>Suite declaration body.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create(string.Empty);

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        PowerShellBenchmarkDslRuntime.RegisterCallerFunctions(CaptureCallerFunctions());
        PowerShellBenchmarkDslRuntime.Benchmark(Name, OutputRoot, CloseOverCallerScope(ScriptBlock));
    }
}

/// <summary>
/// Groups benchmark case declarations.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkCases")]
[Alias("cases")]
public sealed class AddBenchmarkCasesCommand : BenchmarkDslCommand
{
    /// <summary>Case declaration body.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create(string.Empty);

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Cases(CloseOverCallerScope(ScriptBlock));
}

/// <summary>
/// Adds one benchmark case.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkCase")]
[Alias("case")]
public sealed class AddBenchmarkCaseCommand : BenchmarkDslCommand
{
    /// <summary>Case name.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Case values.</summary>
    [Parameter(Position = 1)]
    public Hashtable? Values { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Case(Name, Values);
}

/// <summary>
/// Adds benchmark cases from a script block or evaluated objects.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkCaseSource", DefaultParameterSetName = ParameterSetInputObject)]
[Alias("caseSource", "from")]
public sealed class AddBenchmarkCaseSourceCommand : BenchmarkDslCommand
{
    private const string ParameterSetScriptBlock = "ScriptBlock";
    private const string ParameterSetInputObject = "InputObject";

    /// <summary>Script block that emits case objects.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetScriptBlock)]
    public ScriptBlock? ScriptBlock { get; set; }

    /// <summary>Already evaluated case objects.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromRemainingArguments = true, ParameterSetName = ParameterSetInputObject)]
    public object?[]? InputObject { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        if (ParameterSetName == ParameterSetScriptBlock)
        {
            PowerShellBenchmarkDslRuntime.From(CloseOverCallerScope(ScriptBlock ?? ScriptBlock.Create(string.Empty)));
            return;
        }

        PowerShellBenchmarkDslRuntime.FromValues(InputObject ?? Array.Empty<object?>());
    }
}

/// <summary>
/// Adds a benchmark matrix axis.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkAxis")]
[Alias("axis")]
public sealed class AddBenchmarkAxisCommand : BenchmarkDslCommand
{
    /// <summary>Axis name.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Axis values.</summary>
    [Parameter(Mandatory = true, Position = 1, ValueFromRemainingArguments = true)]
    public object?[] Values { get; set; } = Array.Empty<object?>();

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Axis(Name, Values);
}

/// <summary>
/// Sets the suite setup block.
/// </summary>
[Cmdlet(VerbsCommon.Set, "BenchmarkSetup")]
[Alias("setup")]
public sealed class SetBenchmarkSetupCommand : BenchmarkDslCommand
{
    /// <summary>Setup block.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create(string.Empty);

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Setup(CloseOverCallerScope(ScriptBlock));
}

/// <summary>
/// Sets the suite data factory block.
/// </summary>
[Cmdlet(VerbsCommon.Set, "BenchmarkDataFactory")]
[Alias("data")]
public sealed class SetBenchmarkDataFactoryCommand : BenchmarkDslCommand
{
    /// <summary>Data factory block.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create(string.Empty);

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Data(CloseOverCallerScope(ScriptBlock));
}

/// <summary>
/// Sets benchmark run policy defaults.
/// </summary>
[Cmdlet(VerbsCommon.Set, "BenchmarkPolicy")]
[Alias("policy")]
public sealed class SetBenchmarkPolicyCommand : BenchmarkDslCommand
{
    /// <summary>Warmup iteration count.</summary>
    [Parameter]
    [ValidateRange(0, int.MaxValue)]
    public int? Warmup { get; set; }

    /// <summary>Measured iteration count.</summary>
    [Parameter]
    [Alias("Iterations")]
    [ValidateRange(1, int.MaxValue)]
    public int? Iteration { get; set; }

    /// <summary>Run mode label.</summary>
    [Parameter]
    public string? RunMode { get; set; }

    /// <summary>Work-item ordering strategy.</summary>
    [Parameter]
    public PowerShellBenchmarkRunOrder? Order { get; set; }

    /// <summary>Delay between measured samples, in milliseconds.</summary>
    [Parameter]
    [ValidateRange(0, int.MaxValue)]
    public int? CooldownMilliseconds { get; set; }

    /// <summary>Summary outlier policy.</summary>
    [Parameter]
    public PowerShellBenchmarkOutlierMode? OutlierMode { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Policy(
            Warmup,
            Iteration,
            RunMode,
            Order?.ToString(),
            CooldownMilliseconds,
            OutlierMode?.ToString());
}

/// <summary>
/// Sets the benchmark profile mode.
/// </summary>
[Cmdlet(VerbsCommon.Set, "BenchmarkProfile")]
[Alias("profile")]
public sealed class SetBenchmarkProfileCommand : BenchmarkDslCommand
{
    /// <summary>Profile kind.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public PowerShellBenchmarkProfileKind Name { get; set; } = PowerShellBenchmarkProfileKind.Current;

    /// <summary>Cleanup mode used by profile-owned temporary state.</summary>
    [Parameter]
    public PowerShellBenchmarkCleanupMode? Cleanup { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Profile(NameOf(Name), Cleanup.HasValue ? NameOf(Cleanup.Value) : null);
}

/// <summary>
/// Sets the benchmark cleanup mode.
/// </summary>
[Cmdlet(VerbsCommon.Set, "BenchmarkCleanup")]
[Alias("cleanup")]
public sealed class SetBenchmarkCleanupCommand : BenchmarkDslCommand
{
    /// <summary>Cleanup mode.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public PowerShellBenchmarkCleanupMode Name { get; set; } = PowerShellBenchmarkCleanupMode.Always;

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Cleanup(NameOf(Name));
}

/// <summary>
/// Adds a benchmark engine.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkEngine")]
[Alias("engine")]
public sealed class AddBenchmarkEngineCommand : BenchmarkDslCommand
{
    /// <summary>Engine name.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Engine declaration body.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create(string.Empty);

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Engine(Name, CloseOverCallerScope(ScriptBlock));
}

/// <summary>
/// Adds an operation handler to the current benchmark engine.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkOperation")]
[Alias("operation")]
public sealed class AddBenchmarkOperationCommand : BenchmarkDslCommand
{
    /// <summary>Operation name.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Operation body.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create(string.Empty);

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Operation(Name, CloseOverCallerScope(ScriptBlock));
}

/// <summary>
/// Adds a benchmark skip rule.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkSkipRule")]
[Alias("skip")]
public sealed class AddBenchmarkSkipRuleCommand : BenchmarkDslCommand
{
    /// <summary>Skip rule block.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create(string.Empty);

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Skip(CloseOverCallerScope(ScriptBlock));
}

/// <summary>
/// Adds a benchmark validation block.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkValidation")]
[Alias("validate")]
public sealed class AddBenchmarkValidationCommand : BenchmarkDslCommand
{
    /// <summary>Validation block.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create(string.Empty);

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Validate(CloseOverCallerScope(ScriptBlock));
}

/// <summary>
/// Adds a custom benchmark metric.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkMetric")]
[Alias("metric")]
public sealed class AddBenchmarkMetricCommand : BenchmarkDslCommand
{
    /// <summary>Metric name.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Metric block.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create(string.Empty);

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Metric(Name, CloseOverCallerScope(ScriptBlock));
}

/// <summary>
/// Adds a benchmark comparison definition.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkComparison")]
[Alias("comparison")]
public sealed class AddBenchmarkComparisonCommand : BenchmarkDslCommand
{
    /// <summary>Dimension to compare.</summary>
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Dimension { get; set; } = "Engine";

    /// <summary>Baseline value.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Baseline { get; set; } = string.Empty;

    /// <summary>Metric names.</summary>
    [Parameter]
    public string[]? Metric { get; set; }

    /// <summary>Fractional tolerance used to label practically equivalent results, such as <c>0.05</c> for five percent.</summary>
    [Parameter]
    [ValidateRange(0d, double.MaxValue)]
    public double TieTolerance { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Compare(Dimension, Baseline, Metric, TieTolerance);
}

/// <summary>
/// Adds a README or Markdown benchmark block target.
/// </summary>
[Cmdlet(VerbsCommon.Add, "BenchmarkReadmeBlock")]
[Alias("readme")]
public sealed class AddBenchmarkReadmeBlockCommand : BenchmarkDslCommand
{
    /// <summary>Document path.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Marker block id.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Block { get; set; } = string.Empty;

    /// <summary>Renderer name.</summary>
    [Parameter]
    public string? Renderer { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Readme(Path, Block, Renderer);
}

/// <summary>
/// Sets requested benchmark artifacts.
/// </summary>
[Cmdlet(VerbsCommon.Set, "BenchmarkArtifacts")]
[Alias("artifacts")]
public sealed class SetBenchmarkArtifactsCommand : BenchmarkDslCommand
{
    /// <summary>Artifact kinds.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromRemainingArguments = true)]
    public object?[] Kind { get; set; } = Array.Empty<object?>();

    /// <inheritdoc />
    protected override void ProcessRecord()
        => PowerShellBenchmarkDslRuntime.Artifacts(Kind);
}
