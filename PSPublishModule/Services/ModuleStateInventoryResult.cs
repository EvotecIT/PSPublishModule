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
