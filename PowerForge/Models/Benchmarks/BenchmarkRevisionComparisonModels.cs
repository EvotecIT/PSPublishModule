namespace PowerForge;

/// <summary>
/// Describes a BenchmarkDotNet comparison between two revisions of the same repository on one machine.
/// </summary>
public sealed class BenchmarkRevisionComparisonSpec
{
    /// <summary>Any path inside the repository. Defaults to the current directory.</summary>
    public string RepositoryPath { get; set; } = string.Empty;

    /// <summary>Baseline git revision. Defaults to the repository's default branch.</summary>
    public string? Baseline { get; set; }

    /// <summary>Candidate git revision. Empty measures the current working tree, including uncommitted changes.</summary>
    public string? Candidate { get; set; }

    /// <summary>Benchmark project path, relative to the repository root (or absolute inside it).</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>BenchmarkDotNet <c>--filter</c> value.</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Target framework of the benchmark project, such as <c>net10.0</c>.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Build configuration. Defaults to <c>Release</c>.</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>Number of rounds. Each round runs both sides once; even rounds run the candidate first.</summary>
    public int Rounds { get; set; } = 2;

    /// <summary>
    /// BenchmarkDotNet arguments that replace or extend the defaults
    /// (<c>--launchCount 1 --warmupCount 5 --iterationCount 15 --outliers DontRemove --memory --exporters json</c>).
    /// An option given here replaces the default option of the same name.
    /// </summary>
    public string[] BenchmarkArguments { get; set; } = Array.Empty<string>();

    /// <summary>Folder that receives the temporary worktree and the BenchmarkDotNet artifacts. Defaults to the system temp folder.</summary>
    public string? ScratchRoot { get; set; }

    /// <summary>Keeps the BenchmarkDotNet artifacts and run logs after the comparison. The baseline worktree is always removed.</summary>
    public bool KeepArtifacts { get; set; }

    /// <summary>Optional lock directory shared by every benchmark runner on the machine.</summary>
    public string? LockPath { get; set; }

    /// <summary>How long to wait for the lock before failing.</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromHours(2);

    /// <summary>When set, each run waits until the 15-minute load average is below this value.</summary>
    public double? MaxLoadAverage { get; set; }

    /// <summary>How long one run may wait for the load average before failing.</summary>
    public TimeSpan LoadWaitTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Maximum duration of one benchmark run.</summary>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Result of a revision comparison.
/// </summary>
public sealed class BenchmarkRevisionComparisonResult
{
    /// <summary>Baseline side.</summary>
    public BenchmarkRevisionSide Baseline { get; set; } = new();

    /// <summary>Candidate side.</summary>
    public BenchmarkRevisionSide Candidate { get; set; } = new();

    /// <summary>Benchmark project, relative to the repository root.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>BenchmarkDotNet filter.</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Target framework.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Number of rounds that ran.</summary>
    public int Rounds { get; set; }

    /// <summary>Run order, one entry per run, such as <c>1:baseline</c>.</summary>
    public string[] RunOrder { get; set; } = Array.Empty<string>();

    /// <summary>BenchmarkDotNet arguments passed to every run.</summary>
    public string[] BenchmarkArguments { get; set; } = Array.Empty<string>();

    /// <summary>Runner block in the benchmark standard's form.</summary>
    public BenchmarkRunnerInfo Runner { get; set; } = new();

    /// <summary>Environment reported by BenchmarkDotNet for the first run.</summary>
    public BenchmarkEnvironmentInfo Environment { get; set; } = new();

    /// <summary>One entry per benchmark case.</summary>
    public BenchmarkRevisionComparisonRow[] Benchmarks { get; set; } = Array.Empty<BenchmarkRevisionComparisonRow>();

    /// <summary>Folder holding the kept artifacts, when <see cref="BenchmarkRevisionComparisonSpec.KeepArtifacts"/> was set.</summary>
    public string? ArtifactsPath { get; set; }

    /// <summary>When the comparison started.</summary>
    public DateTimeOffset StartedUtc { get; set; }

    /// <summary>When the comparison finished.</summary>
    public DateTimeOffset FinishedUtc { get; set; }
}

/// <summary>
/// One side of a revision comparison.
/// </summary>
public sealed class BenchmarkRevisionSide
{
    /// <summary>Label shown in reports: <c>baseline</c> or <c>candidate</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Revision as given, or <c>working tree</c>.</summary>
    public string Revision { get; set; } = string.Empty;

    /// <summary>Resolved commit.</summary>
    public string Commit { get; set; } = string.Empty;

    /// <summary>True when the side is the working tree and it has uncommitted changes.</summary>
    public bool Dirty { get; set; }
}

/// <summary>
/// Comparison of one benchmark case between the two sides.
/// </summary>
public sealed class BenchmarkRevisionComparisonRow
{
    /// <summary>BenchmarkDotNet full name, including parameters.</summary>
    public string Benchmark { get; set; } = string.Empty;

    /// <summary>Baseline measurements.</summary>
    public BenchmarkRevisionMeasurement Baseline { get; set; } = new();

    /// <summary>Candidate measurements.</summary>
    public BenchmarkRevisionMeasurement Candidate { get; set; } = new();

    /// <summary>Candidate median divided by baseline median.</summary>
    public double? Ratio { get; set; }

    /// <summary>Lowest per-round ratio.</summary>
    public double? RatioMin { get; set; }

    /// <summary>Highest per-round ratio.</summary>
    public double? RatioMax { get; set; }

    /// <summary>Candidate allocation divided by baseline allocation.</summary>
    public double? AllocatedRatio { get; set; }
}

/// <summary>
/// Measurements of one side for one benchmark case.
/// </summary>
public sealed class BenchmarkRevisionMeasurement
{
    /// <summary>Median of the per-round medians, in milliseconds.</summary>
    public double? MedianMs { get; set; }

    /// <summary>BenchmarkDotNet median of each round, in milliseconds. Null marks a round where the case failed.</summary>
    public double?[] RoundMediansMs { get; set; } = Array.Empty<double?>();

    /// <summary>Spread between rounds: (max - min) / median of the per-round medians, in percent.</summary>
    public double? SpreadPercent { get; set; }

    /// <summary>Median allocation per operation across rounds, in bytes.</summary>
    public double? AllocatedBytes { get; set; }

    /// <summary>Number of rounds in which the case did not produce a measurement.</summary>
    public int FailedRounds { get; set; }
}
