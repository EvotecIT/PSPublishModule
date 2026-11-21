namespace PowerForge;

/// <summary>
/// Planned action for a managed module install request.
/// </summary>
public enum ManagedModuleInstallPlanAction
{
    /// <summary>
    /// A new module version would be installed.
    /// </summary>
    Install,

    /// <summary>
    /// An existing module version would be replaced because Force was requested.
    /// </summary>
    Reinstall,

    /// <summary>
    /// The requested module version already exists and Force was not requested.
    /// </summary>
    SkipExisting
}
