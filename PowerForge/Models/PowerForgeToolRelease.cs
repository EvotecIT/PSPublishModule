using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Downloadable tool release configuration used for runtime-specific executables.
/// </summary>
internal sealed class PowerForgeToolReleaseSpec
{
    public string? ProjectRoot { get; set; }

    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// Optional path to an external DotNet publish config. When set, unified release uses the
    /// richer DotNet publish workflow instead of the legacy simple tool publisher.
    /// </summary>
    public string? DotNetPublishConfigPath { get; set; }

    /// <summary>
    /// Optional inline DotNet publish spec. When set, unified release uses the richer DotNet
    /// publish workflow instead of the legacy simple tool publisher.
    /// </summary>
    public DotNetPublishSpec? DotNetPublish { get; set; }

    /// <summary>
    /// Optional DotNet publish profile override applied to either <see cref="DotNetPublishConfigPath"/>
    /// or <see cref="DotNetPublish"/>.
    /// </summary>
    public string? DotNetPublishProfile { get; set; }

    public PowerForgeToolReleaseTarget[] Targets { get; set; } = Array.Empty<PowerForgeToolReleaseTarget>();

    public PowerForgeToolReleaseGitHubOptions GitHub { get; set; } = new();
}

/// <summary>
/// One named tool target to publish as downloadable executables.
/// </summary>
internal sealed class PowerForgeToolReleaseTarget
{
    public string Name { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;

    public string OutputName { get; set; } = string.Empty;

    public string? CommandAlias { get; set; }

    public string[] Runtimes { get; set; } = Array.Empty<string>();

    public string[] Frameworks { get; set; } = Array.Empty<string>();

    public PowerForgeToolReleaseFlavor Flavor { get; set; } = PowerForgeToolReleaseFlavor.SingleContained;

    public PowerForgeToolReleaseFlavor[] Flavors { get; set; } = Array.Empty<PowerForgeToolReleaseFlavor>();

    public string? ArtifactRootPath { get; set; }

    public string? OutputPath { get; set; }

    public bool UseStaging { get; set; } = true;

    public bool ClearOutput { get; set; } = true;

    public bool Zip { get; set; } = true;

    public string? ZipPath { get; set; }

    public string? ZipNameTemplate { get; set; }

    public bool KeepSymbols { get; set; }

    public bool KeepDocs { get; set; }

    public bool CreateCommandAliasOnUnix { get; set; } = true;

    public Dictionary<string, string>? MsBuildProperties { get; set; }
}

/// <summary>
/// GitHub release settings for tool artefacts.
/// </summary>
internal sealed class PowerForgeToolReleaseGitHubOptions
{
    public bool Publish { get; set; }

    public string? Owner { get; set; }

    public string? Repository { get; set; }

    public string? Token { get; set; }

    public string? TokenFilePath { get; set; }

    public string? TokenEnvName { get; set; }

    public bool GenerateReleaseNotes { get; set; } = true;

    public bool IsPreRelease { get; set; }

    public string? TagTemplate { get; set; }

    public string? ReleaseNameTemplate { get; set; }
}

/// <summary>
/// Supported publish flavors for downloadable tool binaries.
/// </summary>
internal enum PowerForgeToolReleaseFlavor
{
    SingleContained,
    SingleFx,
    Portable,
    Fx
}

/// <summary>
/// Planned tool release execution.
/// </summary>
internal sealed class PowerForgeToolReleasePlan
{
    public string ProjectRoot { get; set; } = string.Empty;

    public string Configuration { get; set; } = "Release";

    public PowerForgeToolReleaseTargetPlan[] Targets { get; set; } = Array.Empty<PowerForgeToolReleaseTargetPlan>();
}

/// <summary>
/// Planned target entry.
/// </summary>
internal sealed class PowerForgeToolReleaseTargetPlan
{
    public string Name { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;

    public string OutputName { get; set; } = string.Empty;

    public string? CommandAlias { get; set; }

    public string Version { get; set; } = string.Empty;

    public string ArtifactRootPath { get; set; } = string.Empty;

    public bool UseStaging { get; set; }

    public bool ClearOutput { get; set; }

    public bool Zip { get; set; }

    public bool KeepSymbols { get; set; }

    public bool KeepDocs { get; set; }

    public bool CreateCommandAliasOnUnix { get; set; }

    public Dictionary<string, string> MsBuildProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public PowerForgeToolReleaseCombinationPlan[] Combinations { get; set; } = Array.Empty<PowerForgeToolReleaseCombinationPlan>();
}

/// <summary>
/// Planned target/runtime/framework/flavor combination.
/// </summary>
internal sealed class PowerForgeToolReleaseCombinationPlan
{
    public string Runtime { get; set; } = string.Empty;

    public string Framework { get; set; } = string.Empty;

    public PowerForgeToolReleaseFlavor Flavor { get; set; }

    public string OutputPath { get; set; } = string.Empty;

    public string? ZipPath { get; set; }
}

/// <summary>
/// Result of executing tool releases.
/// </summary>
internal sealed class PowerForgeToolReleaseResult
{
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public PowerForgeToolReleaseArtifactResult[] Artefacts { get; set; } = Array.Empty<PowerForgeToolReleaseArtifactResult>();

    public string[] ManifestPaths { get; set; } = Array.Empty<string>();
}

/// <summary>
/// One produced downloadable tool artefact.
/// </summary>
internal sealed class PowerForgeToolReleaseArtifactResult
{
    public string Target { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string OutputName { get; set; } = string.Empty;

    public string Runtime { get; set; } = string.Empty;

    public string Framework { get; set; } = string.Empty;

    public PowerForgeToolReleaseFlavor Flavor { get; set; }

    public string OutputPath { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public string? CommandAliasPath { get; set; }

    public string? ZipPath { get; set; }

    public int Files { get; set; }

    public long TotalBytes { get; set; }
}

/// <summary>
/// Per-target manifest written after tool builds complete.
/// </summary>
internal sealed class PowerForgeToolReleaseManifest
{
    public string Target { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string OutputName { get; set; } = string.Empty;

    public PowerForgeToolReleaseArtifactResult[] Artefacts { get; set; } = Array.Empty<PowerForgeToolReleaseArtifactResult>();
}
