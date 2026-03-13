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

    public string[] Targets { get; set; } = Array.Empty<string>();

    public string[] Runtimes { get; set; } = Array.Empty<string>();

    public string[] Frameworks { get; set; } = Array.Empty<string>();

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

    public PowerForgeToolGitHubReleaseResult[] ToolGitHubReleases { get; set; } = Array.Empty<PowerForgeToolGitHubReleaseResult>();
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
