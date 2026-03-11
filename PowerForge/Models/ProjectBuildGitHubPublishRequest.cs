namespace PowerForge;

/// <summary>
/// Input used by <see cref="ProjectBuildGitHubPublisher"/> for GitHub release publishing.
/// </summary>
public sealed class ProjectBuildGitHubPublishRequest
{
    /// <summary>Repository owner.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Repository name.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>GitHub access token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Release workflow result to publish.</summary>
    public DotNetRepositoryReleaseResult Release { get; set; } = new();

    /// <summary>Release mode (Single or PerProject).</summary>
    public string ReleaseMode { get; set; } = "Single";

    /// <summary>Whether to include the project name in the default tag.</summary>
    public bool IncludeProjectNameInTag { get; set; } = true;

    /// <summary>Whether the published GitHub release is a prerelease.</summary>
    public bool IsPreRelease { get; set; }

    /// <summary>Whether GitHub should auto-generate release notes.</summary>
    public bool GenerateReleaseNotes { get; set; }

    /// <summary>Whether publishing failures should stop on first error.</summary>
    public bool PublishFailFast { get; set; } = true;

    /// <summary>Explicit release name override or template.</summary>
    public string? ReleaseName { get; set; }

    /// <summary>Explicit tag name override.</summary>
    public string? TagName { get; set; }

    /// <summary>Tag template used when computing tag names.</summary>
    public string? TagTemplate { get; set; }

    /// <summary>Primary project used for single-release version resolution.</summary>
    public string? PrimaryProject { get; set; }

    /// <summary>Conflict policy when a tag already exists.</summary>
    public string? TagConflictPolicy { get; set; }
}
