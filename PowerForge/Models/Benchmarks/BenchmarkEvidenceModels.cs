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

    /// <summary>
    /// Machine label for local diagnostics. Evidence catalogs never copy it, and the publish policy
    /// rejects published results that still carry it.
    /// </summary>
    public string MachineName { get; set; } = string.Empty;
}

/// <summary>
/// Benchmark evidence profile. The profile decides whether a lane may be published at all;
/// the publish policy decides which of its cells are publishable.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(BenchmarkKebabCaseEnumConverter<BenchmarkEvidenceProfile>))]
public enum BenchmarkEvidenceProfile
{
    /// <summary>Exploratory or shortened run. Never published.</summary>
    Diagnostic,

    /// <summary>Publishable run from any runner that meets the publish policy thresholds.</summary>
    Standard,

    /// <summary>Complete BenchmarkDotNet run that meets the same publish policy thresholds.</summary>
    Full
}

/// <summary>
/// Cross-platform benchmark evidence index (schema 5).
/// </summary>
public sealed class BenchmarkEvidenceCatalog
{
    /// <summary>Catalog schema version. Only schema 5 is read and written.</summary>
    public int SchemaVersion { get; set; } = BenchmarkEvidenceCatalogService.CurrentSchemaVersion;

    /// <summary>Platforms expected for a complete published comparison.</summary>
    public string[] ExpectedPlatforms { get; set; } = { "windows", "linux", "macos" };

    /// <summary>Area identifiers represented by the declared comparisons.</summary>
    public string[] Areas { get; set; } = Array.Empty<string>();

    /// <summary>Publish policy every lane in this catalog was evaluated against.</summary>
    public BenchmarkPublishPolicy Policy { get; set; } = BenchmarkPublishPolicy.CreateDefault();

    /// <summary>Declared libraries with licence metadata and restriction class.</summary>
    public BenchmarkLibraryDeclaration[] Libraries { get; set; } = Array.Empty<BenchmarkLibraryDeclaration>();

    /// <summary>All declared comparisons, as authored, including those without measured evidence.</summary>
    public BenchmarkComparisonDefinition[] Comparisons { get; set; } = Array.Empty<BenchmarkComparisonDefinition>();

    /// <summary>Publication eligibility derived for every declared comparison from its declaration and library classes.</summary>
    public BenchmarkComparisonEligibility[] Eligibility { get; set; } = Array.Empty<BenchmarkComparisonEligibility>();

    /// <summary>Latest lane for every lane key.</summary>
    public BenchmarkEvidenceEntry[] Entries { get; set; } = Array.Empty<BenchmarkEvidenceEntry>();

    /// <summary>Older lanes replaced by a newer run with the same lane key, kept dated with their hashes.</summary>
    public BenchmarkSupersededLane[] Superseded { get; set; } = Array.Empty<BenchmarkSupersededLane>();

    /// <summary>Availability of every declared comparison, framework and expected platform.</summary>
    public BenchmarkPlatformAvailability[] Availability { get; set; } = Array.Empty<BenchmarkPlatformAvailability>();
}

/// <summary>
/// One independently measured benchmark evidence lane. A lane is identified by
/// comparison, platform, framework, profile and lane parameters such as processor affinity.
/// </summary>
public sealed class BenchmarkEvidenceEntry
{
    /// <summary>Stable comparison identifier shared only by equivalent workloads. It never encodes framework or affinity.</summary>
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>Area identifier from the matching comparison declaration, when supplied.</summary>
    public string Area { get; set; } = string.Empty;

    /// <summary>Normalized platform identifier.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Target framework moniker such as net10.0, or empty when the runner has none.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Evidence profile.</summary>
    public BenchmarkEvidenceProfile Profile { get; set; }

    /// <summary>Additional lane dimensions such as <c>affinity</c>. Lanes differing only here are separate lanes of one comparison.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Canonical lane key built from comparison, platform, framework, profile and parameters.</summary>
    public string LaneKey { get; set; } = string.Empty;

