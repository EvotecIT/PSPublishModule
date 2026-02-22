namespace PowerForge;

/// <summary>
/// Typed specification for running a dotnet publish workflow driven by JSON configuration.
/// Intended for producing small, reproducible distributable outputs (single-file, self-contained, AOT optional).
/// </summary>
public sealed class DotNetPublishSpec
{
    /// <summary>
    /// Optional JSON schema reference (for editors and validation tooling).
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>
    /// Optional schema version for external tooling.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Optional active profile name.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// Optional named profiles used to specialize target selection and publish dimensions.
    /// </summary>
    public DotNetPublishProfile[] Profiles { get; set; } = Array.Empty<DotNetPublishProfile>();

    /// <summary>
    /// Optional project catalog used by targets via <c>ProjectId</c>.
    /// </summary>
    public DotNetPublishProject[] Projects { get; set; } = Array.Empty<DotNetPublishProject>();

    /// <summary>
    /// Optional global matrix defaults and filters.
    /// </summary>
    public DotNetPublishMatrix Matrix { get; set; } = new();

    /// <summary>
    /// Global dotnet settings (restore/build behavior, solution path, default runtimes).
    /// </summary>
    public DotNetPublishDotNetOptions DotNet { get; set; } = new();

    /// <summary>
    /// Publish targets.
    /// </summary>
    public DotNetPublishTarget[] Targets { get; set; } = Array.Empty<DotNetPublishTarget>();

    /// <summary>
    /// Optional installer definitions (for example MSI prepare/build flows) bound to published targets.
    /// </summary>
    public DotNetPublishInstaller[] Installers { get; set; } = Array.Empty<DotNetPublishInstaller>();

    /// <summary>
    /// Optional benchmark gates (extract + baseline verify/update) executed near pipeline end.
    /// </summary>
    public DotNetPublishBenchmarkGate[] BenchmarkGates { get; set; } = Array.Empty<DotNetPublishBenchmarkGate>();

    /// <summary>
    /// Optional manifest output configuration.
    /// </summary>
    public DotNetPublishOutputs Outputs { get; set; } = new();
}

/// <summary>
/// Named profile that can specialize dotnet publish target and matrix defaults.
/// </summary>
public sealed class DotNetPublishProfile
{
    /// <summary>Profile name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When true, used by default when <see cref="DotNetPublishSpec.Profile"/> is not set.</summary>
    public bool Default { get; set; }

    /// <summary>Optional target-name filter.</summary>
    public string[] Targets { get; set; } = Array.Empty<string>();

    /// <summary>Optional runtime override for all selected targets.</summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>Optional framework override for all selected targets.</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>Optional style override for all selected targets.</summary>
    public DotNetPublishStyle? Style { get; set; }
}

/// <summary>
/// Named project catalog entry.
/// </summary>
public sealed class DotNetPublishProject
{
    /// <summary>Project identifier referenced by targets.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Path to the project file (*.csproj).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Optional project grouping tag (metadata only).</summary>
    public string? Group { get; set; }
}

/// <summary>
/// Installer definition used for packaging flows (for example MSI prepare/build).
/// </summary>
public sealed class DotNetPublishInstaller
{
    /// <summary>Installer identifier used in paths and step keys.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Source publish target name this installer prepares payloads from.</summary>
    public string PrepareFromTarget { get; set; } = string.Empty;

    /// <summary>
    /// Optional payload staging path template.
    /// Supports tokens: <c>{installer}</c>, <c>{target}</c>, <c>{rid}</c>, <c>{framework}</c>, <c>{style}</c>, <c>{configuration}</c>.
    /// </summary>
    public string? StagingPath { get; set; }

    /// <summary>
    /// Optional prepare manifest path template.
    /// Supports tokens: <c>{installer}</c>, <c>{target}</c>, <c>{rid}</c>, <c>{framework}</c>, <c>{style}</c>, <c>{configuration}</c>.
    /// </summary>
    public string? ManifestPath { get; set; }

    /// <summary>
    /// Optional installer project ID (resolved from <see cref="DotNetPublishSpec.Projects"/>) used by <c>msi.build</c>.
    /// </summary>
    public string? InstallerProjectId { get; set; }

    /// <summary>
    /// Optional installer project path (for example <c>*.wixproj</c>).
    /// When set, <c>msi.build</c> uses this path directly instead of <see cref="InstallerProjectId"/>.
    /// </summary>
    public string? InstallerProjectPath { get; set; }

