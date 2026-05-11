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
    /// Optional named signing profiles reused by targets and installers.
    /// </summary>
    public Dictionary<string, DotNetPublishSignOptions>? SigningProfiles { get; set; }

    /// <summary>
    /// Publish targets.
    /// </summary>
    public DotNetPublishTarget[] Targets { get; set; } = Array.Empty<DotNetPublishTarget>();

    /// <summary>
    /// Optional portable/distribution bundle definitions composed from published targets.
    /// </summary>
    public DotNetPublishBundle[] Bundles { get; set; } = Array.Empty<DotNetPublishBundle>();

    /// <summary>
    /// Optional installer definitions (for example MSI prepare/build flows) bound to published targets.
    /// </summary>
    public DotNetPublishInstaller[] Installers { get; set; } = Array.Empty<DotNetPublishInstaller>();

    /// <summary>
    /// Optional Microsoft Store / MSIX packaging definitions bound to published targets.
    /// </summary>
    public DotNetPublishStorePackage[] StorePackages { get; set; } = Array.Empty<DotNetPublishStorePackage>();

    /// <summary>
    /// Optional benchmark gates (extract + baseline verify/update) executed near pipeline end.
    /// </summary>
    public DotNetPublishBenchmarkGate[] BenchmarkGates { get; set; } = Array.Empty<DotNetPublishBenchmarkGate>();

    /// <summary>
    /// Optional command hooks executed at fixed publish phases.
    /// </summary>
    public DotNetPublishCommandHook[] Hooks { get; set; } = Array.Empty<DotNetPublishCommandHook>();

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
    /// Optional bundle identifier to prepare from instead of the raw publish output.
    /// When set, the installer uses the matching bundle artefact for the selected combination.
    /// </summary>
    public string? PrepareFromBundleId { get; set; }

    /// <summary>
    /// Optional runtime filter for installer generation. When set, only matching publish combinations are used.
    /// </summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional framework filter for installer generation. When set, only matching publish combinations are used.
    /// </summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional style filter for installer generation. When set, only matching publish combinations are used.
    /// </summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

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
    /// Optional wildcard patterns excluded from generated WiX harvest output.
    /// Patterns match paths relative to the MSI staging root using forward slashes.
    /// </summary>
    public string[] HarvestExcludePatterns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional MSI versioning policy used by <c>msi.build</c>.
    /// </summary>
    public DotNetPublishMsiVersionOptions? Versioning { get; set; }

    /// <summary>
    /// Optional installer-specific MSBuild properties passed to <c>msi.build</c> as <c>/p:Name=Value</c>.
    /// Values here override matching entries from <see cref="DotNetPublishDotNetOptions.MsBuildProperties"/>.
    /// </summary>
    public Dictionary<string, string>? MsBuildProperties { get; set; }

    /// <summary>
    /// Optional named signing profile reference used when <see cref="Sign"/> is not set.
    /// </summary>
    public string? SignProfile { get; set; }

    /// <summary>
    /// Optional MSI signing options applied by <c>msi.sign</c>.
    /// Reuses the same policy contract as publish signing.
    /// </summary>
    public DotNetPublishSignOptions? Sign { get; set; }

    /// <summary>
    /// Optional partial overrides applied on top of <see cref="SignProfile"/>.
    /// Ignored when <see cref="Sign"/> is set.
    /// </summary>
    public DotNetPublishSignPatch? SignOverrides { get; set; }

    /// <summary>
    /// Optional client-license injection passed to MSI build as an MSBuild property.
    /// </summary>
    public DotNetPublishMsiClientLicenseOptions? ClientLicense { get; set; }
}

/// <summary>
/// Microsoft Store / MSIX packaging definition bound to a published target combination.
/// </summary>
public sealed class DotNetPublishStorePackage
{
    /// <summary>Store package identifier used in paths and step keys.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Source publish target name this Store package is associated with.</summary>
    public string PrepareFromTarget { get; set; } = string.Empty;

    /// <summary>
    /// Optional runtime filter for Store package generation. When set, only matching publish combinations are used.
    /// </summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional framework filter for Store package generation. When set, only matching publish combinations are used.
    /// </summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional style filter for Store package generation. When set, only matching publish combinations are used.
    /// </summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    /// <summary>
    /// Optional packaging project ID (resolved from <see cref="DotNetPublishSpec.Projects"/>).
    /// </summary>
    public string? PackagingProjectId { get; set; }

