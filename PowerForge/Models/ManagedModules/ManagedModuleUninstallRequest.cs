namespace PowerForge;

/// <summary>
/// Request for removing installed module versions through the managed module engine.
/// </summary>
public sealed class ManagedModuleUninstallRequest
{
    /// <summary>
    /// Module name or wildcard patterns to remove.
    /// </summary>
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Exact version or NuGet-style version range to remove. When omitted, the latest matching version is selected.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Restrict matching to prerelease module versions.
    /// </summary>
    public bool Prerelease { get; set; }

    /// <summary>
    /// Install scope used when <see cref="ModuleRoot" /> is not supplied.
    /// </summary>
    public ManagedModuleInstallScope Scope { get; set; } = ManagedModuleInstallScope.CurrentUser;

    /// <summary>
    /// PowerShell path family used when resolving default module roots.
    /// </summary>
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>
    /// Explicit module root. Required when scope is custom.
    /// </summary>
    public string? ModuleRoot { get; set; }

    /// <summary>
    /// Exact installed module directory to remove when the request originates from installed-resource inventory.
    /// </summary>
    public string? InstalledLocation { get; set; }

    /// <summary>
    /// Skip checking whether removed modules are still required by other installed modules.
    /// </summary>
    public bool SkipDependencyCheck { get; set; }

    /// <summary>
    /// Allow removal of module versions that are loaded in the current process.
    /// </summary>
    public bool AllowLoadedModuleUninstall { get; set; }

    /// <summary>
    /// Defers loaded-module blocking until uninstall execution so callers can confirm or filter planned targets first.
    /// </summary>
    public bool DeferLoadedModuleCheck { get; set; }

    /// <summary>
    /// Defers dependency blocking until uninstall execution so callers can confirm or filter planned targets first.
    /// </summary>
    public bool DeferDependencyCheck { get; set; }

    /// <summary>
    /// Loaded module evidence from the host process.
    /// </summary>
    public IReadOnlyList<ManagedModuleLoadedModule> LoadedModules { get; set; } = Array.Empty<ManagedModuleLoadedModule>();
}
