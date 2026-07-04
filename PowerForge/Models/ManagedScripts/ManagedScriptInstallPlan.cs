namespace PowerForge;

/// <summary>
/// Non-mutating plan for installing a script resource from a managed repository.
/// </summary>
public sealed class ManagedScriptInstallPlan
{
    /// <summary>Script package id.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Selected package version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Planned operation.</summary>
    public ManagedScriptInstallPlanAction Action { get; set; }

    /// <summary>Repository name used by the operation.</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>Repository source used by the operation.</summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>Resolved install scope.</summary>
    public ManagedScriptInstallScope Scope { get; set; }

    /// <summary>Resolved PowerShell path family.</summary>
    public ManagedModuleShellEdition ShellEdition { get; set; }

    /// <summary>Destination directory for installed scripts.</summary>
    public string ScriptRoot { get; set; } = string.Empty;

    /// <summary>Final script path.</summary>
    public string ScriptPath { get; set; } = string.Empty;

    /// <summary>True when the operation would write or replace a script file.</summary>
    public bool WouldWriteFiles { get; set; }

    /// <summary>Existing script version read from PSScriptInfo, when available.</summary>
    public string? ExistingVersion { get; set; }

    /// <summary>Exact version requested by the caller, when supplied.</summary>
    public string? RequestedVersion { get; set; }

    /// <summary>Minimum version requested by the caller, when supplied.</summary>
    public string? MinimumVersion { get; set; }

    /// <summary>Maximum version requested by the caller, when supplied.</summary>
    public string? MaximumVersion { get; set; }

    /// <summary>NuGet-style version policy requested by the caller, when supplied.</summary>
    public string? VersionPolicy { get; set; }

    /// <summary>Expected SHA256 hash supplied by the caller, when package integrity verification was requested.</summary>
    public string? ExpectedPackageSha256 { get; set; }
}

/// <summary>
/// Planned script install action.
/// </summary>
public enum ManagedScriptInstallPlanAction
{
    /// <summary>Install a script that is not present at the target path.</summary>
    Install,

    /// <summary>Skip because the target script already has the selected version.</summary>
    SkipExisting,

    /// <summary>Replace an existing script because force was requested.</summary>
    Reinstall
}
