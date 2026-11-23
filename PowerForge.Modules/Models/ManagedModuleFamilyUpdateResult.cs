namespace PowerForge;

/// <summary>
/// Result for one module updated as part of a managed module family policy.
/// </summary>
public sealed class ManagedModuleFamilyUpdateResult
{
    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Policy name that matched this module.
    /// </summary>
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>
    /// Version selected for family alignment.
    /// </summary>
    public string TargetVersion { get; set; } = string.Empty;

    /// <summary>
    /// Highest installed version before update.
    /// </summary>
    public string? PreviousVersion { get; set; }

    /// <summary>
    /// Action selected for this family member.
    /// </summary>
    public ManagedModuleFamilyUpdatePlanAction Action { get; set; }

    /// <summary>
    /// Versioned module directory selected by the operation.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Install result when the family operation wrote files.
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
