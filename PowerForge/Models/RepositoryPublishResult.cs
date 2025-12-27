namespace PowerForge;

/// <summary>
/// Result of publishing a module folder or nupkg to a PowerShell repository.
/// </summary>
public sealed class RepositoryPublishResult
{
    /// <summary>Full path that was published.</summary>
    public string Path { get; }

    /// <summary>When true, indicates <see cref="Path"/> was a nupkg.</summary>
    public bool IsNupkg { get; }

    /// <summary>Repository name used for publishing.</summary>
    public string RepositoryName { get; }

    /// <summary>Publishing tool/provider that was used.</summary>
    public PublishTool Tool { get; }

    /// <summary>Whether the repository was created by this run.</summary>
    public bool RepositoryCreated { get; }

    /// <summary>Whether the repository was unregistered at the end of the run.</summary>
    public bool RepositoryUnregistered { get; }

    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public RepositoryPublishResult(
        string path,
        bool isNupkg,
        string repositoryName,
        PublishTool tool,
        bool repositoryCreated,
        bool repositoryUnregistered)
    {
        Path = path;
        IsNupkg = isNupkg;
        RepositoryName = repositoryName;
        Tool = tool;
        RepositoryCreated = repositoryCreated;
        RepositoryUnregistered = repositoryUnregistered;
    }
}

