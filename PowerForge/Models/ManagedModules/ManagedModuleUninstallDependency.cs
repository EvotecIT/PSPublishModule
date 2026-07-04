namespace PowerForge;

/// <summary>
/// Describes an installed module that depends on a module version selected for uninstall.
/// </summary>
public sealed class ManagedModuleUninstallDependency
{
    /// <summary>
    /// Name of the module declaring the dependency.
    /// </summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>
    /// Version of the module declaring the dependency.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Path to the module declaring the dependency.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable required module constraint.
    /// </summary>
    public string RequiredVersion { get; set; } = string.Empty;
}
