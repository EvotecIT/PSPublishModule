namespace PowerForge;

/// <summary>
/// Action selected for a module covered by a managed module family policy.
/// </summary>
public enum ManagedModuleFamilyUpdatePlanAction
{
    /// <summary>
    /// The family member already has the selected version.
    /// </summary>
    SkipUpToDate,

    /// <summary>
    /// The family member has an older installed version and can be aligned.
    /// </summary>
    Update,

    /// <summary>
    /// Force was requested and the family member would be reinstalled at the selected version.
    /// </summary>
    Reinstall,

    /// <summary>
    /// The repository does not contain the selected target version for this family member.
    /// </summary>
    MissingRepositoryVersion,

    /// <summary>
    /// The family member has a newer version than the selected target and downgrade was not requested.
    /// </summary>
    DowngradeBlocked
}
