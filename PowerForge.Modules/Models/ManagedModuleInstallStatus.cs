namespace PowerForge;

/// <summary>
/// Outcome of a managed module install request.
/// </summary>
public enum ManagedModuleInstallStatus
{
    /// <summary>
    /// The module version was installed.
    /// </summary>
    Installed,

    /// <summary>
    /// The requested version was already present and force was not requested.
    /// </summary>
    AlreadyInstalled
}