    /// <summary>
    /// Harvest mode for payload tree processing during <c>msi.prepare</c>.
    /// </summary>
    public DotNetPublishMsiHarvestMode Harvest { get; set; } = DotNetPublishMsiHarvestMode.None;

    /// <summary>
    /// Optional harvest output path template for generated WiX fragment.
    /// Supports tokens: <c>{installer}</c>, <c>{target}</c>, <c>{rid}</c>, <c>{framework}</c>, <c>{style}</c>, <c>{configuration}</c>.
    /// </summary>
    public string? HarvestPath { get; set; }

    /// <summary>
    /// Optional WiX <c>DirectoryRef</c> identifier for generated harvest output. Default: <c>INSTALLFOLDER</c>.
    /// </summary>
    public string? HarvestDirectoryRefId { get; set; }

    /// <summary>
    /// Optional WiX component group identifier template for generated harvest output.
    /// Supports tokens: <c>{installer}</c>, <c>{target}</c>, <c>{rid}</c>, <c>{framework}</c>, <c>{style}</c>, <c>{configuration}</c>.
    /// </summary>
    public string? HarvestComponentGroupId { get; set; }

    /// <summary>
    /// Optional MSI versioning policy used by <c>msi.build</c>.
    /// </summary>
    public DotNetPublishMsiVersionOptions? Versioning { get; set; }

    /// <summary>
    /// Optional MSI signing options applied by <c>msi.sign</c>.
    /// Reuses the same policy contract as publish signing.
    /// </summary>
    public DotNetPublishSignOptions? Sign { get; set; }

    /// <summary>
    /// Optional client-license injection passed to MSI build as an MSBuild property.
    /// </summary>
    public DotNetPublishMsiClientLicenseOptions? ClientLicense { get; set; }
}

/// <summary>
/// MSI version policy options for date-floor + monotonic versioning.
/// </summary>
public sealed class DotNetPublishMsiVersionOptions
{
    /// <summary>
    /// Enables MSI version policy.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Major version segment (0-255). Default: 1.
    /// </summary>
    public int Major { get; set; } = 1;

    /// <summary>
    /// Minor version segment (0-255). Default: 0.
    /// </summary>
    public int Minor { get; set; }

    /// <summary>
    /// Optional floor date in UTC (<c>yyyy-MM-dd</c> or <c>yyyyMMdd</c>).
    /// The patch segment will be at least the day number since 2000-01-01 for this date.
    /// </summary>
    public string? FloorDateUtc { get; set; }

    /// <summary>
    /// When true, uses a persisted state file and bumps patch monotonically.
    /// Default: true.
    /// </summary>
    public bool Monotonic { get; set; } = true;

    /// <summary>
    /// Optional state file path template used when <see cref="Monotonic"/> is true.
    /// Supports tokens: <c>{installer}</c>, <c>{target}</c>, <c>{rid}</c>, <c>{framework}</c>, <c>{style}</c>, <c>{configuration}</c>.
    /// </summary>
    public string? StatePath { get; set; }

    /// <summary>
    /// MSBuild property name used to pass resolved MSI version. Default: <c>ProductVersion</c>.
    /// </summary>
    public string? PropertyName { get; set; } = "ProductVersion";

    /// <summary>
    /// Maximum allowed patch segment. Default: 65535.
    /// </summary>
    public int PatchCap { get; set; } = 65535;
}

/// <summary>
/// Optional client-license injection options for MSI build.
/// </summary>
public sealed class DotNetPublishMsiClientLicenseOptions
{
    /// <summary>
    /// Enables client-license injection.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Optional logical client identifier used by <see cref="Path"/> and <see cref="PathTemplate"/> tokens.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Optional explicit client-license path template.
    /// Supports tokens: <c>{installer}</c>, <c>{target}</c>, <c>{rid}</c>, <c>{framework}</c>, <c>{style}</c>,
    /// <c>{configuration}</c>, <c>{clientId}</c>.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Optional fallback client-license path template used when <see cref="Path"/> is not set.
    /// Default: <c>Installer/Clients/{clientId}/{target}.txlic</c>.
    /// </summary>
    public string? PathTemplate { get; set; } = "Installer/Clients/{clientId}/{target}.txlic";

