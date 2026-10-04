namespace PowerForge;

/// <summary>Options for a duration gate calibrated from accepted runs on the same runner environment.</summary>
public sealed class BenchmarkHistoryRequest
{
    /// <summary>Stable workload version or fixture hash. Change it when measured work changes.</summary>
    public string WorkloadId { get; set; } = string.Empty;
    /// <summary>Stable runner pool or dedicated machine identity, independent of an ephemeral host name.</summary>
    public string RunnerIdentity { get; set; } = string.Empty;
    /// <summary>Minimum independent accepted runs required before a threshold can be used.</summary>
    public int MinimumRuns { get; set; } = 5;
    /// <summary>Minimum successful measured samples in every lane of an accepted or current run.</summary>
    public int MinimumSamples { get; set; } = 5;
    /// <summary>Maximum recent accepted runs used to calibrate each lane.</summary>
    public int WindowSize { get; set; } = 20;
    /// <summary>Minimum relative allowance above the historical median, in addition to measured noise.</summary>
    public double RelativeTolerance { get; set; } = 0.10;
    /// <summary>Minimum absolute duration allowance in milliseconds.</summary>
    public double AbsoluteToleranceMs { get; set; }
}

/// <summary>Bounded accepted-run history. Raw samples remain in the original runner artifacts.</summary>
public sealed class BenchmarkHistory
{
    /// <summary>History schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Accepted independent runs, each retaining its exact normalized result hash.</summary>
    public BenchmarkHistoryEntry[] Entries { get; set; } = Array.Empty<BenchmarkHistoryEntry>();
}

/// <summary>One accepted measured run and its duration medians.</summary>
public sealed class BenchmarkHistoryEntry
{
    /// <summary>Unique run identifier supplied by the benchmark runner.</summary>
    public string RunId { get; set; } = string.Empty;
    /// <summary>UTC completion time.</summary>
    public DateTimeOffset FinishedUtc { get; set; }
    /// <summary>Workload identity supplied by the caller.</summary>
    public string WorkloadId { get; set; } = string.Empty;
    /// <summary>Hash of the exact runner, runtime, hardware and measurement policy dimensions.</summary>
    public string EnvironmentSha256 { get; set; } = string.Empty;
    /// <summary>SHA-256 of the normalized result accepted for this record.</summary>
    public string ResultSha256 { get; set; } = string.Empty;
    /// <summary>Source identity reported by the producer, when available.</summary>
    public string SourceCommit { get; set; } = string.Empty;
    /// <summary>Lossless hashed lane identities and their recomputed measured durations.</summary>
    public Dictionary<string, BenchmarkHistoryLane> Lanes { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Accepted measurements for a single workload lane.</summary>
public sealed class BenchmarkHistoryLane
{
    /// <summary>Human-readable scenario, operation and engine label.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Median duration recomputed from all successful raw samples.</summary>
    public double MedianMs { get; set; }
    /// <summary>Independent measured iterations used by this lane.</summary>
    public int SampleCount { get; set; }
}

/// <summary>Result of a history verification or explicit accepted-run update.</summary>
public sealed class BenchmarkHistoryResult
{
    /// <summary>True only when every lane has enough comparable history and is within its threshold.</summary>
    public bool Passed { get; set; }
    /// <summary>True when history was explicitly updated, without asserting verification.</summary>
    public bool Updated { get; set; }
    /// <summary>True when at least one lane lacks the required independent history.</summary>
    public bool Calibrating { get; set; }
    /// <summary>Environment identity used for comparison.</summary>
    public string EnvironmentSha256 { get; set; } = string.Empty;
    /// <summary>Duration thresholds and their measured historical noise.</summary>
    public BenchmarkHistoryMetricResult[] Metrics { get; set; } = Array.Empty<BenchmarkHistoryMetricResult>();
    /// <summary>Missing, uncalibrated or regressed lane diagnostics.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}

/// <summary>One independently calibrated duration lane.</summary>
public sealed class BenchmarkHistoryMetricResult
{
    /// <summary>Stable lossless lane identity.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Human-readable lane label.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Current median duration.</summary>
    public double? ActualMs { get; set; }
    /// <summary>Median of accepted run medians.</summary>
    public double? BaselineMs { get; set; }
    /// <summary>Median absolute deviation of accepted run medians.</summary>
    public double? NoiseMs { get; set; }
    /// <summary>Baseline plus the largest of relative, absolute or three-noise allowances.</summary>
    public double? AllowedMs { get; set; }
    /// <summary>Number of independent accepted runs used.</summary>
    public int HistoryRuns { get; set; }
    /// <summary>True when this lane is missing or exceeds a calibrated limit.</summary>
    public bool Regressed { get; set; }
}
