namespace PowerForge;

/// <summary>
/// Installed module version selected for managed uninstall.
/// </summary>
public sealed class ManagedModuleUninstallTarget
{
    /// <summary>
    /// Module name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Installed module version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Module root containing the module folder.
    /// </summary>
    public string ModuleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Installed module version directory.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Installed module GUID, when declared by the manifest.
    /// </summary>
    public string? Guid { get; set; }

    /// <summary>
    /// True when the selected install is a legacy flat module folder instead of a version directory.
    /// </summary>
    public bool IsFlatLayout { get; set; }

    /// <summary>
    /// True when loaded module evidence matched this installed version.
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// Installed modules that would lose a required dependency if this target is removed.
    /// </summary>
    public IReadOnlyList<ManagedModuleUninstallDependency> RequiredBy { get; set; } = Array.Empty<ManagedModuleUninstallDependency>();
}
