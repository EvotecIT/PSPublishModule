namespace PowerForge;

/// <summary>
/// Execution status for a benchmark sample.
/// </summary>
public enum BenchmarkSampleStatus
{
    /// <summary>The measured operation completed successfully.</summary>
    Succeeded,

    /// <summary>The measured operation failed and the failure was recorded as data.</summary>
    Failed,

    /// <summary>The expanded benchmark case was skipped before measurement.</summary>
    Skipped
}

/// <summary>
/// Common artifact selector for reusable benchmark runs.
/// </summary>
[Flags]
public enum BenchmarkArtifactKind
{
    /// <summary>No artifacts are requested.</summary>
    None = 0,

    /// <summary>Write JSON artifacts.</summary>
    Json = 1,

    /// <summary>Write CSV artifacts.</summary>
    Csv = 2,

    /// <summary>Write Markdown artifacts.</summary>
    Markdown = 4,

    /// <summary>Write the default artifact set.</summary>
    Default = Json | Csv | Markdown
}

/// <summary>
/// Baseline behavior for generic benchmark gates.
/// </summary>
public enum BenchmarkBaselineMode
{
    /// <summary>Verify current benchmark values against an existing baseline.</summary>
    Verify,

    /// <summary>Update the baseline file from current benchmark values.</summary>
    Update
}

/// <summary>
/// One raw measurement emitted by a benchmark run.
/// </summary>
public sealed class BenchmarkSample
{
    /// <summary>Stable run identifier shared by all samples from the same invocation.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Benchmark suite name.</summary>
    public string Suite { get; set; } = string.Empty;

    /// <summary>Scenario or case name.</summary>
    public string Scenario { get; set; } = string.Empty;

    /// <summary>Measured operation name.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Engine, implementation, or competitor lane name.</summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>PowerShell host or runtime label.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Operating-system label captured for report grouping.</summary>
    public string Os { get; set; } = string.Empty;

    /// <summary>Run mode such as quick, standard, or publish.</summary>
    public string RunMode { get; set; } = string.Empty;

    /// <summary>Zero-based iteration number after warmup has completed.</summary>
    public int Iteration { get; set; }

    /// <summary>Sample status.</summary>
    public BenchmarkSampleStatus Status { get; set; }

    /// <summary>Measured operation duration in milliseconds.</summary>
    public double DurationMs { get; set; }

    /// <summary>Optional allocated bytes value when a runner can collect it.</summary>
    public long? AllocatedBytes { get; set; }

    /// <summary>Optional working-set delta value when a runner can collect it.</summary>
    public long? WorkingSetDeltaBytes { get; set; }

    /// <summary>Optional primary numeric output metric.</summary>
    public double? OutputMetric { get; set; }

    /// <summary>Failure or skip reason.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Case and matrix variables flattened for portable reporting.</summary>
    public Dictionary<string, string?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Custom numeric metrics captured outside the timed block.</summary>
    public Dictionary<string, double> Metrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Aggregated benchmark row for one scenario, operation, engine, and host.
/// </summary>
public sealed class BenchmarkSummaryRow
{
    /// <summary>Benchmark suite name.</summary>
    public string Suite { get; set; } = string.Empty;

    /// <summary>Scenario or case name.</summary>
    public string Scenario { get; set; } = string.Empty;

    /// <summary>Measured operation name.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Engine, implementation, or competitor lane name.</summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>PowerShell host or runtime label.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Operating-system label captured for report grouping.</summary>
    public string Os { get; set; } = string.Empty;

    /// <summary>Case and matrix variables represented by this summary row.</summary>
    public Dictionary<string, string?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of successful measured samples in this group.</summary>
    public int SampleCount { get; set; }

    /// <summary>Number of failed measured samples in this group.</summary>
    public int FailureCount { get; set; }

    /// <summary>Group status derived from successful and failed samples.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Median duration in milliseconds.</summary>
    public double? MedianMs { get; set; }

    /// <summary>Mean duration in milliseconds.</summary>
    public double? MeanMs { get; set; }

    /// <summary>Minimum duration in milliseconds.</summary>
    public double? MinMs { get; set; }

    /// <summary>Maximum duration in milliseconds.</summary>
    public double? MaxMs { get; set; }

    /// <summary>Custom numeric metrics aggregated as averages by metric name.</summary>
    public Dictionary<string, double> Metrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Relative comparison row against a baseline engine.
/// </summary>
public sealed class BenchmarkComparisonRow
{
    /// <summary>Benchmark suite name.</summary>
    public string Suite { get; set; } = string.Empty;

    /// <summary>Scenario or case name.</summary>
    public string Scenario { get; set; } = string.Empty;

    /// <summary>Measured operation name.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>PowerShell host or runtime label.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Operating-system label represented by this comparison row.</summary>
    public string Os { get; set; } = string.Empty;

