namespace PowerForge;

/// <summary>
/// Planned action for one installed module covered by a managed module family policy.
/// </summary>
public sealed class ManagedModuleFamilyUpdatePlanItem
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
    /// All installed versions discovered for this family member.
    /// </summary>
    public IReadOnlyList<string> InstalledVersions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Planned family action.
    /// </summary>
    public ManagedModuleFamilyUpdatePlanAction Action { get; set; }

    /// <summary>
    /// Versioned module directory selected by this plan item.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// True when the repository contains the selected target version for this module.
    /// </summary>
    public bool RepositoryVersionAvailable { get; set; }

    /// <summary>
    /// True when this action would write module files.
    /// </summary>
    public bool WouldWriteFiles { get; set; }

    /// <summary>
    /// Diagnostic reason when the family action cannot be applied safely.
    /// </summary>
    public string? ConflictReason { get; set; }
}
