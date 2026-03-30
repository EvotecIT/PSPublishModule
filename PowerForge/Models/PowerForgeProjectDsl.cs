namespace PowerForge;

/// <summary>
/// PowerShell-first project/release configuration that maps to the unified PowerForge release engine.
/// </summary>
public sealed class ConfigurationProject
{
    /// <summary>
    /// Friendly project/release name used for authoring and diagnostics.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional project root used for resolving relative paths when invoking from PowerShell objects.
    /// </summary>
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Release-level defaults.
    /// </summary>
    public ConfigurationProjectRelease Release { get; set; } = new();

    /// <summary>
    /// Optional workspace validation and feature-toggle defaults.
    /// </summary>
    public ConfigurationProjectWorkspace? Workspace { get; set; }

    /// <summary>
    /// Optional shared signing defaults applied to generated publish and installer entries.
    /// </summary>
    public ConfigurationProjectSigning? Signing { get; set; }

    /// <summary>
    /// Optional output-root and staging defaults.
    /// </summary>
    public ConfigurationProjectOutput? Output { get; set; }

    /// <summary>
    /// Publish targets to build.
    /// </summary>
    public ConfigurationProjectTarget[] Targets { get; set; } = Array.Empty<ConfigurationProjectTarget>();

    /// <summary>
    /// Optional installer definitions bound to the configured targets.
    /// </summary>
    public ConfigurationProjectInstaller[] Installers { get; set; } = Array.Empty<ConfigurationProjectInstaller>();
}

/// <summary>
/// Release-level defaults for a PowerShell-authored project build.
/// </summary>
public sealed class ConfigurationProjectRelease
{
    /// <summary>
    /// Build configuration used by the release workflow.
    /// </summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// When true, enables tool/app GitHub release publishing by default for this project object.
    /// </summary>
    public bool PublishToolGitHub { get; set; }

    /// <summary>
    /// When true, skips restore operations for DotNetPublish-backed tool/app flows.
    /// </summary>
    public bool SkipRestore { get; set; }

    /// <summary>
    /// When true, skips build operations for DotNetPublish-backed tool/app flows.
    /// </summary>
    public bool SkipBuild { get; set; }

    /// <summary>
    /// Optional release-level output selection defaults.
    /// </summary>
    public ConfigurationProjectReleaseOutputType[] ToolOutput { get; set; } = Array.Empty<ConfigurationProjectReleaseOutputType>();

    /// <summary>
    /// Optional release-level output exclusion defaults.
    /// </summary>
    public ConfigurationProjectReleaseOutputType[] SkipToolOutput { get; set; } = Array.Empty<ConfigurationProjectReleaseOutputType>();
}