    /// <summary>
    /// MSBuild property name used to pass resolved license path.
    /// Default: <c>ClientLicensePath</c>.
    /// </summary>
    public string? PropertyName { get; set; } = "ClientLicensePath";

    /// <summary>
    /// Policy applied when enabled but the resolved license file is missing.
    /// </summary>
    public DotNetPublishPolicyMode OnMissingFile { get; set; } = DotNetPublishPolicyMode.Warn;
}

/// <summary>
/// Benchmark gate definition (metric extraction + baseline verify/update).
/// </summary>
public sealed class DotNetPublishBenchmarkGate
{
    /// <summary>Stable gate identifier used in step keys and reports.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>When false, gate is ignored.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to benchmark input file (JSON payload or text log).
    /// Relative paths are resolved against project root.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Baseline file path used for verify/update mode.
    /// Relative paths are resolved against project root.
    /// </summary>
    public string BaselinePath { get; set; } = string.Empty;

    /// <summary>Verify current metrics or update baseline file.</summary>
    public DotNetPublishBaselineMode BaselineMode { get; set; } = DotNetPublishBaselineMode.Verify;

    /// <summary>
    /// When true in verify mode, newly discovered metrics missing from baseline fail the gate.
    /// </summary>
    public bool FailOnNew { get; set; } = true;

    /// <summary>
    /// Relative tolerance used to compute allowed metric cap from baseline.
    /// Allowed = max(baseline * (1 + relativeTolerance), baseline + absoluteToleranceMs).
    /// </summary>
    public double RelativeTolerance { get; set; } = 0.10;

    /// <summary>Absolute tolerance in milliseconds used alongside relative tolerance.</summary>
    public double AbsoluteToleranceMs { get; set; } = 0;

    /// <summary>Policy applied when a metric exceeds computed allowed cap.</summary>
    public DotNetPublishPolicyMode OnRegression { get; set; } = DotNetPublishPolicyMode.Fail;

    /// <summary>Policy applied when an expected metric cannot be extracted from source input.</summary>
    public DotNetPublishPolicyMode OnMissingMetric { get; set; } = DotNetPublishPolicyMode.Fail;

    /// <summary>Metric extraction definitions.</summary>
    public DotNetPublishBenchmarkMetric[] Metrics { get; set; } = Array.Empty<DotNetPublishBenchmarkMetric>();
}

/// <summary>
/// Extraction rule for one benchmark metric.
/// </summary>
public sealed class DotNetPublishBenchmarkMetric
{
    /// <summary>Metric identifier in results and baseline map.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Extraction source type (JSON path or regex).</summary>
    public DotNetPublishBenchmarkMetricSource Source { get; set; } = DotNetPublishBenchmarkMetricSource.JsonPath;

    /// <summary>JSON dot-path used when <see cref="Source"/> is <c>JsonPath</c>.</summary>
    public string? Path { get; set; }

    /// <summary>Regex pattern used when <see cref="Source"/> is <c>Regex</c>.</summary>
    public string? Pattern { get; set; }

    /// <summary>Regex capture group index used when <see cref="Source"/> is <c>Regex</c>. Default: 1.</summary>
    public int Group { get; set; } = 1;

    /// <summary>Aggregation applied to extracted values. Default: Last.</summary>
    public DotNetPublishBenchmarkMetricAggregation Aggregation { get; set; } = DotNetPublishBenchmarkMetricAggregation.Last;

    /// <summary>When true, missing metric is treated according to gate <c>OnMissingMetric</c> policy.</summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// Global matrix defaults and filters for target expansion.
/// </summary>
public sealed class DotNetPublishMatrix
{
    /// <summary>Default runtimes applied when a target does not specify runtimes.</summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>Default frameworks applied when a target does not specify frameworks.</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>Default styles applied when a target does not specify styles.</summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    /// <summary>
    /// Optional include rules. When non-empty, only combinations matching at least one rule are kept.
    /// </summary>
    public DotNetPublishMatrixRule[] Include { get; set; } = Array.Empty<DotNetPublishMatrixRule>();

    /// <summary>
    /// Optional exclude rules. Matching combinations are removed after include filtering.
    /// </summary>
    public DotNetPublishMatrixRule[] Exclude { get; set; } = Array.Empty<DotNetPublishMatrixRule>();
}

/// <summary>
/// Matrix include/exclude rule with optional wildcard patterns.
/// </summary>
public sealed class DotNetPublishMatrixRule
{
    /// <summary>Optional target-name patterns (`*` and `?` supported).</summary>
    public string[] Targets { get; set; } = Array.Empty<string>();

