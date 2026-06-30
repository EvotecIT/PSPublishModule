namespace PowerForge;

/// <summary>
/// Transport policy options for managed module repository HTTP operations.
/// </summary>
public sealed class ManagedModuleRepositoryClientOptions
{
    /// <summary>
    /// Maximum number of retry attempts after the initial request for transient failures.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Delay before retrying a transient failure.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Optional timeout applied to each HTTP request attempt.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Maximum package bytes accepted while downloading or copying a package.
    /// </summary>
    public long MaxPackageBytes { get; set; } = 1024L * 1024L * 1024L;

    /// <summary>
    /// Maximum concurrent HTTP connections allowed per repository host when the managed client owns the HTTP handler.
    /// </summary>
    public int MaxConnectionsPerServer { get; set; } = 32;

    /// <summary>
    /// True when HTTP requests should use proxy configuration.
    /// </summary>
    public bool UseProxy { get; set; } = true;

    /// <summary>
    /// Optional explicit proxy address used by the managed repository client when it owns the HTTP handler.
    /// </summary>
    public Uri? ProxyAddress { get; set; }

    /// <summary>
    /// Optional proxy credential for explicit proxy connections.
    /// </summary>
    public RepositoryCredential? ProxyCredential { get; set; }

    /// <summary>
    /// True when the explicit proxy should be bypassed for local addresses.
    /// </summary>
    public bool BypassProxyOnLocal { get; set; } = true;
}
