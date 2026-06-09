using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Summary returned by <see cref="ProjectBuildGitHubPublisher"/>.
/// </summary>
public sealed class ProjectBuildGitHubPublishSummary
{
    /// <summary>Whether the publish workflow succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Top-level error message when the workflow failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Whether publishing ran in per-project mode.</summary>
    public bool PerProject { get; set; }

    /// <summary>Tag name used for single-release mode.</summary>
    public string? SummaryTag { get; set; }

    /// <summary>Release URL used for single-release mode.</summary>
    public string? SummaryReleaseUrl { get; set; }

    /// <summary>Number of assets published in single-release mode.</summary>
    public int SummaryAssetsCount { get; set; }

    /// <summary>Per-project publish results.</summary>
    public List<ProjectBuildGitHubResult> Results { get; } = new();
}
