namespace PowerForge;

/// <summary>
/// Host-facing project publish configuration resolved from <c>project.build.json</c>.
/// </summary>
public sealed class ProjectBuildPublishHostConfiguration
{
    /// <summary>Resolved configuration path.</summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Whether NuGet publishing is enabled.</summary>
    public bool PublishNuget { get; set; }

    /// <summary>Whether GitHub publishing is enabled.</summary>
    public bool PublishGitHub { get; set; }

    /// <summary>Resolved NuGet publish source.</summary>
    public string PublishSource { get; set; } = "https://api.nuget.org/v3/index.json";

    /// <summary>Resolved NuGet API key.</summary>
    public string? PublishApiKey { get; set; }

    /// <summary>Resolved GitHub token.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>Configured GitHub owner.</summary>
    public string? GitHubUsername { get; set; }

    /// <summary>Configured GitHub repository name.</summary>
    public string? GitHubRepositoryName { get; set; }

    /// <summary>Whether the GitHub release should be marked as a prerelease.</summary>
    public bool GitHubIsPreRelease { get; set; }

    /// <summary>Whether default tags should include the project name.</summary>
    public bool GitHubIncludeProjectNameInTag { get; set; } = true;

    /// <summary>Whether GitHub should generate release notes.</summary>
    public bool GitHubGenerateReleaseNotes { get; set; }

    /// <summary>Configured GitHub release name override or template.</summary>
    public string? GitHubReleaseName { get; set; }

    /// <summary>Configured GitHub tag override.</summary>
    public string? GitHubTagName { get; set; }

    /// <summary>Configured GitHub tag template.</summary>
    public string? GitHubTagTemplate { get; set; }

    /// <summary>Configured GitHub release mode.</summary>
    public string GitHubReleaseMode { get; set; } = "Single";

    /// <summary>Configured GitHub primary project.</summary>
    public string? GitHubPrimaryProject { get; set; }

    /// <summary>Configured GitHub tag conflict policy.</summary>
    public string? GitHubTagConflictPolicy { get; set; }
}