    /// <summary>Optional runtime pattern (`*` and `?` supported).</summary>
    public string? Runtime { get; set; }

    /// <summary>Optional framework pattern (`*` and `?` supported).</summary>
    public string? Framework { get; set; }

    /// <summary>Optional style pattern (`*` and `?` supported).</summary>
    public string? Style { get; set; }
}

/// <summary>
/// Global options for dotnet restore/build/publish.
/// </summary>
public sealed class DotNetPublishDotNetOptions
{
    /// <summary>
    /// Optional project root. When omitted, the directory containing the config file is used.
    /// All relative paths in the spec are resolved against this root.
    /// </summary>
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// When true, allows publish output and zip paths to resolve outside <see cref="ProjectRoot"/>.
    /// Default: false.
    /// </summary>
    public bool AllowOutputOutsideProjectRoot { get; set; }

    /// <summary>
    /// When true, allows manifest and checksum output paths to resolve outside <see cref="ProjectRoot"/>.
    /// Default: false.
    /// </summary>
    public bool AllowManifestOutsideProjectRoot { get; set; }

    /// <summary>
    /// When true, checks output directory for locked files before publish/copy operations.
    /// Default: true.
    /// </summary>
    public bool LockedOutputGuard { get; set; } = true;

    /// <summary>
    /// Policy applied when locked files are detected in output directory.
    /// Default: Fail.
    /// </summary>
    public DotNetPublishPolicyMode OnLockedOutput { get; set; } = DotNetPublishPolicyMode.Fail;

    /// <summary>
    /// Maximum number of locked-file samples included in diagnostics. Default: 5.
    /// </summary>
    public int LockedOutputSampleLimit { get; set; } = 5;

    /// <summary>
    /// Optional solution path to restore/clean/build before publishing. When omitted, the pipeline restores/builds each target project.
    /// </summary>
    public string? SolutionPath { get; set; }

    /// <summary>
    /// Build configuration (Release/Debug). Default: Release.
    /// </summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// When true, runs <c>dotnet restore</c> before building/publishing.
    /// </summary>
    public bool Restore { get; set; } = true;

    /// <summary>
    /// When true, runs <c>dotnet clean</c> before building/publishing.
    /// </summary>
    public bool Clean { get; set; }

    /// <summary>
    /// When true, runs <c>dotnet build</c> before publishing and uses <c>--no-build</c> in publish by default.
    /// </summary>
    public bool Build { get; set; } = true;

    /// <summary>
    /// When true, publishes with <c>--no-restore</c> (recommended when <see cref="Restore"/> is true).
    /// </summary>
    public bool NoRestoreInPublish { get; set; } = true;

    /// <summary>
    /// When true, publishes with <c>--no-build</c> (recommended when <see cref="Build"/> is true).
    /// </summary>
    public bool NoBuildInPublish { get; set; } = true;

    /// <summary>
    /// Default runtime identifiers to publish for (when a target does not specify its own runtimes).
    /// </summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional MSBuild properties passed to build/publish (as <c>/p:Name=Value</c>).
    /// </summary>
    public Dictionary<string, string>? MsBuildProperties { get; set; }
}

/// <summary>
/// Output settings for dotnet publish pipeline manifests.
/// </summary>
public sealed class DotNetPublishOutputs
{
    /// <summary>
    /// Optional path for a JSON manifest file that summarizes produced artefacts.
    /// When omitted, defaults to <c>Artifacts/DotNetPublish/manifest.json</c> under the project root.
    /// </summary>
    public string? ManifestJsonPath { get; set; }

    /// <summary>
    /// Optional path for a text manifest file that summarizes produced artefacts.
    /// When omitted, defaults to <c>Artifacts/DotNetPublish/manifest.txt</c> under the project root.
    /// </summary>
    public string? ManifestTextPath { get; set; }

    /// <summary>
    /// Optional path for a SHA256 checksums file.
    /// When set, the pipeline emits checksums for produced artefacts and manifest files.
    /// </summary>
    public string? ChecksumsPath { get; set; }

    /// <summary>
    /// Optional path for run report JSON (step timings, artifact/signing summary, gate outcomes).
    /// </summary>
    public string? RunReportPath { get; set; }
}
