namespace PowerForge;

/// <summary>
/// Request for uninstalling a managed script resource.
/// </summary>
public sealed class ManagedScriptUninstallRequest
{
    /// <summary>Script name without extension.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Install scope used when ScriptRoot is not supplied.</summary>
    public ManagedScriptInstallScope Scope { get; set; } = ManagedScriptInstallScope.CurrentUser;

    /// <summary>PowerShell path family used when resolving default CurrentUser or AllUsers script roots.</summary>
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>Explicit script root. When supplied, Scope is treated as Custom.</summary>
    public string? ScriptRoot { get; set; }

    /// <summary>Exact installed script version to remove. When omitted, any installed version is removed.</summary>
    public string? Version { get; set; }

    /// <summary>Remove a script even when PSScriptInfo metadata cannot be read.</summary>
    public bool Force { get; set; }
}
