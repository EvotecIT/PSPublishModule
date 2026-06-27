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
}
