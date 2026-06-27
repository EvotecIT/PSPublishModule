namespace PowerForge;

/// <summary>
/// Result returned by the managed module updater.
/// </summary>
public sealed class ManagedModuleUpdateResult
{
    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Version selected by the update operation.
    /// </summary>
    public string TargetVersion { get; set; } = string.Empty;

    /// <summary>
    /// Highest installed version in the inspected scope before the update.
    /// </summary>
    public string? PreviousVersion { get; set; }

    /// <summary>
    /// Update outcome.
    /// </summary>
    public ManagedModuleUpdateStatus Status { get; set; }

    /// <summary>
    /// Repository name used by the operation.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Module root that was inspected.
    /// </summary>
    public string ModuleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Versioned module directory selected by the operation.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Elapsed time spent by this update operation.
    /// </summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>
    /// Install result when the operation wrote files.
    /// </summary>
    public ManagedModuleInstallResult? InstallResult { get; set; }

    /// <summary>
    /// Receipt written after successful delivery, when the operation changed disk state.
    /// </summary>
    public ManagedModuleReceipt? Receipt { get; set; }

    /// <summary>
    /// Full path to the receipt written after successful delivery.
    /// </summary>
    public string? ReceiptPath { get; set; }
}
