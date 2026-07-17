namespace PSPublishModule;

/// <summary>
/// PowerShell-facing module-state inventory result.
/// </summary>
public sealed class ModuleStateInventoryResult
{
    /// <summary>
    /// Gets or sets the inventory source.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the module paths that were scanned.
    /// </summary>
    public string[] ModulePaths { get; set; } = [];

    /// <summary>
    /// Gets or sets structured module roots that were scanned, including edition, scope, and user-profile identity.
    /// </summary>
    public ModuleStateInventoryPathResult[] ScannedPaths { get; set; } = [];

    /// <summary>
    /// Gets or sets diagnostics produced while resolving or scanning module roots.
    /// </summary>
    public ModuleStateInventoryDiagnosticResult[] Diagnostics { get; set; } = [];

    /// <summary>
    /// Gets or sets installed modules discovered in the inventory.
    /// </summary>
    public ModuleStateInstalledModuleResult[] InstalledModules { get; set; } = [];
}

/// <summary>
/// PowerShell-facing installed module inventory entry.
/// </summary>
public sealed class ModuleStateInstalledModuleResult
{
    /// <summary>
    /// Gets or sets the installed resource type.
    /// </summary>
    public string Type { get; set; } = "Module";

    /// <summary>
    /// Gets or sets the module name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the module version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the PowerShell edition associated with the module path.
    /// </summary>
    public string? PowerShellEdition { get; set; }

    /// <summary>
    /// Gets or sets the module installation scope associated with the module path.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the discovered module path.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the physical module root containing this installed module copy.
    /// </summary>
    public string? ModuleRoot { get; set; }

    /// <summary>
    /// Gets or sets every module root that was scanned with this inventory row. Pipeline consumers use this
    /// provenance to preserve estate-wide safety checks even when the row itself identifies one exact install.
    /// </summary>
    public string[] InventoryModuleRoots { get; set; } = [];

    /// <summary>
    /// Gets or sets structured provenance for every root scanned with this row, including visibility and
    /// whether the inventory successfully enumerated the root.
    /// </summary>
    public ModuleStateInventoryPathResult[] InventoryPaths { get; set; } = [];

    /// <summary>
    /// Gets or sets the local user profile associated with this module root when known.
    /// </summary>
    public string? ProfileName { get; set; }

    /// <summary>
    /// Gets or sets the PSResourceGet-compatible installed location.
    /// </summary>
    public string? InstalledLocation
    {
        get => Path;
        set => Path = value;
    }

    /// <summary>
    /// Gets or sets the repository that supplied the installed module when known.
    /// </summary>
    public string? SourceRepository { get; set; }

    /// <summary>
    /// Gets or sets whether this version is loaded in the current process inventory.
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// Gets or sets whether this module copy is the first import candidate by module path precedence.
    /// </summary>
    public bool IsEffectiveImportCandidate { get; set; }

    /// <summary>
    /// Gets or sets command names explicitly exported by the module manifest when known.
    /// </summary>
    public string[] ExportedCommands { get; set; } = [];
}

/// <summary>
/// PowerShell-facing module root scanned by module-state inventory.
/// </summary>
public sealed class ModuleStateInventoryPathResult
{
    /// <summary>Gets or sets the physical module root.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the PowerShell edition associated with the root.</summary>
    public string? PowerShellEdition { get; set; }

    /// <summary>Gets or sets the installation scope associated with the root.</summary>
    public string? Scope { get; set; }

    /// <summary>Gets or sets the local user profile associated with the root.</summary>
    public string? ProfileName { get; set; }

    /// <summary>Gets or sets whether failure to scan the root makes inventory incomplete.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Gets or sets whether inventory successfully enumerated this root.</summary>
    public bool WasAvailable { get; set; }
}

/// <summary>
/// PowerShell-facing module-state inventory diagnostic.
/// </summary>
public sealed class ModuleStateInventoryDiagnosticResult
{
    /// <summary>Gets or sets the diagnostic severity.</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable diagnostic code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the diagnostic message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the affected path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the affected PowerShell edition.</summary>
    public string? PowerShellEdition { get; set; }

    /// <summary>Gets or sets the affected installation scope.</summary>
    public string? Scope { get; set; }

    /// <summary>Gets or sets the affected local user profile.</summary>
    public string? ProfileName { get; set; }
}
