namespace PowerForge;

/// <summary>
/// Result returned after a managed module package download or local feed copy.
/// </summary>
public sealed class ManagedModuleDownloadResult
{
    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Package version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Repository name that provided the package.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Source URL or local path used for the download.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Full destination package path.
    /// </summary>
    public string PackagePath { get; set; } = string.Empty;

    /// <summary>
    /// Number of bytes written.
    /// </summary>
    public long BytesWritten { get; set; }

    /// <summary>
    /// Metadata read from the downloaded package when available.
    /// </summary>
    public ManagedModulePackageMetadata? Metadata { get; set; }
}