    /// <summary>Case and matrix variables represented by this comparison row.</summary>
    public Dictionary<string, string?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Compared engine name.</summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>Baseline engine name.</summary>
    public string BaselineEngine { get; set; } = string.Empty;

    /// <summary>Actual metric value for the compared engine.</summary>
    public double? Actual { get; set; }

    /// <summary>Baseline metric value.</summary>
    public double? Baseline { get; set; }

    /// <summary>Ratio of actual value to baseline value.</summary>
    public double? Ratio { get; set; }

    /// <summary>Metric name used for comparison.</summary>
    public string Metric { get; set; } = "MedianMs";
}

/// <summary>
/// Complete benchmark result payload written by the reusable runner.
/// </summary>
public sealed class BenchmarkRunResult
{
    /// <summary>Stable run identifier.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Benchmark suite name.</summary>
    public string Suite { get; set; } = string.Empty;

    /// <summary>Run start timestamp in UTC.</summary>
    public DateTimeOffset StartedUtc { get; set; }

    /// <summary>Run finish timestamp in UTC.</summary>
    public DateTimeOffset FinishedUtc { get; set; }

    /// <summary>Raw samples.</summary>
    public BenchmarkSample[] Samples { get; set; } = Array.Empty<BenchmarkSample>();

    /// <summary>Aggregated summary rows.</summary>
    public BenchmarkSummaryRow[] Summary { get; set; } = Array.Empty<BenchmarkSummaryRow>();

    /// <summary>Optional comparison rows.</summary>
    public BenchmarkComparisonRow[] Comparison { get; set; } = Array.Empty<BenchmarkComparisonRow>();

    /// <summary>Paths to artifacts written during the run.</summary>
    public Dictionary<string, string> Artifacts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Environment and runner metadata.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Result of a Markdown benchmark block update.
/// </summary>
public sealed class BenchmarkDocumentUpdateResult
{
    /// <summary>Updated document path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Updated block identifier.</summary>
    public string BlockId { get; set; } = string.Empty;

    /// <summary>True when the file content changed.</summary>
    public bool Changed { get; set; }
}

/// <summary>
/// Request for a generic benchmark gate evaluation.
/// </summary>
public sealed class BenchmarkGateRequest
{
    /// <summary>Summary JSON path.</summary>
    public string SummaryPath { get; set; } = string.Empty;

    /// <summary>Baseline JSON path.</summary>
    public string BaselinePath { get; set; } = string.Empty;

    /// <summary>Metric to evaluate.</summary>
    public string Metric { get; set; } = "MedianMs";

    /// <summary>Fields used to construct stable metric keys.</summary>
    public string[] GroupBy { get; set; } = { "Suite", "Scenario", "Operation", "Engine", "Host", "OS", "Variables" };

    /// <summary>Baseline behavior mode.</summary>
    public BenchmarkBaselineMode BaselineMode { get; set; } = BenchmarkBaselineMode.Verify;

    /// <summary>When true, metrics missing from the baseline fail verification.</summary>
    public bool FailOnNew { get; set; } = true;

    /// <summary>Relative tolerance used to compute the allowed cap.</summary>
    public double RelativeTolerance { get; set; } = 0.10;

    /// <summary>Absolute tolerance in milliseconds used to compute the allowed cap.</summary>
    public double AbsoluteToleranceMs { get; set; }
}

/// <summary>
/// Result of a generic benchmark gate evaluation.
/// </summary>
public sealed class BenchmarkGateResult
{
    /// <summary>True when verification passed or update completed without missing data.</summary>
    public bool Passed { get; set; }

    /// <summary>True when the baseline file was updated.</summary>
    public bool BaselineUpdated { get; set; }

    /// <summary>Baseline path used for this gate.</summary>
    public string BaselinePath { get; set; } = string.Empty;

    /// <summary>Metric-level results.</summary>
    public BenchmarkGateMetricResult[] Metrics { get; set; } = Array.Empty<BenchmarkGateMetricResult>();

    /// <summary>Diagnostic messages.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Result for one benchmark gate metric key.
/// </summary>
public sealed class BenchmarkGateMetricResult
{
    /// <summary>Stable metric key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Actual metric value.</summary>
    public double? Actual { get; set; }

    /// <summary>Baseline metric value.</summary>
    public double? Baseline { get; set; }

    /// <summary>Computed allowed cap.</summary>
    public double? Allowed { get; set; }

    /// <summary>True when the metric is new compared to the baseline.</summary>
    public bool MissingInBaseline { get; set; }

    /// <summary>True when a baseline metric was not produced by the current run.</summary>
    public bool MissingInCurrent { get; set; }

    /// <summary>True when the actual value exceeded the allowed cap.</summary>
    public bool Regressed { get; set; }
}
