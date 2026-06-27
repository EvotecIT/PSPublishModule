namespace PowerForge;

/// <summary>
/// PowerShell module install scope for managed module operations.
/// </summary>
public enum ManagedModuleInstallScope
{
    /// <summary>
    /// Install under the current user's module path.
    /// </summary>
    CurrentUser,

    /// <summary>
    /// Install under the machine-wide module path.
    /// </summary>
    AllUsers,

    /// <summary>
    /// Install under a caller-provided module root.
    /// </summary>
    Custom
}
