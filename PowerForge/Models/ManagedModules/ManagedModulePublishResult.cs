namespace PowerForge;

/// <summary>
/// Result returned after packaging and publishing a managed module package.
/// </summary>
public sealed class ManagedModulePublishResult
{
    /// <summary>
    /// Package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Package version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Created package path.
    /// </summary>
    public string PackagePath { get; set; } = string.Empty;

    /// <summary>
    /// Number of module files added to the package.
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Size of the created package in bytes.
    /// </summary>
    public long PackageBytes { get; set; }

    /// <summary>
    /// Repository name that received the package.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Repository source used for publishing.
    /// </summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>
    /// Upload endpoint or local destination used for publishing.
    /// </summary>
    public string PublishSource { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status code returned by a remote publish operation.
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// True when the package was delivered to the target repository.
    /// </summary>
    public bool Published { get; set; }
}
