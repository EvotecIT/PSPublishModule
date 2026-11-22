namespace PowerForge;

/// <summary>
/// Result returned by the managed module uninstaller.
/// </summary>
public sealed class ManagedModuleUninstallResult
{
    /// <summary>
    /// Module name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Installed or requested module version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Uninstall outcome.
    /// </summary>
    public ManagedModuleUninstallStatus Status { get; set; }

    /// <summary>
    /// Module root containing the module folder.
    /// </summary>
    public string ModuleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Installed module version directory.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Elapsed time spent by the uninstall operation.
    /// </summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>
    /// True when dependency checks were skipped.
    /// </summary>
    public bool DependencyCheckSkipped { get; set; }

    /// <summary>
    /// True when loaded module evidence matched this installed version.
    /// </summary>
    public bool WasLoaded { get; set; }
}
