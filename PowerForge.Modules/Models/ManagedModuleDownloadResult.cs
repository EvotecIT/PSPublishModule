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
    /// Time from the start of the successful HTTP attempt to the first package body bytes read.
    /// Includes redirects in that attempt. Null for cache hits and local feeds.
    /// </summary>
    public TimeSpan? TimeToFirstByte { get; set; }

    /// <summary>
    /// Time from observing the first package body byte through the end of the response body.
    /// Includes copying and hashing the bytes. Null for cache hits and local feeds.
    /// </summary>
    public TimeSpan? BodyTransferElapsed { get; set; }

    /// <summary>
    /// Average rate for the bytes after the first during <see cref="BodyTransferElapsed"/>.
    /// Null when no HTTP body transfer was measured.
    /// </summary>
    public double? BytesPerSecond => BytesWritten > 1 && BodyTransferElapsed is { TotalSeconds: > 0 } elapsed
        ? (BytesWritten - 1) / elapsed.TotalSeconds
        : null;

    /// <summary>
    /// Number of repository HTTP request attempts used while delivering this package.
    /// </summary>
    public long RequestCount { get; set; }

    /// <summary>
    /// Number of HTTP redirects followed while delivering this package.
    /// </summary>
    public long RedirectCount { get; set; }

    /// <summary>
    /// True when the package was reused from the destination cache without repository transfer.
    /// </summary>
    public bool FromCache { get; set; }

    /// <summary>
    /// SHA256 hash of the downloaded or copied package, encoded as lowercase hexadecimal.
    /// </summary>
    public string PackageSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Metadata read from the downloaded package when available.
    /// </summary>
    public ManagedModulePackageMetadata? Metadata { get; set; }
}
