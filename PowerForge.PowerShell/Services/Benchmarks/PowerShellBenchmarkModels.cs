using System.Management.Automation;

namespace PowerForge;

/// <summary>
/// PowerShell benchmark profile isolation mode.
/// </summary>
public enum PowerShellBenchmarkProfileKind
{
    /// <summary>Run in the current user and host context.</summary>
    Current,

    /// <summary>Run from a temporary local Windows account with a loaded user profile.</summary>
    TemporaryLocalUser
}

/// <summary>
/// Cleanup behavior for benchmark-owned temporary environment state.
/// </summary>
public enum PowerShellBenchmarkCleanupMode
{
    /// <summary>Always remove benchmark-owned temporary environment state.</summary>
    Always,

    /// <summary>Keep benchmark-owned temporary environment state when a run fails.</summary>
    KeepOnFailure,

    /// <summary>Always keep benchmark-owned temporary environment state for inspection.</summary>
    KeepAlways
}

/// <summary>
/// Ordering strategy for measured benchmark work items.
/// </summary>
public enum PowerShellBenchmarkRunOrder
{
    /// <summary>Run work items in their resolved plan order every iteration.</summary>
    Sequential,

    /// <summary>Rotate work item order by iteration to reduce first-lane bias.</summary>
    Rotated,

    /// <summary>Shuffle work item order deterministically for each iteration.</summary>
    Randomized
}

/// <summary>
/// PowerShell-authored benchmark suite definition.
/// </summary>
public sealed class PowerShellBenchmarkSuite
{
    /// <summary>Suite name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Output root for benchmark artifacts.</summary>
    public string OutputRoot { get; set; } = Path.Combine("Build", "Benchmarks");

    /// <summary>Warmup iteration count.</summary>
    public int WarmupCount { get; set; } = 1;

    /// <summary>Measured iteration count.</summary>
    public int IterationCount { get; set; } = 3;

    /// <summary>Run mode label.</summary>
    public string RunMode { get; set; } = "standard";

    /// <summary>Measured work-item ordering strategy.</summary>
    public PowerShellBenchmarkRunOrder RunOrder { get; set; } = PowerShellBenchmarkRunOrder.Rotated;

    /// <summary>Delay between measured samples, in milliseconds.</summary>
    public int CooldownMilliseconds { get; set; }

    /// <summary>Outlier policy used by the summary service.</summary>
    public PowerShellBenchmarkOutlierMode OutlierMode { get; set; } = PowerShellBenchmarkOutlierMode.None;

    /// <summary>PowerShell profile isolation mode.</summary>
    public PowerShellBenchmarkProfileKind Profile { get; set; } = PowerShellBenchmarkProfileKind.Current;

    /// <summary>Profile value exposed to planning, skip rules, and case metadata when execution must use a different host profile.</summary>
    public PowerShellBenchmarkProfileKind? PlanningProfile { get; set; }

    /// <summary>Cleanup behavior for benchmark-owned temporary environment state.</summary>
    public PowerShellBenchmarkCleanupMode Cleanup { get; set; } = PowerShellBenchmarkCleanupMode.Always;

    /// <summary>Declared benchmark cases.</summary>
    public List<PowerShellBenchmarkCase> Cases { get; } = new();

    /// <summary>Declared benchmark axes.</summary>
    public List<PowerShellBenchmarkAxis> Axes { get; } = new();

    /// <summary>Declared benchmark engines.</summary>
    public List<PowerShellBenchmarkEngine> Engines { get; } = new();

    /// <summary>Custom metrics collected after measured operations.</summary>
    public List<PowerShellBenchmarkMetric> Metrics { get; } = new();

    /// <summary>README/document update definitions.</summary>
    public List<PowerShellBenchmarkReadmeBlock> ReadmeBlocks { get; } = new();

    /// <summary>Comparison definitions.</summary>
    public List<PowerShellBenchmarkComparison> Comparisons { get; } = new();

    /// <summary>Setup block called outside measured operation time.</summary>
    public ScriptBlock? Setup { get; set; }

    /// <summary>Data factory block called outside measured operation time.</summary>
    public ScriptBlock? Data { get; set; }

    /// <summary>Skip rule called before a case is measured.</summary>
    public ScriptBlock? Skip { get; set; }

    /// <summary>Validation block called outside measured operation time.</summary>
    public ScriptBlock? Validate { get; set; }

    /// <summary>Requested artifact kinds.</summary>
    public BenchmarkArtifactKind Artifacts { get; set; } = BenchmarkArtifactKind.Default;
}

/// <summary>
/// One benchmark case definition.
/// </summary>
public sealed class PowerShellBenchmarkCase
{
    /// <summary>Case name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Case values.</summary>
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// One benchmark matrix axis.
/// </summary>
public sealed class PowerShellBenchmarkAxis
{
    /// <summary>Axis name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Axis values.</summary>
    public List<object?> Values { get; } = new();
}

/// <summary>
/// Benchmark engine containing named operation handlers.
/// </summary>
public sealed class PowerShellBenchmarkEngine
{
    /// <summary>Engine name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Operation handlers keyed by operation name.</summary>
    public Dictionary<string, ScriptBlock> Operations { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Custom metric script block.
/// </summary>
public sealed class PowerShellBenchmarkMetric
{
    /// <summary>Metric name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Metric script block.</summary>
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create(string.Empty);
}

/// <summary>
/// README or Markdown block update definition.
/// </summary>
public sealed class PowerShellBenchmarkReadmeBlock
{
    /// <summary>Document path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Marker block identifier.</summary>
    public string BlockId { get; set; } = string.Empty;

    /// <summary>Renderer name.</summary>
    public string Renderer { get; set; } = "SummaryTable";
}

/// <summary>
/// Comparison definition for a benchmark suite.
/// </summary>
public sealed class PowerShellBenchmarkComparison
{
    /// <summary>Dimension used for comparison.</summary>
    public string Dimension { get; set; } = "Engine";

    /// <summary>Baseline value for the comparison dimension.</summary>
    public string Baseline { get; set; } = string.Empty;

    /// <summary>Metric names to compare.</summary>
    public string[] Metrics { get; set; } = { "MedianMs" };

    /// <summary>Fractional tolerance used to label practically equivalent results, such as <c>0.05</c> for five percent.</summary>
    public double TieTolerance { get; set; }
}

/// <summary>
/// Resolved benchmark work item.
/// </summary>
public sealed class PowerShellBenchmarkWorkItem
{
    /// <summary>Case values after axis expansion.</summary>
    public Dictionary<string, object?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scenario name.</summary>
    public string Scenario { get; set; } = string.Empty;

    /// <summary>Engine name.</summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>Operation name.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Host label.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Selected handler.</summary>
    public ScriptBlock Handler { get; set; } = ScriptBlock.Create(string.Empty);

    /// <summary>Whether the work item was filtered by a skip rule during planning.</summary>
    public bool IsSkipped { get; set; }
}
