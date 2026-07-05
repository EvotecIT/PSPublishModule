namespace PowerForge;

/// <summary>
/// Non-mutating plan for saving a script resource from a managed repository.
/// </summary>
public sealed class ManagedScriptSavePlan
{
    /// <summary>Script package id.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Selected package version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Planned operation.</summary>
    public ManagedScriptSavePlanAction Action { get; set; }

    /// <summary>Repository name used by the operation.</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>Repository source used by the operation.</summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>Destination directory for saved scripts.</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Final script path.</summary>
    public string ScriptPath { get; set; } = string.Empty;

    /// <summary>True when the operation would write or replace a script file.</summary>
    public bool WouldWriteFiles { get; set; }

    /// <summary>True when the operation would download and verify package metadata/content without writing the existing script.</summary>
    public bool WouldVerifyPackage { get; set; }

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

    /// <summary>True when repository metadata indicates the selected package requires license acceptance.</summary>
    public bool LicenseAcceptanceRequired { get; set; }

    /// <summary>Reason the planned operation cannot write, when applicable.</summary>
    public string? BlockReason { get; set; }
}

/// <summary>
/// Planned script save action.
/// </summary>
public enum ManagedScriptSavePlanAction
{
    /// <summary>Save a script that is not present at the target path.</summary>
    Save,

    /// <summary>Skip because the target script already has the selected version.</summary>
    SkipExisting,

    /// <summary>Replace an existing script because force was requested.</summary>
    Reinstall,

    /// <summary>Verify package policy for an existing matching script without writing it.</summary>
    VerifyExisting,

    /// <summary>Cannot write because the target path already has another version or unreadable metadata and force was not requested.</summary>
    BlockedExisting
}
