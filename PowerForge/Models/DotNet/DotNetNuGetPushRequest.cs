namespace PowerForge;

/// <summary>
/// Request to execute <c>dotnet nuget push</c>.
/// </summary>
public sealed class DotNetNuGetPushRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DotNetNuGetPushRequest"/> class.
    /// </summary>
    /// <param name="packagePath">Package path to push.</param>
    /// <param name="apiKey">API key passed to the feed.</param>
    /// <param name="source">Feed source URL or name.</param>
    /// <param name="skipDuplicate">When true, passes <c>--skip-duplicate</c>.</param>
    /// <param name="workingDirectory">Optional working directory override.</param>
    /// <param name="timeout">Optional timeout override.</param>
    public DotNetNuGetPushRequest(
        string packagePath,
        string apiKey,
        string source,
        bool skipDuplicate = true,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        PackagePath = packagePath;
        ApiKey = apiKey;
        Source = source;
        SkipDuplicate = skipDuplicate;
        WorkingDirectory = workingDirectory;
        Timeout = timeout;
    }

    /// <summary>
    /// Gets the package path to push.
    /// </summary>
    public string PackagePath { get; }

    /// <summary>
    /// Gets the API key passed to the feed.
    /// </summary>
    public string ApiKey { get; }

    /// <summary>
    /// Gets the feed source URL or name.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets a value indicating whether <c>--skip-duplicate</c> should be passed.
    /// </summary>
    public bool SkipDuplicate { get; }

    /// <summary>
    /// Gets the optional working directory override.
    /// </summary>
    public string? WorkingDirectory { get; }

    /// <summary>
    /// Gets the optional timeout override.
    /// </summary>
    public TimeSpan? Timeout { get; }
}
