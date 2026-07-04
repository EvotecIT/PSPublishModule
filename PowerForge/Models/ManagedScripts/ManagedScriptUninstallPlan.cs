namespace PowerForge;

/// <summary>
/// Non-mutating plan for uninstalling a managed script resource.
/// </summary>
public sealed class ManagedScriptUninstallPlan
{
    /// <summary>Script name without extension.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Planned operation.</summary>
    public ManagedScriptUninstallPlanAction Action { get; set; }

    /// <summary>Resolved install scope.</summary>
    public ManagedScriptInstallScope Scope { get; set; }

    /// <summary>Resolved PowerShell path family.</summary>
    public ManagedModuleShellEdition ShellEdition { get; set; }

    /// <summary>Destination directory for installed scripts.</summary>
    public string ScriptRoot { get; set; } = string.Empty;

    /// <summary>Final script path.</summary>
    public string ScriptPath { get; set; } = string.Empty;

    /// <summary>True when the operation would remove a script file.</summary>
    public bool WouldRemoveFile { get; set; }

    /// <summary>Installed script version read from PSScriptInfo, when available.</summary>
    public string? ExistingVersion { get; set; }

    /// <summary>Exact version requested by the caller, when supplied.</summary>
    public string? RequestedVersion { get; set; }

    /// <summary>Reason the script would not be removed, when applicable.</summary>
    public string? SkipReason { get; set; }
}

/// <summary>
/// Planned script uninstall action.
/// </summary>
public enum ManagedScriptUninstallPlanAction
{
    /// <summary>Remove the installed script file.</summary>
    Remove,

    /// <summary>Skip because the target script is not installed.</summary>
    SkipMissing,

    /// <summary>Skip because the installed version does not match the requested version.</summary>
    SkipVersionMismatch
}
