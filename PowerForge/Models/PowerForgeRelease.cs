using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Unified repository release configuration that can drive package and tool outputs from one JSON file.
/// </summary>
internal sealed class PowerForgeReleaseSpec
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public int SchemaVersion { get; set; } = 1;

    public ProjectBuildConfiguration? Packages { get; set; }

    public PowerForgeToolReleaseSpec? Tools { get; set; }

    public PowerForgeWorkspaceValidationOptions? WorkspaceValidation { get; set; }

    public PowerForgeReleaseOutputsOptions Outputs { get; set; } = new();
}

/// <summary>
/// Host-facing request for executing a unified release workflow.
/// </summary>
internal sealed class PowerForgeReleaseRequest
{
    public string ConfigPath { get; set; } = string.Empty;

    public bool PlanOnly { get; set; }

    public bool ValidateOnly { get; set; }

    public bool PackagesOnly { get; set; }

    public bool ToolsOnly { get; set; }

    public bool? PublishNuget { get; set; }

    public bool? PublishProjectGitHub { get; set; }

    public bool? PublishToolGitHub { get; set; }

    public string? Configuration { get; set; }

    public bool SkipRestore { get; set; }

    public bool SkipBuild { get; set; }

    public bool SkipWorkspaceValidation { get; set; }

    public string? WorkspaceConfigPath { get; set; }

    public string? WorkspaceProfile { get; set; }

    public string? WorkspaceTestimoXRoot { get; set; }

    public string[] WorkspaceEnableFeatures { get; set; } = Array.Empty<string>();

    public string[] WorkspaceDisableFeatures { get; set; } = Array.Empty<string>();

    public string? OutputRoot { get; set; }

    public string? StageRoot { get; set; }

    public string? ManifestJsonPath { get; set; }

    public string? ChecksumsPath { get; set; }

    public bool SkipReleaseChecksums { get; set; }

    public bool? KeepSymbols { get; set; }

    public bool? EnableSigning { get; set; }

    public string? SignProfile { get; set; }

    public string? SignToolPath { get; set; }

    public string? SignThumbprint { get; set; }

    public string? SignSubjectName { get; set; }

    public DotNetPublishPolicyMode? SignOnMissingTool { get; set; }

    public DotNetPublishPolicyMode? SignOnFailure { get; set; }

    public string? SignTimestampUrl { get; set; }

    public string? SignDescription { get; set; }

    public string? SignUrl { get; set; }

    public string? SignCsp { get; set; }

    public string? SignKeyContainer { get; set; }

    public string? PackageSignThumbprint { get; set; }

    public string? PackageSignStore { get; set; }

    public string? PackageSignTimestampUrl { get; set; }

    public string[] Targets { get; set; } = Array.Empty<string>();

    public string[] Runtimes { get; set; } = Array.Empty<string>();

    public string[] Frameworks { get; set; } = Array.Empty<string>();

    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    public PowerForgeToolReleaseFlavor[] Flavors { get; set; } = Array.Empty<PowerForgeToolReleaseFlavor>();
}

/// <summary>
/// Aggregate result for a unified release run.
/// </summary>
internal sealed class PowerForgeReleaseResult
{
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public string ConfigPath { get; set; } = string.Empty;

    public ProjectBuildHostExecutionResult? Packages { get; set; }

    public PowerForgeToolReleasePlan? ToolPlan { get; set; }

    public PowerForgeToolReleaseResult? Tools { get; set; }

    public DotNetPublishPlan? DotNetToolPlan { get; set; }

    public DotNetPublishResult? DotNetTools { get; set; }

    public WorkspaceValidationPlan? WorkspaceValidationPlan { get; set; }

    public WorkspaceValidationResult? WorkspaceValidation { get; set; }

    public PowerForgeToolGitHubReleaseResult[] ToolGitHubReleases { get; set; } = Array.Empty<PowerForgeToolGitHubReleaseResult>();

    public string[] ReleaseAssets { get; set; } = Array.Empty<string>();

    public PowerForgeReleaseAssetEntry[] ReleaseAssetEntries { get; set; } = Array.Empty<PowerForgeReleaseAssetEntry>();

    public string? ReleaseManifestPath { get; set; }

    public string? ReleaseChecksumsPath { get; set; }
}

/// <summary>
/// Optional output settings for unified release aggregation.
/// </summary>
internal sealed class PowerForgeReleaseOutputsOptions
{
    public string? ManifestJsonPath { get; set; }

    public string? ChecksumsPath { get; set; }

    public PowerForgeReleaseStagingOptions? Staging { get; set; }
}

internal sealed class PowerForgeReleaseStagingOptions
{
    public string? RootPath { get; set; }

    public string PackagesPath { get; set; } = "nuget";

    public string PortablePath { get; set; } = "portable";

    public string InstallerPath { get; set; } = "installer";

    public string StorePath { get; set; } = "store";

    public string ToolsPath { get; set; } = "tools";

    public string MetadataPath { get; set; } = "metadata";

    public string OtherPath { get; set; } = "assets";
}

internal enum PowerForgeReleaseAssetCategory
{
    Package,
    Portable,
    Installer,
    Store,
    Tool,
    Metadata,
    Other
}

internal sealed class PowerForgeReleaseAssetEntry
{
    public string Path { get; set; } = string.Empty;

    public PowerForgeReleaseAssetCategory Category { get; set; }

    public string? Source { get; set; }

    public string? Target { get; set; }

    public string? RelativeStagePath { get; set; }

    public string? StagedPath { get; set; }
}

internal sealed class PowerForgeWorkspaceValidationOptions
{
    public string? ConfigPath { get; set; }

    public string? Profile { get; set; }

    public string[] EnableFeatures { get; set; } = Array.Empty<string>();

    public string[] DisableFeatures { get; set; } = Array.Empty<string>();
}

/// <summary>
/// GitHub publishing result for one tool release group.
/// </summary>
internal sealed class PowerForgeToolGitHubReleaseResult
{
    public string Target { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;

    public string ReleaseName { get; set; } = string.Empty;

    public string[] AssetPaths { get; set; } = Array.Empty<string>();

    public bool Success { get; set; }

    public string? ReleaseUrl { get; set; }

    public bool ReusedExistingRelease { get; set; }

    public string? ErrorMessage { get; set; }

    public string[] SkippedExistingAssets { get; set; } = Array.Empty<string>();
}
