namespace PowerForge;

/// <summary>
/// Result of publishing a module build to an external destination (repository or GitHub release).
/// </summary>
public sealed class ModulePublishResult
{
    /// <summary>Publish destination type.</summary>
    public PublishDestination Destination { get; }

    /// <summary>Optional repository name (PowerShell repo name or GitHub repository name).</summary>
    public string? RepositoryName { get; }

    /// <summary>Optional GitHub username/owner.</summary>
    public string? UserName { get; }

    /// <summary>Optional tag name used for GitHub releases.</summary>
    public string? TagName { get; }

    /// <summary>Published module version including prerelease suffix when applicable.</summary>
    public string VersionText { get; }

    /// <summary>True when the GitHub release was marked as prerelease.</summary>
    public bool IsPreRelease { get; }

    /// <summary>Asset paths published to GitHub (when applicable).</summary>
    public string[] AssetPaths { get; }

    /// <summary>Release URL when publishing to GitHub (when available).</summary>
    public string? ReleaseUrl { get; }

    /// <summary>True when publishing succeeded.</summary>
    public bool Succeeded { get; }

    /// <summary>Optional error message when <see cref="Succeeded"/> is false.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Repository publishing engine used for PowerShell repository destinations.</summary>
    public PublishTool? Tool { get; }

    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public ModulePublishResult(
        PublishDestination destination,
        string? repositoryName,
        string? userName,
        string? tagName,
        string versionText,
        bool isPreRelease,
        string[] assetPaths,
        string? releaseUrl,
        bool succeeded,
        string? errorMessage)
        : this(
            destination,
            repositoryName,
            userName,
            tagName,
            versionText,
            isPreRelease,
            assetPaths,
            releaseUrl,
            succeeded,
            errorMessage,
            tool: null)
    {
    }

    /// <summary>
    /// Creates a new result instance and records the repository publishing engine.
    /// </summary>
    public ModulePublishResult(
        PublishDestination destination,
        string? repositoryName,
        string? userName,
        string? tagName,
        string versionText,
        bool isPreRelease,
        string[] assetPaths,
        string? releaseUrl,
        bool succeeded,
        string? errorMessage,
        PublishTool? tool)
    {
        Destination = destination;
        RepositoryName = repositoryName;
        UserName = userName;
        TagName = tagName;
        VersionText = versionText ?? string.Empty;
        IsPreRelease = isPreRelease;
        AssetPaths = assetPaths ?? Array.Empty<string>();
        ReleaseUrl = releaseUrl;
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
        Tool = tool;
    }
}
