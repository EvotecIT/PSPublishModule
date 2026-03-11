using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Aggregate result for project builds.
/// </summary>
public sealed class ProjectBuildResult
{
    /// <summary>True when the pipeline completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Error message when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Release pipeline result.</summary>
    public DotNetRepositoryReleaseResult? Release { get; set; }

    /// <summary>GitHub publishing results.</summary>
    public List<ProjectBuildGitHubResult> GitHub { get; } = new();
}
