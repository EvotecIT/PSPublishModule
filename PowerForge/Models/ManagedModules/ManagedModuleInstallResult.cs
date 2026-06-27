namespace PowerForge;

/// <summary>
/// Result returned by the managed module installer.
/// </summary>
public sealed class ManagedModuleInstallResult
{
    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Installed or selected version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Install outcome.
    /// </summary>
    public ManagedModuleInstallStatus Status { get; set; }

    /// <summary>
    /// Repository name used by the operation.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Module root that contains module folders.
    /// </summary>
    public string ModuleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Versioned module directory.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Package download or copy result when the install performed package delivery.
    /// </summary>
    public ManagedModuleDownloadResult? Download { get; set; }

    /// <summary>
    /// Number of files extracted into the module directory.
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Number of extracted package bytes.
    /// </summary>
    public long ExtractedBytes { get; set; }

    /// <summary>
    /// Receipt written after successful delivery, when the operation changed disk state.
    /// </summary>
    public ManagedModuleReceipt? Receipt { get; set; }

    /// <summary>
    /// Full path to the receipt written after successful delivery.
    /// </summary>
    public string? ReceiptPath { get; set; }
}
