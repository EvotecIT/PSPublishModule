namespace PowerForge;

/// <summary>
/// Normalized environment identity captured with a benchmark run.
/// </summary>
public sealed class BenchmarkEnvironmentInfo
{
    /// <summary>Normalized operating-system family such as Windows, Linux, or macOS.</summary>
    public string OsFamily { get; set; } = string.Empty;

    /// <summary>Detailed operating-system description.</summary>
    public string OsDescription { get; set; } = string.Empty;

    /// <summary>Operating-system architecture.</summary>
    public string OsArchitecture { get; set; } = string.Empty;

    /// <summary>Benchmark process architecture.</summary>
    public string ProcessArchitecture { get; set; } = string.Empty;

    /// <summary>Processor model reported by the benchmark host.</summary>
    public string ProcessorName { get; set; } = string.Empty;

    /// <summary>Number of physical processors when reported by the benchmark host.</summary>
    public int? PhysicalProcessorCount { get; set; }

    /// <summary>Number of physical cores when reported by the benchmark host.</summary>
    public int? PhysicalCoreCount { get; set; }

    /// <summary>Number of logical processors visible to the benchmark process.</summary>
    public int? LogicalCoreCount { get; set; }

    /// <summary>Runtime description used by the benchmark process.</summary>
    public string RuntimeVersion { get; set; } = string.Empty;

    /// <summary>.NET SDK version used to launch or build the benchmark.</summary>
    public string DotNetSdkVersion { get; set; } = string.Empty;

    /// <summary>Benchmark runner name and version.</summary>
    public string Runner { get; set; } = string.Empty;

    /// <summary>Machine label, when the producer intentionally publishes it.</summary>
    public string MachineName { get; set; } = string.Empty;
}

/// <summary>
/// Cross-platform benchmark evidence index.
/// </summary>
public sealed class BenchmarkEvidenceCatalog
{
    /// <summary>Catalog schema version.</summary>
    public int SchemaVersion { get; set; } = 3;

    /// <summary>Platforms expected for a complete published comparison.</summary>
    public string[] ExpectedPlatforms { get; set; } = { "windows", "linux", "macos" };

    /// <summary>Independent platform and run-mode evidence lanes.</summary>
    public BenchmarkEvidenceEntry[] Entries { get; set; } = Array.Empty<BenchmarkEvidenceEntry>();

    /// <summary>Availability of every expected platform.</summary>
    public BenchmarkPlatformAvailability[] Availability { get; set; } = Array.Empty<BenchmarkPlatformAvailability>();
}

/// <summary>
/// One independently measured benchmark evidence lane.
/// </summary>
public sealed class BenchmarkEvidenceEntry
{
    /// <summary>Stable comparison identifier shared only by equivalent workloads.</summary>
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>Normalized platform identifier.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Run mode such as quick or full.</summary>
    public string RunMode { get; set; } = string.Empty;

    /// <summary>UTC timestamp from the imported result.</summary>
    public DateTimeOffset GeneratedUtc { get; set; }

    /// <summary>True when this lane is intended for public claims.</summary>
    public bool Publish { get; set; }

    /// <summary>Portable path or URL to the normalized run result.</summary>
    public string ResultPath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exact normalized result payload validated for this entry.</summary>
    public string ResultSha256 { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 identity of the normalized local artifact destination relative to the catalog.
    /// This prevents two lanes from overwriting one output even when the previous file is absent.
    /// </summary>
    public string ArtifactDestinationSha256 { get; set; } = string.Empty;

    /// <summary>Benchmark suite name.</summary>
    public string Suite { get; set; } = string.Empty;

    /// <summary>Environment captured by the benchmark runner.</summary>
    public BenchmarkEnvironmentInfo Environment { get; set; } = new();

    /// <summary>
    /// Exact workload, fixture, dependency, and source dimensions used to decide whether
    /// different platform lanes are comparable.
    /// </summary>
    public Dictionary<string, string> Compatibility { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when this entry matches the other publishable lanes in its comparison group.</summary>
    public bool Comparable { get; set; } = true;

    /// <summary>Reasons this entry cannot be compared directly with another lane.</summary>
    public string[] CompatibilityIssues { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Availability state for one expected benchmark platform.
/// </summary>
public sealed class BenchmarkPlatformAvailability
{
    /// <summary>Comparison identifier whose availability is described.</summary>
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>Run mode whose availability is described.</summary>
    public string RunMode { get; set; } = string.Empty;

    /// <summary>Normalized platform identifier.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>True when at least one evidence lane exists for this platform.</summary>
    public bool Available { get; set; }

    /// <summary>Run modes currently available for this platform.</summary>
    public string[] RunModes { get; set; } = Array.Empty<string>();

    /// <summary>Most recent lane timestamp for this platform.</summary>
    public DateTimeOffset? LatestUtc { get; set; }
}
