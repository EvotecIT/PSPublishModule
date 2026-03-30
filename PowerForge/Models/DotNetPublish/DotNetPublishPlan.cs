namespace PowerForge;

/// <summary>
/// Planned execution for a dotnet publish run (resolved paths + ordered steps).
/// </summary>
public sealed class DotNetPublishPlan
{
    /// <summary>Project root used for resolving relative paths.</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>When true, output/zip paths may resolve outside <see cref="ProjectRoot"/>.</summary>
    public bool AllowOutputOutsideProjectRoot { get; set; }

    /// <summary>When true, manifest/checksum paths may resolve outside <see cref="ProjectRoot"/>.</summary>
    public bool AllowManifestOutsideProjectRoot { get; set; }

    /// <summary>When true, checks output directory for locked files before publish/copy operations.</summary>
    public bool LockedOutputGuard { get; set; } = true;

    /// <summary>Policy applied when locked output files are detected.</summary>
    public DotNetPublishPolicyMode OnLockedOutput { get; set; } = DotNetPublishPolicyMode.Fail;

    /// <summary>Maximum number of locked-file samples included in diagnostics.</summary>
    public int LockedOutputSampleLimit { get; set; } = 5;

    /// <summary>Build configuration (Release/Debug).</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>Optional resolved solution path.</summary>
    public string? SolutionPath { get; set; }

    /// <summary>When true, runs dotnet restore before publishing.</summary>
    public bool Restore { get; set; }

    /// <summary>When true, runs dotnet clean before publishing.</summary>
    public bool Clean { get; set; }

    /// <summary>When true, runs dotnet build before publishing.</summary>
    public bool Build { get; set; }

    /// <summary>When true, uses --no-restore during publish.</summary>
    public bool NoRestoreInPublish { get; set; }

    /// <summary>When true, uses --no-build during publish.</summary>
    public bool NoBuildInPublish { get; set; }

    /// <summary>Resolved MSBuild properties.</summary>
    public Dictionary<string, string> MsBuildProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolved targets (paths + publish options).</summary>
    public DotNetPublishTargetPlan[] Targets { get; set; } = Array.Empty<DotNetPublishTargetPlan>();

    /// <summary>Resolved bundle definitions.</summary>
    public DotNetPublishBundlePlan[] Bundles { get; set; } = Array.Empty<DotNetPublishBundlePlan>();

    /// <summary>Resolved installer definitions.</summary>
    public DotNetPublishInstallerPlan[] Installers { get; set; } = Array.Empty<DotNetPublishInstallerPlan>();

    /// <summary>Resolved Microsoft Store / MSIX packaging definitions.</summary>
    public DotNetPublishStorePackagePlan[] StorePackages { get; set; } = Array.Empty<DotNetPublishStorePackagePlan>();

    /// <summary>Resolved benchmark gates.</summary>
    public DotNetPublishBenchmarkGatePlan[] BenchmarkGates { get; set; } = Array.Empty<DotNetPublishBenchmarkGatePlan>();

    /// <summary>Resolved output settings.</summary>
    public DotNetPublishOutputs Outputs { get; set; } = new();

    /// <summary>Ordered steps that will be executed.</summary>
    public DotNetPublishStep[] Steps { get; set; } = Array.Empty<DotNetPublishStep>();
}

/// <summary>
/// Resolved plan entry for a single publish target.
/// </summary>
public sealed class DotNetPublishTargetPlan
{
    /// <summary>Target name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Target kind.</summary>
    public DotNetPublishTargetKind Kind { get; set; } = DotNetPublishTargetKind.Unknown;

    /// <summary>Resolved project path (*.csproj).</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Resolved publish options.</summary>
    public DotNetPublishPublishOptions Publish { get; set; } = new();

    /// <summary>Resolved framework/runtime/style publish combinations for this target.</summary>
    public DotNetPublishTargetCombination[] Combinations { get; set; } = Array.Empty<DotNetPublishTargetCombination>();
}

/// <summary>
/// Resolved publish combination (framework + runtime + style) for a target.
/// </summary>
public sealed class DotNetPublishTargetCombination
{
    /// <summary>Target framework.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Runtime identifier.</summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>Publish style.</summary>
    public DotNetPublishStyle Style { get; set; }
}

/// <summary>
/// Resolved installer definition for packaging flows.
/// </summary>
public sealed class DotNetPublishInstallerPlan
{
    /// <summary>Installer identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Source target used for payload preparation.</summary>
    public string PrepareFromTarget { get; set; } = string.Empty;

    /// <summary>Optional source bundle identifier used for payload preparation.</summary>
    public string? PrepareFromBundleId { get; set; }

    /// <summary>Optional runtime filters for installer generation.</summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>Optional framework filters for installer generation.</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>Optional style filters for installer generation.</summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    /// <summary>Optional payload staging path template.</summary>
    public string? StagingPath { get; set; }

    /// <summary>Optional prepare manifest path template.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>Optional installer project ID used for project-catalog resolution.</summary>
    public string? InstallerProjectId { get; set; }

    /// <summary>Optional resolved installer project path (for example wixproj path).</summary>
    public string? InstallerProjectPath { get; set; }

