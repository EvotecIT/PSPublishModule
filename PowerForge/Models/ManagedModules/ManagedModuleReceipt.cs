namespace PowerForge;

/// <summary>
/// Durable evidence written after a managed module operation successfully changes disk state.
/// </summary>
public sealed class ManagedModuleReceipt
{
    /// <summary>
    /// Operation that produced the receipt.
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Module version delivered by the operation.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Previous module version known before the operation, when available.
    /// </summary>
    public string? PreviousVersion { get; set; }

    /// <summary>
    /// Repository name used by the operation.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Repository source used by the operation.
    /// </summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>
    /// Source URL or local package path used for delivery.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Package path downloaded or copied before extraction.
    /// </summary>
    public string PackagePath { get; set; } = string.Empty;

    /// <summary>
    /// SHA256 hash of the package used for delivery.
    /// </summary>
    public string PackageSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Module root that contains module folders.
    /// </summary>
    public string ModuleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Versioned module directory.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Operation status when the receipt was written.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Number of files extracted by the operation.
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Number of extracted package bytes.
    /// </summary>
    public long ExtractedBytes { get; set; }

    /// <summary>
    /// UTC timestamp when the operation completed and the receipt was written.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; set; }
}
