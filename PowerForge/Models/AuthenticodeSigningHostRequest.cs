namespace PowerForge;

/// <summary>
/// Host-facing request for invoking Authenticode signing through PSPublishModule.
/// </summary>
public sealed class AuthenticodeSigningHostRequest
{
    /// <summary>
    /// Working directory and target path passed to the signing command.
    /// </summary>
    public string SigningPath { get; set; } = string.Empty;

    /// <summary>
    /// File include patterns passed to <c>Register-Certificate</c>.
    /// </summary>
    public IReadOnlyList<string> IncludePatterns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Path to the PSPublishModule manifest that should be imported.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Certificate thumbprint.
    /// </summary>
    public string Thumbprint { get; set; } = string.Empty;

    /// <summary>
    /// Certificate store name.
    /// </summary>
    public string StoreName { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp server URI.
    /// </summary>
    public string TimeStampServer { get; set; } = string.Empty;
}