    /// <summary>Harvest mode for payload tree processing.</summary>
    public DotNetPublishMsiHarvestMode Harvest { get; set; } = DotNetPublishMsiHarvestMode.None;

    /// <summary>Optional harvest output path template.</summary>
    public string? HarvestPath { get; set; }

    /// <summary>Optional WiX directory reference ID.</summary>
    public string? HarvestDirectoryRefId { get; set; }

    /// <summary>Optional WiX component group ID template.</summary>
    public string? HarvestComponentGroupId { get; set; }

    /// <summary>Optional MSI version policy used by build steps.</summary>
    public DotNetPublishMsiVersionOptions? Versioning { get; set; }

    /// <summary>Optional installer-specific MSBuild properties passed to <c>msi.build</c>.</summary>
    public Dictionary<string, string>? MsBuildProperties { get; set; }

    /// <summary>Optional MSI signing options used by sign steps.</summary>
    public DotNetPublishSignOptions? Sign { get; set; }

    /// <summary>Optional client-license injection options used by MSI build steps.</summary>
    public DotNetPublishMsiClientLicenseOptions? ClientLicense { get; set; }
}

/// <summary>
/// Resolved Microsoft Store / MSIX packaging definition.
/// </summary>
public sealed class DotNetPublishStorePackagePlan
{
    /// <summary>Store package identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Source target used for Store packaging.</summary>
    public string PrepareFromTarget { get; set; } = string.Empty;

    /// <summary>Optional runtime filters for Store package generation.</summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>Optional framework filters for Store package generation.</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>Optional style filters for Store package generation.</summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    /// <summary>Optional packaging project ID used for project-catalog resolution.</summary>
    public string? PackagingProjectId { get; set; }

    /// <summary>Optional resolved packaging project path (for example <c>*.wapproj</c>).</summary>
    public string? PackagingProjectPath { get; set; }

    /// <summary>Optional resolved Store package output path template.</summary>
    public string? OutputPath { get; set; }

    /// <summary>When true, clears the Store package output directory before build.</summary>
    public bool ClearOutput { get; set; } = true;

    /// <summary>Store packaging build mode.</summary>
    public DotNetPublishStoreBuildMode BuildMode { get; set; } = DotNetPublishStoreBuildMode.StoreUpload;

    /// <summary>Appx/MSIX bundle behavior.</summary>
    public DotNetPublishStoreBundleMode Bundle { get; set; } = DotNetPublishStoreBundleMode.Auto;

    /// <summary>When true, generates an app installer file when the packaging project supports it.</summary>
    public bool GenerateAppInstaller { get; set; }

    /// <summary>Optional Store packaging MSBuild properties.</summary>
    public Dictionary<string, string>? MsBuildProperties { get; set; }
}

/// <summary>
/// Resolved bundle definition for composition flows.
/// </summary>
public sealed class DotNetPublishBundlePlan
{
    /// <summary>Bundle identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Primary source target used for bundle composition.</summary>
    public string PrepareFromTarget { get; set; } = string.Empty;

    /// <summary>Optional runtime filters for bundle generation.</summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>Optional framework filters for bundle generation.</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>Optional style filters for bundle generation.</summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    /// <summary>Optional resolved bundle output path template.</summary>
    public string? OutputPath { get; set; }

    /// <summary>When true, clears the bundle output directory before composition.</summary>
    public bool ClearOutput { get; set; } = true;

    /// <summary>When true, creates a bundle zip.</summary>
    public bool Zip { get; set; }

    /// <summary>Optional resolved bundle zip path template.</summary>
    public string? ZipPath { get; set; }

    /// <summary>Optional bundle zip name template.</summary>
    public string? ZipNameTemplate { get; set; }

    /// <summary>Additional published target includes.</summary>
    public DotNetPublishBundleIncludePlan[] Includes { get; set; } = Array.Empty<DotNetPublishBundleIncludePlan>();

    /// <summary>Bundle post-copy scripts.</summary>
    public DotNetPublishBundleScriptPlan[] Scripts { get; set; } = Array.Empty<DotNetPublishBundleScriptPlan>();

    /// <summary>Optional built-in bundle post-processing actions.</summary>
    public DotNetPublishBundlePostProcessOptions? PostProcess { get; set; }
}

/// <summary>
/// Resolved include target for a bundle.
/// </summary>
public sealed class DotNetPublishBundleIncludePlan
{
    /// <summary>Included target name.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Optional bundle subdirectory for copied files.</summary>
    public string? Subdirectory { get; set; }

    /// <summary>Optional framework override for the included target.</summary>
    public string? Framework { get; set; }

    /// <summary>Optional runtime override for the included target.</summary>
    public string? Runtime { get; set; }

    /// <summary>Optional style override for the included target.</summary>
    public DotNetPublishStyle? Style { get; set; }

    /// <summary>When true, missing include artefacts fail the bundle step.</summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// Resolved PowerShell hook for bundle composition.
/// </summary>
public sealed class DotNetPublishBundleScriptPlan
{
    /// <summary>Resolved script path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Script arguments with templates preserved for step-time expansion.</summary>
    public string[] Arguments { get; set; } = Array.Empty<string>();

