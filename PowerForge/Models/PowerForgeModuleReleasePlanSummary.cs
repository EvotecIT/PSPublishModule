using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class PowerForgeModuleReleasePlanSummary
{
    public string? ModuleName { get; set; }

    public string RepositoryRoot { get; set; } = string.Empty;

    public string? ConfigPath { get; set; }

    public string? ScriptPath { get; set; }

    public string ModulePath { get; set; } = string.Empty;

    public string? ManifestPath { get; set; }

    public string? Configuration { get; set; }

    public string? Framework { get; set; }

    public ConfigurationGateMode RunMode { get; set; } = ConfigurationGateMode.Build;

    public bool IncludesPackages { get; set; }

    public bool IncludesProjectPackages { get; set; }

    public int TimeoutSeconds { get; set; }

    public bool NoDotnetBuild { get; set; }

    public string? ModuleVersion { get; set; }

    public string? PreReleaseTag { get; set; }

    public string? StagingPath { get; set; }

    [JsonIgnore]
    public string[] PackedModuleRoots { get; set; } = Array.Empty<string>();

    public bool NoSign { get; set; }

    public bool SkipInstall { get; set; }

    public bool SignModule { get; set; }

    public bool PowerForgeReleaseStage { get; set; }

    public bool UnifiedGitHubRelease { get; set; }

    public string[] ArtifactPaths { get; set; } = Array.Empty<string>();
}
