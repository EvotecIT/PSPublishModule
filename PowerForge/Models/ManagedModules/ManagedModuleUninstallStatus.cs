namespace PowerForge;

/// <summary>
/// Outcome of a managed module uninstall operation.
/// </summary>
public enum ManagedModuleUninstallStatus
{
    /// <summary>
    /// The selected module version was removed.
    /// </summary>
    Uninstalled,

    /// <summary>
    /// No installed module version matched the request.
    /// </summary>
    NotInstalled
}
