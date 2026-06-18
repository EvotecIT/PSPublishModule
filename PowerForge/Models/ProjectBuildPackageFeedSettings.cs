namespace PowerForge;

/// <summary>
/// Resolved NuGet feed settings for project-build version lookup and package publishing.
/// </summary>
internal sealed class ProjectBuildPackageFeedSettings
{
    /// <summary>NuGet sources used for version lookup.</summary>
    public string[]? VersionSources { get; set; }

    /// <summary>Credential used for authenticated version lookup sources.</summary>
    public RepositoryCredential? VersionSourceCredential { get; set; }

    /// <summary>Credentials scoped to individual version lookup sources.</summary>
    public Dictionary<string, RepositoryCredential>? VersionSourceCredentials { get; set; }

    /// <summary>NuGet source used by <c>dotnet nuget push</c>.</summary>
    public string? PublishSource { get; set; }

    /// <summary>API key used by <c>dotnet nuget push</c>.</summary>
    public string? PublishApiKey { get; set; }

    /// <summary>GitHub token resolved from the project-build GitHub token settings.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>GitHub Packages owner when the shared feed mode is active.</summary>
    public string? GitHubPackagesOwner { get; set; }
}
