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
    /// Install result when the operation wrote files.
    /// </summary>
    public ManagedModuleInstallResult? InstallResult { get; set; }
}