    /// <summary>
    /// Optional packaging project path (for example <c>*.wapproj</c>).
    /// When set, Store build uses this path directly instead of <see cref="PackagingProjectId"/>.
    /// </summary>
    public string? PackagingProjectPath { get; set; }

    /// <summary>
    /// Optional Store package output path template.
    /// Supports tokens: <c>{storePackage}</c>, <c>{target}</c>, <c>{rid}</c>, <c>{framework}</c>, <c>{style}</c>, <c>{configuration}</c>.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// When true, clears the Store package output directory before build. Default: true.
    /// </summary>
    public bool ClearOutput { get; set; } = true;

    /// <summary>
    /// Store packaging build mode. Default: <see cref="DotNetPublishStoreBuildMode.StoreUpload"/>.
    /// </summary>
    public DotNetPublishStoreBuildMode BuildMode { get; set; } = DotNetPublishStoreBuildMode.StoreUpload;

    /// <summary>
    /// Appx/MSIX bundle behavior. Default: <see cref="DotNetPublishStoreBundleMode.Auto"/>.
    /// </summary>
    public DotNetPublishStoreBundleMode Bundle { get; set; } = DotNetPublishStoreBundleMode.Auto;

    /// <summary>
    /// When true, generates an app installer file when the packaging project supports it.
    /// </summary>
    public bool GenerateAppInstaller { get; set; }

    /// <summary>
    /// Optional extra MSBuild properties applied only to the Store packaging project build.
    /// </summary>
    public Dictionary<string, string>? MsBuildProperties { get; set; }
}

/// <summary>
/// Bundle definition composed from one primary target plus optional included targets and post-copy scripts.
/// </summary>
public sealed class DotNetPublishBundle
{
    /// <summary>Bundle identifier used in paths and step keys.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Primary publish target name this bundle is composed from.</summary>
    public string PrepareFromTarget { get; set; } = string.Empty;

    /// <summary>
    /// Optional runtime filter for bundle generation. When set, only matching publish combinations are used.
    /// </summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional framework filter for bundle generation. When set, only matching publish combinations are used.
    /// </summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional style filter for bundle generation. When set, only matching publish combinations are used.
    /// </summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    /// <summary>
    /// Optional bundle output path template.
    /// Supports tokens: <c>{bundle}</c>, <c>{target}</c>, <c>{rid}</c>, <c>{framework}</c>, <c>{style}</c>, <c>{configuration}</c>.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// Optional subdirectory under the bundle output root where the primary publish target should be copied.
    /// When omitted, the primary target is copied into the bundle root.
    /// </summary>
    public string? PrimarySubdirectory { get; set; }

    /// <summary>
    /// When true, clears the bundle output directory before composing files. Default: true.
    /// </summary>
    public bool ClearOutput { get; set; } = true;

    /// <summary>
    /// When true, creates a zip file for the composed bundle.
    /// </summary>
    public bool Zip { get; set; }

    /// <summary>
    /// Optional zip output path. Supports the same tokens as <see cref="OutputPath"/>.
    /// </summary>
    public string? ZipPath { get; set; }

    /// <summary>
    /// Optional zip name template used when <see cref="ZipPath"/> is not provided.
    /// Supports tokens: <c>{bundle}</c>, <c>{target}</c>, <c>{rid}</c>, <c>{framework}</c>, <c>{style}</c>, <c>{configuration}</c>.
    /// </summary>
    public string? ZipNameTemplate { get; set; }

    /// <summary>
    /// Optional additional published targets copied into the bundle.
    /// </summary>
    public DotNetPublishBundleInclude[] Includes { get; set; } = Array.Empty<DotNetPublishBundleInclude>();

    /// <summary>
    /// Optional file or directory items copied into the bundle after publish-target includes.
    /// </summary>
    public DotNetPublishBundleCopyItem[] CopyItems { get; set; } = Array.Empty<DotNetPublishBundleCopyItem>();

    /// <summary>
    /// Optional built PowerShell module payloads copied into the bundle, usually under <c>Modules/{moduleName}</c>.
    /// </summary>
    public DotNetPublishBundleModuleInclude[] ModuleIncludes { get; set; } = Array.Empty<DotNetPublishBundleModuleInclude>();

    /// <summary>
    /// Optional scripts generated from templates into the bundle after copy operations.
    /// </summary>
    public DotNetPublishBundleGeneratedScript[] GeneratedScripts { get; set; } = Array.Empty<DotNetPublishBundleGeneratedScript>();

    /// <summary>
    /// Optional PowerShell scripts executed after the bundle contents are copied.
    /// </summary>
    public DotNetPublishBundleScript[] Scripts { get; set; } = Array.Empty<DotNetPublishBundleScript>();

