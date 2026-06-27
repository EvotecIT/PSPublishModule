namespace PowerForge;

/// <summary>
/// Action selected by a non-mutating managed module update plan.
/// </summary>
public enum ManagedModuleUpdatePlanAction
{
    /// <summary>
    /// No installed copy exists in the selected scope, so update would install the selected version.
    /// </summary>
    InstallMissing,

    /// <summary>
    /// An older installed copy exists and update would install the selected version.
    /// </summary>
    Update,

    /// <summary>
    /// Force was requested and update would reinstall the selected version.
    /// </summary>
    Reinstall,

    /// <summary>
    /// The installed copy already satisfies the selected version.
    /// </summary>
    SkipUpToDate
}