    /// <summary>UTC timestamp from the imported result.</summary>
    public DateTimeOffset GeneratedUtc { get; set; }

    /// <summary>True when this lane is intended for public claims and passed the lane-level publish policy.</summary>
    public bool Publish { get; set; }

    /// <summary>
    /// <c>complete</c> when every declared publishable library has a publishable cell in every case;
    /// <c>partial</c> when some cells failed or were dropped by the publish policy.
    /// </summary>
    public string Completeness { get; set; } = string.Empty;

    /// <summary>Portable path or URL to the normalized run result.</summary>
    public string ResultPath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exact normalized result payload validated for this entry.</summary>
    public string ResultSha256 { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 identity of the normalized local artifact destination relative to the catalog.
    /// This prevents two lanes from overwriting one output even when the previous file is absent.
    /// </summary>
    public string ArtifactDestinationSha256 { get; set; } = string.Empty;

    /// <summary>Portable file name of the normalized artifact stored beside this catalog.</summary>
    public string ArtifactFileName { get; set; } = string.Empty;

    /// <summary>Portable relative path of the normalized artifact within this catalog's bundle.</summary>
    public string ArtifactRelativePath { get; set; } = string.Empty;

    /// <summary>Benchmark suite name.</summary>
    public string Suite { get; set; } = string.Empty;

    /// <summary>Environment captured by the benchmark runner, without the machine name.</summary>
    public BenchmarkEnvironmentInfo Environment { get; set; } = new();

    /// <summary>How the runner measured this lane.</summary>
    public BenchmarkRunnerInfo Runner { get; set; } = new();

    /// <summary>Provenance required before a lane can be published.</summary>
    public BenchmarkEvidenceProvenance Provenance { get; set; } = new();

    /// <summary>Libraries whose licence acceptance was recorded for this run.</summary>
    public string[] AcceptedLibraries { get; set; } = Array.Empty<string>();

    /// <summary>One cell per case, operation, library and parameter set, including failed cells.</summary>
    public BenchmarkEvidenceCell[] Cells { get; set; } = Array.Empty<BenchmarkEvidenceCell>();

    /// <summary>Lane-level policy findings. A published lane has none.</summary>
    public string[] PolicyIssues { get; set; } = Array.Empty<string>();

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
/// One measured cell: a case and operation for one library and parameter set.
/// Failed and skipped cells are kept with their reason so one crash never discards the rest of a run.
/// </summary>
public sealed class BenchmarkEvidenceCell
{
    /// <summary>Comparison the cell belongs to.</summary>
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>Case (scenario) name.</summary>
    public string Case { get; set; } = string.Empty;

    /// <summary>Measured operation.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Library identifier, matching a declared library id.</summary>
    public string Library { get; set; } = string.Empty;

    /// <summary>Case parameters such as row count or data shape.</summary>
    public Dictionary<string, string?> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Succeeded, Failed or Skipped.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Failure or skip reason.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Statistic reported in <see cref="ValueMs"/>: median or mean.</summary>
    public string Statistic { get; set; } = string.Empty;

    /// <summary>Reported duration in milliseconds.</summary>
    public double? ValueMs { get; set; }

    /// <summary>Confidence interval of the reported statistic.</summary>
    public BenchmarkConfidenceInterval? ConfidenceInterval { get; set; }

    /// <summary>Number of measured samples behind the statistic.</summary>
    public int SampleCount { get; set; }

    /// <summary>Number of measured samples that failed.</summary>
    public int FailureCount { get; set; }

    /// <summary>Output validation result for this cell: passed, failed or missing.</summary>
    public string Validation { get; set; } = string.Empty;

    /// <summary>Output validation detail, such as a checksum or the reason validation failed.</summary>
    public string ValidationDetail { get; set; } = string.Empty;

    /// <summary>True when the publish policy allows this cell in public claims.</summary>
    public bool Publishable { get; set; }

    /// <summary>Why the cell is not publishable.</summary>
    public string[] Issues { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Outcome against the fastest publishable cell of the same case: <c>fastest</c>, <c>even</c>
    /// (confidence intervals overlap) or <c>slower</c>. Empty for cells that are not publishable.
    /// </summary>
    public string Outcome { get; set; } = string.Empty;
}

/// <summary>Provenance a published lane must carry. The machine name is never part of it.</summary>
public sealed class BenchmarkEvidenceProvenance
{
    /// <summary>Clean source commit the benchmark was built from.</summary>
    public string GitSha { get; set; } = string.Empty;

    /// <summary>True when the source tree had no tracked or untracked changes.</summary>
    public bool GitWorktreeClean { get; set; }

    /// <summary>Operating-system description.</summary>
    public string Os { get; set; } = string.Empty;

    /// <summary>Process architecture.</summary>
    public string Architecture { get; set; } = string.Empty;

    /// <summary>Processor model.</summary>
    public string Cpu { get; set; } = string.Empty;

    /// <summary>Logical cores visible to the benchmark process.</summary>
    public int? Cores { get; set; }

    /// <summary>Runtime description.</summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>.NET SDK version, when the runner builds with one.</summary>
    public string Sdk { get; set; } = string.Empty;

    /// <summary>Package versions and hashes of the measured libraries.</summary>
    public BenchmarkPackageReference[] Packages { get; set; } = Array.Empty<BenchmarkPackageReference>();
}

/// <summary>Package identity recorded with evidence.</summary>
public sealed class BenchmarkPackageReference
{
    /// <summary>Package identifier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Exact package version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>SHA-256 or NuGet content hash of the package.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>A lane replaced by a newer run with the same lane key.</summary>
public sealed class BenchmarkSupersededLane
{
    /// <summary>Canonical lane key.</summary>
    public string LaneKey { get; set; } = string.Empty;

    /// <summary>Comparison identifier.</summary>
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>Platform.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Framework.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Profile.</summary>
    public BenchmarkEvidenceProfile Profile { get; set; }

    /// <summary>When the replaced run finished.</summary>
    public DateTimeOffset GeneratedUtc { get; set; }

    /// <summary>When the newer run that replaced it finished.</summary>
    public DateTimeOffset SupersededByUtc { get; set; }

    /// <summary>Whether the replaced lane was published.</summary>
    public bool Publish { get; set; }

    /// <summary>Result path the replaced lane referenced.</summary>
    public string ResultPath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the replaced result payload.</summary>
    public string ResultSha256 { get; set; } = string.Empty;

    /// <summary>Libraries whose measurements the stored result contains.</summary>
    public string[] Libraries { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Availability state for one declared comparison, framework and expected platform.
/// Only published (standard or full) lanes make a platform available.
/// </summary>
public sealed class BenchmarkPlatformAvailability
{
    /// <summary>Comparison identifier whose availability is described.</summary>
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>Framework whose availability is described, or empty.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Normalized platform identifier.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>True when at least one published lane exists.</summary>
    public bool Available { get; set; }

    /// <summary>Published profiles available for this platform.</summary>
    public BenchmarkEvidenceProfile[] Profiles { get; set; } = Array.Empty<BenchmarkEvidenceProfile>();

    /// <summary>Most recent published lane timestamp.</summary>
    public DateTimeOffset? LatestUtc { get; set; }
}

/// <summary>Derived publication eligibility of one declared comparison.</summary>
public sealed class BenchmarkComparisonEligibility
{
    /// <summary>Comparison identifier.</summary>
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>True when the comparison can be published.</summary>
    public bool Publishable { get; set; }

    /// <summary>Why it cannot be published.</summary>
    public string Reason { get; set; } = string.Empty;
}