    /// <summary>
    /// Optional built-in post-processing actions executed after bundle scripts and before zip creation.
    /// </summary>
    public DotNetPublishBundlePostProcessOptions? PostProcess { get; set; }
}

/// <summary>
/// Additional published target copied into a bundle.
/// </summary>
public sealed class DotNetPublishBundleInclude
{
    /// <summary>Target name to include in the bundle.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Optional subdirectory under the bundle output root where the included target should be copied.
    /// When omitted, the include is copied into the bundle root.
    /// </summary>
    public string? Subdirectory { get; set; }

    /// <summary>
    /// Optional framework override for the included target. When omitted, the bundle combination framework is used when possible.
    /// </summary>
    public string? Framework { get; set; }

    /// <summary>
    /// Optional runtime override for the included target. When omitted, the bundle combination runtime is used.
    /// </summary>
    public string? Runtime { get; set; }

    /// <summary>
    /// Optional style override for the included target. When omitted, the bundle combination style is used.
    /// </summary>
    public DotNetPublishStyle? Style { get; set; }

    /// <summary>
    /// When true, missing include artefacts fail the bundle step. Default: true.
    /// </summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// File or directory copied into a bundle from a path outside the publish target output.
/// </summary>
public sealed class DotNetPublishBundleCopyItem
{
    /// <summary>Source file or directory path. Relative paths resolve against project root and support bundle tokens.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Destination path under the bundle output root. Supports bundle tokens.</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>When true, missing sources fail the bundle step. Default: true.</summary>
    public bool Required { get; set; } = true;

    /// <summary>When true, clears an existing destination file/directory before copy. Default: true.</summary>
    public bool ClearDestination { get; set; } = true;
}

/// <summary>
/// Built PowerShell module payload copied into a bundle.
/// </summary>
public sealed class DotNetPublishBundleModuleInclude
{
    /// <summary>Logical module name. Used by default destination and template tokens.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Source module directory path, preferably a PowerForge module build artefact output.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Optional destination path under the bundle output root. Default: <c>Modules/{moduleName}</c>.</summary>
    public string? DestinationPath { get; set; }

    /// <summary>When true, missing sources fail the bundle step. Default: true.</summary>
    public bool Required { get; set; } = true;

    /// <summary>When true, clears an existing destination directory before copy. Default: true.</summary>
    public bool ClearDestination { get; set; } = true;
}

/// <summary>
/// Script generated from inline or file-based template content into a bundle.
/// </summary>
public sealed class DotNetPublishBundleGeneratedScript
{
    /// <summary>Optional template file path resolved relative to project root. Supports bundle tokens.</summary>
    public string? TemplatePath { get; set; }

    /// <summary>Optional inline template content. Used when <see cref="TemplatePath"/> is not set.</summary>
    public string? Template { get; set; }

    /// <summary>Output path under the bundle output root. Supports bundle tokens.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Template token values. Values support bundle tokens before template rendering.</summary>
    public Dictionary<string, string>? Tokens { get; set; }

    /// <summary>When true, replaces an existing output file. Default: true.</summary>
    public bool Overwrite { get; set; } = true;

    /// <summary>Optional named signing profile reference used when <see cref="Sign"/> is not set.</summary>
    public string? SignProfile { get; set; }

    /// <summary>Optional signing options for the generated script.</summary>
    public DotNetPublishSignOptions? Sign { get; set; }

