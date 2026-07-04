namespace PowerForge;

/// <summary>
/// Request for installing a script resource package through the managed engine.
/// </summary>
public sealed class ManagedScriptInstallRequest
{
    /// <summary>Repository to query and download from.</summary>
    public ManagedModuleRepository Repository { get; set; } = null!;

    /// <summary>Script package id.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Install scope used when ScriptRoot is not supplied.</summary>
    public ManagedScriptInstallScope Scope { get; set; } = ManagedScriptInstallScope.CurrentUser;

    /// <summary>PowerShell path family used when resolving default CurrentUser or AllUsers script roots.</summary>
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>Explicit script root. When supplied, Scope is treated as Custom.</summary>
    public string? ScriptRoot { get; set; }

    /// <summary>Exact package version. When omitted, the latest repository version is used.</summary>
    public string? Version { get; set; }

    /// <summary>Minimum package version allowed when Version is omitted.</summary>
    public string? MinimumVersion { get; set; }

    /// <summary>Maximum package version allowed when Version is omitted.</summary>
    public string? MaximumVersion { get; set; }

    /// <summary>NuGet-style version range policy used when Version is omitted.</summary>
    public string? VersionPolicy { get; set; }

    /// <summary>Include prerelease versions when resolving latest.</summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>Optional download cache directory. A temporary directory is used when omitted.</summary>
    public string? PackageCacheDirectory { get; set; }

    /// <summary>Optional expected SHA256 hash for the script package.</summary>
    public string? ExpectedPackageSha256 { get; set; }

    /// <summary>Optional repository/package trust policy.</summary>
    public ManagedModuleTrustPolicy? TrustPolicy { get; set; }

    /// <summary>Repository credential.</summary>
    public RepositoryCredential? Credential { get; set; }

    /// <summary>Replace an existing script file.</summary>
    public bool Force { get; set; }

    /// <summary>Accept package licenses when a package declares license acceptance is required.</summary>
    public bool AcceptLicense { get; set; }
}