    /// <summary>Optional working directory template.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Maximum script execution time in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>When true, prefer pwsh.</summary>
    public bool PreferPwsh { get; set; } = true;

    /// <summary>When true, script failures fail the bundle step.</summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// Resolved benchmark gate definition.
/// </summary>
public sealed class DotNetPublishBenchmarkGatePlan
{
    /// <summary>Gate identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>When false, gate is ignored.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Resolved source input path.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Resolved baseline file path.</summary>
    public string BaselinePath { get; set; } = string.Empty;

    /// <summary>Verify or update mode.</summary>
    public DotNetPublishBaselineMode BaselineMode { get; set; } = DotNetPublishBaselineMode.Verify;

    /// <summary>When true in verify mode, missing baseline entries for extracted metrics fail the gate.</summary>
    public bool FailOnNew { get; set; } = true;

    /// <summary>Relative tolerance used for allowed-cap calculation.</summary>
    public double RelativeTolerance { get; set; } = 0.10;

    /// <summary>Absolute tolerance in milliseconds used for allowed-cap calculation.</summary>
    public double AbsoluteToleranceMs { get; set; }

    /// <summary>Policy applied on regression.</summary>
    public DotNetPublishPolicyMode OnRegression { get; set; } = DotNetPublishPolicyMode.Fail;

    /// <summary>Policy applied when expected metric is missing in source input.</summary>
    public DotNetPublishPolicyMode OnMissingMetric { get; set; } = DotNetPublishPolicyMode.Fail;

    /// <summary>Resolved metric extraction rules.</summary>
    public DotNetPublishBenchmarkMetricPlan[] Metrics { get; set; } = Array.Empty<DotNetPublishBenchmarkMetricPlan>();
}

/// <summary>
/// Resolved benchmark metric extraction rule.
/// </summary>
public sealed class DotNetPublishBenchmarkMetricPlan
{
    /// <summary>Metric identifier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Extraction source type.</summary>
    public DotNetPublishBenchmarkMetricSource Source { get; set; } = DotNetPublishBenchmarkMetricSource.JsonPath;

    /// <summary>JSON path used for JsonPath source.</summary>
    public string? Path { get; set; }

    /// <summary>Regex pattern used for Regex source.</summary>
    public string? Pattern { get; set; }

    /// <summary>Regex capture group index for Regex source.</summary>
    public int Group { get; set; } = 1;

    /// <summary>Aggregation for extracted values.</summary>
    public DotNetPublishBenchmarkMetricAggregation Aggregation { get; set; } = DotNetPublishBenchmarkMetricAggregation.Last;

    /// <summary>Whether missing metric should be treated per OnMissingMetric policy.</summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// A single executable step in the dotnet publish pipeline.
/// </summary>
public sealed class DotNetPublishStep
{
    /// <summary>Stable step key used to map progress updates.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Step kind.</summary>
    public DotNetPublishStepKind Kind { get; set; }

    /// <summary>Human-friendly step title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional target name for publish steps.</summary>
    public string? TargetName { get; set; }

    /// <summary>Optional target framework for publish steps.</summary>
    public string? Framework { get; set; }

    /// <summary>Optional runtime identifier for publish steps.</summary>       
    public string? Runtime { get; set; }

    /// <summary>Optional publish style for publish steps.</summary>
    public DotNetPublishStyle? Style { get; set; }

    /// <summary>Optional installer identifier for installer-related steps.</summary>
    public string? InstallerId { get; set; }

    /// <summary>Optional Store package identifier for Store-related steps.</summary>
    public string? StorePackageId { get; set; }

    /// <summary>Optional bundle identifier for bundle-related steps.</summary>
    public string? BundleId { get; set; }

    /// <summary>Optional gate identifier for benchmark-related steps.</summary>
    public string? GateId { get; set; }

    /// <summary>Optional resolved payload staging path for installer prepare steps.</summary>
    public string? StagingPath { get; set; }

    /// <summary>Optional resolved prepare manifest path for installer prepare steps.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>Optional resolved harvest path for installer prepare steps.</summary>
    public string? HarvestPath { get; set; }

    /// <summary>Optional resolved WiX directory reference ID for harvest output.</summary>
    public string? HarvestDirectoryRefId { get; set; }

    /// <summary>Optional resolved WiX component group ID for harvest output.</summary>
    public string? HarvestComponentGroupId { get; set; }

    /// <summary>Optional resolved installer project path for build steps.</summary>
    public string? InstallerProjectPath { get; set; }

    /// <summary>Optional resolved Store packaging project path for Store build steps.</summary>
    public string? StorePackageProjectPath { get; set; }

    /// <summary>Optional resolved bundle output path for bundle steps.</summary>
    public string? BundleOutputPath { get; set; }

    /// <summary>Optional resolved bundle zip path for bundle steps.</summary>
    public string? BundleZipPath { get; set; }

    /// <summary>Optional resolved Store package output path for Store build steps.</summary>
    public string? StorePackageOutputPath { get; set; }
}
