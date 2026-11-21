namespace PowerForge;

/// <summary>
/// Raised when a managed module package or repository does not satisfy the requested trust policy.
/// </summary>
public sealed class ManagedModuleTrustException : InvalidOperationException
{
    /// <summary>
    /// Creates a trust-policy exception.
    /// </summary>
    public ManagedModuleTrustException(
        string message,
        string? moduleName,
        string? version,
        string repositoryName,
        string reason)
        : base(message)
    {
        ModuleName = moduleName;
        Version = version;
        RepositoryName = repositoryName;
        Reason = reason;
    }

    /// <summary>
    /// Module or package id when the violation is package-specific.
    /// </summary>
    public string? ModuleName { get; }

    /// <summary>
    /// Package version when the violation is package-specific.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// Repository name involved in the failed trust check.
    /// </summary>
    public string RepositoryName { get; }

    /// <summary>
    /// Machine-readable reason text for diagnostics and tests.
    /// </summary>
    public string Reason { get; }
}
