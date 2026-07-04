namespace PowerForge;

/// <summary>
/// Result returned after uninstalling a managed script resource.
/// </summary>
public sealed class ManagedScriptUninstallResult
{
    /// <summary>Script name without extension.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Uninstall outcome.</summary>
    public ManagedScriptUninstallStatus Status { get; set; }

    /// <summary>Resolved install scope.</summary>
    public ManagedScriptInstallScope Scope { get; set; }

    /// <summary>Resolved PowerShell path family.</summary>
    public ManagedModuleShellEdition ShellEdition { get; set; }

    /// <summary>Destination directory for installed scripts.</summary>
    public string ScriptRoot { get; set; } = string.Empty;

    /// <summary>Final script path.</summary>
    public string ScriptPath { get; set; } = string.Empty;

    /// <summary>Installed script version read before removal, when available.</summary>
    public string? ExistingVersion { get; set; }

    /// <summary>Exact version requested by the caller, when supplied.</summary>
    public string? RequestedVersion { get; set; }

    /// <summary>Reason the script was not removed, when applicable.</summary>
    public string? SkipReason { get; set; }

    /// <summary>Elapsed time spent by this uninstall operation.</summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>PSScriptInfo metadata read before removal, when available.</summary>
    public ManagedScriptFileInfo? ScriptInfo { get; set; }
}

/// <summary>
/// Script uninstall operation status.
/// </summary>
public enum ManagedScriptUninstallStatus
{
    /// <summary>The script file was removed.</summary>
    Removed,

    /// <summary>The script was already missing.</summary>
    SkippedMissing,

    /// <summary>The installed script version did not match the requested version.</summary>
    SkippedVersionMismatch
}