/// <summary>
/// Workspace validation and feature-toggle settings for a PowerShell-authored project build.
/// </summary>
public sealed class ConfigurationProjectWorkspace
{
    /// <summary>
    /// Optional workspace validation config path.
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Optional workspace profile name.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// Optional workspace features to enable.
    /// </summary>
    public string[] EnableFeatures { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional workspace features to disable.
    /// </summary>
    public string[] DisableFeatures { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When true, keeps workspace validation configured but disabled by default for this invocation object.
    /// </summary>
    public bool SkipValidation { get; set; }
}

/// <summary>
/// Shared signing defaults for PowerShell-authored project builds.
/// </summary>
public sealed class ConfigurationProjectSigning
{
    /// <summary>
    /// Signing activation mode.
    /// </summary>
    public ConfigurationProjectSigningMode Mode { get; set; } = ConfigurationProjectSigningMode.OnDemand;

    /// <summary>
    /// Optional path to the signing tool.
    /// </summary>
    public string? ToolPath { get; set; }

    /// <summary>
    /// Optional certificate thumbprint.
    /// </summary>
    public string? Thumbprint { get; set; }

    /// <summary>
    /// Optional certificate subject name.
    /// </summary>
    public string? SubjectName { get; set; }

    /// <summary>
    /// Policy when the signing tool is missing.
    /// </summary>
    public DotNetPublishPolicyMode OnMissingTool { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Policy when signing a file fails.
    /// </summary>
    public DotNetPublishPolicyMode OnFailure { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Optional RFC3161 timestamp URL.
    /// </summary>
    public string? TimestampUrl { get; set; } = "http://timestamp.digicert.com";

    /// <summary>
    /// Optional signature description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional signature URL.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Optional CSP name.
    /// </summary>
    public string? Csp { get; set; }

    /// <summary>
    /// Optional key container name.
    /// </summary>
    public string? KeyContainer { get; set; }
}

/// <summary>
/// Output-root and staging defaults for a PowerShell-authored project build.
/// </summary>
public sealed class ConfigurationProjectOutput
{
    /// <summary>
    /// Optional DotNetPublish output-root override.
    /// </summary>
    public string? OutputRoot { get; set; }

    /// <summary>
    /// Optional unified release staging root.
    /// </summary>
    public string? StageRoot { get; set; }

    /// <summary>
    /// Optional unified release manifest output path.
    /// </summary>
    public string? ManifestJsonPath { get; set; }

    /// <summary>
    /// Optional unified release checksums output path.
    /// </summary>
    public string? ChecksumsPath { get; set; }

    /// <summary>
    /// When true, keeps top-level release checksum generation enabled.
    /// </summary>
    public bool IncludeChecksums { get; set; } = true;
}

/// <summary>
/// High-level publish target entry used by the PowerShell project-build DSL.
/// </summary>
public sealed class ConfigurationProjectTarget
{
    /// <summary>
    /// Friendly target name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Path to the project file to publish.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional target kind metadata.
    /// </summary>
    public DotNetPublishTargetKind Kind { get; set; } = DotNetPublishTargetKind.Unknown;

    /// <summary>
    /// Primary framework used when <see cref="Frameworks"/> is not set.
    /// </summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>
    /// Optional framework matrix values.
    /// </summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional runtime matrix values.
    /// </summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Primary publish style used when <see cref="Styles"/> is not set.
    /// </summary>
    public DotNetPublishStyle Style { get; set; } = DotNetPublishStyle.PortableCompat;

    /// <summary>
    /// Optional style matrix values.
    /// </summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    /// <summary>
    /// Optional output path template for the raw publish output.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// Optional requested output categories for this target.
    /// </summary>
    public ConfigurationProjectTargetOutputType[] OutputType { get; set; } = new[] { ConfigurationProjectTargetOutputType.Tool };

    /// <summary>
    /// When true, the raw publish output is also zipped.
    /// </summary>
    public bool Zip { get; set; }

    /// <summary>
    /// When true, publish uses a temporary staging directory before final copy.
    /// </summary>
    public bool UseStaging { get; set; } = true;

    /// <summary>
    /// When true, clears the final output directory before copy.
    /// </summary>
    public bool ClearOutput { get; set; } = true;

    /// <summary>
    /// When true, keeps symbol files.
    /// </summary>
    public bool KeepSymbols { get; set; }

    /// <summary>
    /// When true, keeps documentation files.
    /// </summary>
    public bool KeepDocs { get; set; }

    /// <summary>
    /// Optional ReadyToRun override.
    /// </summary>
    public bool? ReadyToRun { get; set; }
}

/// <summary>
/// Installer definition used by the PowerShell project-build DSL.
/// </summary>
public sealed class ConfigurationProjectInstaller
{
    /// <summary>
    /// Installer identifier used in output paths and pipeline step keys.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Name of the source target this installer builds from.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Path to the installer project file (for example a wixproj).
    /// </summary>
    public string InstallerProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// When true, prepares the installer from an auto-generated portable bundle instead of the raw publish output.
    /// </summary>
    public bool PrepareFromPortableBundle { get; set; } = true;

    /// <summary>
    /// Harvest mode used during MSI prepare.
    /// </summary>
    public DotNetPublishMsiHarvestMode Harvest { get; set; } = DotNetPublishMsiHarvestMode.Auto;

    /// <summary>
    /// Optional runtime filter for this installer.
    /// </summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional framework filter for this installer.
    /// </summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional style filter for this installer.
    /// </summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    /// <summary>
    /// Optional WiX DirectoryRef identifier for generated harvest fragments.
    /// </summary>
    public string? HarvestDirectoryRefId { get; set; }

    /// <summary>
    /// Optional installer-specific MSBuild properties.
    /// </summary>
    public Dictionary<string, string>? MsBuildProperties { get; set; }
}

/// <summary>
/// Signing activation modes for PowerShell-authored project builds.
/// </summary>
public enum ConfigurationProjectSigningMode
{
    /// <summary>
    /// Do not preconfigure signing in the generated release spec.
    /// </summary>
    Disabled,

    /// <summary>
    /// Configure signing metadata but keep signing disabled until the caller requests it.
    /// </summary>
    OnDemand,

    /// <summary>
    /// Enable signing in the generated release spec by default.
    /// </summary>
    Enabled
}

/// <summary>
/// High-level output categories supported by the PowerShell project-build DSL target model.
/// </summary>
public enum ConfigurationProjectTargetOutputType
{
    /// <summary>
    /// Keep the raw publish output as a requested release artefact.
    /// </summary>
    Tool,

    /// <summary>
    /// Produce a portable bundle from the raw publish output.
    /// </summary>
    Portable
}

/// <summary>
/// High-level release output categories supported by the PowerShell project-build DSL release model.
/// </summary>
public enum ConfigurationProjectReleaseOutputType
{
    /// <summary>
    /// Keep the raw publish output as a requested release artefact.
    /// </summary>
    Tool,

    /// <summary>
    /// Produce a portable bundle from the raw publish output.
    /// </summary>
    Portable,

    /// <summary>
    /// Produce an installer artefact.
    /// </summary>
    Installer,

    /// <summary>
    /// Produce a store package artefact.
    /// </summary>
    Store
}