    /// <summary>Optional partial overrides applied on top of <see cref="SignProfile"/>.</summary>
    public DotNetPublishSignPatch? SignOverrides { get; set; }
}

/// <summary>
/// PowerShell script executed as part of bundle composition.
/// </summary>
public sealed class DotNetPublishBundleScript
{
    /// <summary>Script path resolved relative to project root when not rooted.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Optional argument list passed to the script. Supports tokens: <c>{bundle}</c>, <c>{target}</c>, <c>{rid}</c>,
    /// <c>{framework}</c>, <c>{style}</c>, <c>{configuration}</c>, <c>{projectRoot}</c>, <c>{output}</c>,
    /// <c>{sourceOutput}</c>, <c>{zip}</c>.
    /// </summary>
    public string[] Arguments { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional working directory resolved relative to project root when not rooted. Defaults to project root.
    /// Supports the same tokens as <see cref="Arguments"/>.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Maximum script execution time in seconds. Default: 600.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// When true, prefer pwsh; otherwise allow Windows PowerShell first on Windows. Default: true.
    /// </summary>
    public bool PreferPwsh { get; set; } = true;

    /// <summary>
    /// When true, script failures fail the bundle step. Default: true.
    /// </summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// Built-in post-processing actions executed after bundle scripts and before zip creation.
/// </summary>
public sealed class DotNetPublishBundlePostProcessOptions
{
    /// <summary>
    /// Optional directory archive rules used to zip selected directories in-place.
    /// </summary>
    public DotNetPublishBundleArchiveRule[] ArchiveDirectories { get; set; } = Array.Empty<DotNetPublishBundleArchiveRule>();

    /// <summary>
    /// Optional wildcard patterns for files or directories to delete relative to bundle root.
    /// Supports <c>*</c>, <c>?</c>, and <c>**</c>.
    /// </summary>
    public string[] DeletePatterns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional wildcard patterns for files to sign relative to bundle root.
    /// When omitted and signing is enabled, bundle signing targets executables plus DLLs when <see cref="DotNetPublishSignOptions.IncludeDlls"/> is true.
    /// </summary>
    public string[] SignPatterns { get; set; } = Array.Empty<string>();

    /// <summary>Optional named signing profile reference used when <see cref="Sign"/> is not set.</summary>
    public string? SignProfile { get; set; }

    /// <summary>Optional signing options applied to bundle files matched by <see cref="SignPatterns"/>.</summary>
    public DotNetPublishSignOptions? Sign { get; set; }

    /// <summary>Optional partial overrides applied on top of <see cref="SignProfile"/>.</summary>
    public DotNetPublishSignPatch? SignOverrides { get; set; }

    /// <summary>
    /// Optional metadata manifest emitted into the bundle after post-processing.
    /// </summary>
    public DotNetPublishBundleMetadataOptions? Metadata { get; set; }
}

/// <summary>
/// Directory archive rule applied within a composed bundle.
/// </summary>
public sealed class DotNetPublishBundleArchiveRule
{
    /// <summary>
    /// Directory path relative to the bundle root.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Archive selection mode. Default: <see cref="DotNetPublishBundleArchiveMode.Self"/>.
    /// </summary>
    public DotNetPublishBundleArchiveMode Mode { get; set; } = DotNetPublishBundleArchiveMode.Self;

    /// <summary>
    /// Optional archive file name template. Supports <c>{name}</c>.
    /// Default: <c>{name}.zip</c>.
    /// </summary>
    public string? ArchiveNameTemplate { get; set; }

    /// <summary>
    /// When true, removes the source directory after the archive is created. Default: true.
    /// </summary>
    public bool DeleteSource { get; set; } = true;
}

/// <summary>
/// Optional metadata manifest written into a composed bundle.
/// </summary>
public sealed class DotNetPublishBundleMetadataOptions
{
    /// <summary>
    /// Output path relative to the bundle root.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// When true, includes standard bundle properties such as id, target, runtime, framework, style, and timestamps.
    /// Default: true.
    /// </summary>
    public bool IncludeStandardProperties { get; set; } = true;

    /// <summary>
    /// Optional additional metadata properties. Values support the same bundle tokens as bundle scripts plus <c>{createdUtc}</c>.
    /// </summary>
    public Dictionary<string, string>? Properties { get; set; }
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
/// Command hook executed at a fixed phase of the dotnet publish pipeline.
/// </summary>
public sealed class DotNetPublishCommandHook
{
    /// <summary>Default command hook timeout in seconds.</summary>
    public const int DefaultTimeoutSeconds = 600;

    /// <summary>Stable hook identifier used in plan step keys.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Pipeline phase where the hook runs.</summary>
    public DotNetPublishCommandHookPhase Phase { get; set; }

    /// <summary>Executable or script command. Relative paths resolve against project root and support hook tokens.</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Command arguments. Values support hook tokens.</summary>
    public string[] Arguments { get; set; } = Array.Empty<string>();

    /// <summary>Optional working directory. Relative paths resolve against project root and support hook tokens.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Optional environment variables. Values support hook tokens.</summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>Maximum command execution time in seconds. Default: 600.</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>When true, a non-zero exit code fails the publish run. Default: true.</summary>
    public bool Required { get; set; } = true;

    /// <summary>Optional target-name filter for target/bundle phases.</summary>
    public string[] Targets { get; set; } = Array.Empty<string>();

    /// <summary>Optional runtime filter for target/bundle phases.</summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>Optional framework filter for target/bundle phases.</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>Optional publish-style filter for target/bundle phases.</summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();
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
