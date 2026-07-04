namespace PowerForge;

/// <summary>
/// Result returned after installing a script resource from a managed repository.
/// </summary>
public sealed class ManagedScriptInstallResult
{
    /// <summary>Script package id.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Selected package version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Install outcome.</summary>
    public ManagedScriptInstallStatus Status { get; set; }

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

    /// <summary>Elapsed time spent by this install operation.</summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>Elapsed time spent resolving the selected package version.</summary>
    public TimeSpan VersionResolutionElapsed { get; set; }

    /// <summary>Elapsed time spent downloading or copying the package.</summary>
    public TimeSpan DownloadElapsed { get; set; }

    /// <summary>Elapsed time spent extracting and validating the script payload.</summary>
    public TimeSpan ExtractionElapsed { get; set; }

    /// <summary>Package download or copy result when package delivery happened.</summary>
    public ManagedModuleDownloadResult? Download { get; set; }

    /// <summary>PSScriptInfo metadata read from the installed script.</summary>
    public ManagedScriptFileInfo? ScriptInfo { get; set; }
}

/// <summary>
/// Script install operation status.
/// </summary>
public enum ManagedScriptInstallStatus
{
    /// <summary>The script was installed or replaced.</summary>
    Installed,

    /// <summary>The selected script version was already present.</summary>
    SkippedExisting
}
