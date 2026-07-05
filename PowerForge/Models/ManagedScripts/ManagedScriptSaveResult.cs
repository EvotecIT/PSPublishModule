namespace PowerForge;

/// <summary>
/// Result returned after saving a script resource from a managed repository.
/// </summary>
public sealed class ManagedScriptSaveResult
{
    /// <summary>Script package id.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Selected package version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Save outcome.</summary>
    public ManagedScriptSaveStatus Status { get; set; }

    /// <summary>Repository name used by the operation.</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>Repository source used by the operation.</summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>Destination directory for saved scripts.</summary>
    public string DestinationPath { get; set; } = string.Empty;

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

    /// <summary>Elapsed time spent by this save operation.</summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>Elapsed time spent resolving the selected package version.</summary>
    public TimeSpan VersionResolutionElapsed { get; set; }

    /// <summary>Elapsed time spent downloading or copying the package.</summary>
    public TimeSpan DownloadElapsed { get; set; }

    /// <summary>Elapsed time spent extracting and validating the script payload.</summary>
    public TimeSpan ExtractionElapsed { get; set; }

    /// <summary>Package download or copy result when package delivery happened.</summary>
    public ManagedModuleDownloadResult? Download { get; set; }

    /// <summary>PSScriptInfo metadata read from the saved script.</summary>
    public ManagedScriptFileInfo? ScriptInfo { get; set; }
}

/// <summary>
/// Script save operation status.
/// </summary>
public enum ManagedScriptSaveStatus
{
    /// <summary>The script was saved or replaced.</summary>
    Saved,

    /// <summary>The selected script version was already present.</summary>
    SkippedExisting
}
