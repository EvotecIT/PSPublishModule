namespace PowerForge;

/// <summary>
/// Result returned after delivering an existing package to a managed module repository.
/// </summary>
public sealed class ManagedModulePackagePublishResult
{
    /// <summary>
    /// Package path that was delivered.
    /// </summary>
    public string PackagePath { get; set; } = string.Empty;

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
