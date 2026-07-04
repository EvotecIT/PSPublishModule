namespace PowerForge;

/// <summary>
/// PowerShell script install scope for managed script operations.
/// </summary>
public enum ManagedScriptInstallScope
{
    /// <summary>
    /// Install under the current user's script path.
    /// </summary>
    CurrentUser,

    /// <summary>
    /// Install under the machine-wide script path.
    /// </summary>
    AllUsers,

    /// <summary>
    /// Install under a caller-provided script root.
    /// </summary>
    Custom
}
