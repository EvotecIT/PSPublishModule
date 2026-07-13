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
    /// <param name="workingDirectory">Optional NuGet configuration and process context.</param>
    /// <param name="timeout">Optional timeout override.</param>
    public DotNetNuGetPushRequest(
        string packagePath,
        string apiKey,
        string source,
        bool skipDuplicate = true,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
        : this(
            packagePath,
            apiKey,
            source,
            skipDuplicate,
            workingDirectory,
            timeout,
            suppressCompanionSymbols: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DotNetNuGetPushRequest"/> class.
    /// </summary>
    /// <param name="packagePath">Package path to push.</param>
    /// <param name="apiKey">API key passed to the feed.</param>
    /// <param name="source">Feed source URL or name.</param>
    /// <param name="skipDuplicate">When true, passes <c>--skip-duplicate</c>.</param>
    /// <param name="workingDirectory">Optional NuGet configuration and process context.</param>
    /// <param name="timeout">Optional timeout override.</param>
    /// <param name="suppressCompanionSymbols">When true, passes <c>--no-symbols</c>.</param>
    public DotNetNuGetPushRequest(
        string packagePath,
        string apiKey,
        string source,
        bool skipDuplicate,
        string? workingDirectory,
        TimeSpan? timeout,
        bool suppressCompanionSymbols)
    {
        PackagePath = packagePath;
        ApiKey = apiKey;
        Source = source;
        SkipDuplicate = skipDuplicate;
        WorkingDirectory = workingDirectory;
        Timeout = timeout;
        SuppressCompanionSymbols = suppressCompanionSymbols;
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
    /// Gets the optional NuGet configuration and process context. When implicit symbol publication is enabled,
    /// the client may stage the primary and companion packages beneath this directory for one-command publication.
    /// </summary>
    public string? WorkingDirectory { get; }

    /// <summary>
    /// Gets the optional timeout override.
    /// </summary>
    public TimeSpan? Timeout { get; }

    /// <summary>
    /// Gets a value indicating whether implicit companion symbol publication should be suppressed.
    /// </summary>
    public bool SuppressCompanionSymbols { get; }
}
