namespace PowerForge;

/// <summary>
/// Outcome of a managed module update request.
/// </summary>
public enum ManagedModuleUpdateStatus
{
    /// <summary>
    /// The requested module was already at the selected version.
    /// </summary>
    UpToDate,

    /// <summary>
    /// The requested module was updated from an older installed version.
    /// </summary>
    Updated,

    /// <summary>
    /// The requested module was not present in the selected scope and was installed.
    /// </summary>
    InstalledMissing
}
