namespace PowerForge;

/// <summary>
/// Planned managed module uninstall operation.
/// </summary>
public sealed class ManagedModuleUninstallPlan
{
    /// <summary>
    /// Module names or wildcard patterns requested by the caller.
    /// </summary>
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Version policy requested by the caller.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Module root inspected by the plan.
    /// </summary>
    public string ModuleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Module roots whose installed modules participate in dependency revalidation.
    /// </summary>
    public IReadOnlyList<string> DependencyModuleRoots { get; set; } = Array.Empty<string>();

    internal IReadOnlyList<string> DependencyModuleRootsRequiringAvailability { get; set; } = Array.Empty<string>();

    /// <summary>
    /// True when dependency checks are skipped.
    /// </summary>
    public bool SkipDependencyCheck { get; set; }

    /// <summary>
    /// True when loaded-module uninstall safety has been explicitly overridden.
    /// </summary>
    public bool AllowLoadedModuleUninstall { get; set; }

    /// <summary>
    /// Selected installed module versions.
    /// </summary>
    public IReadOnlyList<ManagedModuleUninstallTarget> Targets { get; set; } = Array.Empty<ManagedModuleUninstallTarget>();

    internal IReadOnlyList<ManagedModuleUninstallTarget> DependencyRemovalTargets { get; set; } = Array.Empty<ManagedModuleUninstallTarget>();

    /// <summary>
    /// Explicit module names from the request that did not match any installed module.
    /// </summary>
    public IReadOnlyList<string> MissingNames { get; set; } = Array.Empty<string>();
}
