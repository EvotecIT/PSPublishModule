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
    /// Repository source used by the operation.
    /// </summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>
    /// Exact version requested by the caller, when supplied.
    /// </summary>
    public string? RequestedVersion { get; set; }

    /// <summary>
    /// Minimum version requested by the caller, when supplied.
    /// </summary>
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// Maximum version requested by the caller, when supplied.
    /// </summary>
    public string? MaximumVersion { get; set; }

    /// <summary>
    /// NuGet-style version policy requested by the caller, when supplied.
    /// </summary>
    public string? VersionPolicy { get; set; }

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

    /// <summary>
    /// True when installed source evidence satisfied the requested source policy before the operation.
    /// </summary>
    public bool SourcePolicySatisfied { get; set; } = true;

    /// <summary>
    /// Diagnostic reason when source evidence did not satisfy policy before the operation.
    /// </summary>
    public string? SourcePolicyReason { get; set; }

    /// <summary>
    /// Managed receipt discovered for the installed version before the operation, when available.
    /// </summary>
    public ManagedModuleReceipt? InstalledReceipt { get; set; }

    /// <summary>
    /// Results for related installed modules covered by the family policy.
    /// </summary>
    public IReadOnlyList<ManagedModuleFamilyUpdateResult> FamilyResults { get; set; } = Array.Empty<ManagedModuleFamilyUpdateResult>();
}
