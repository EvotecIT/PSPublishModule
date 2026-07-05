namespace PowerForge;

/// <summary>
/// Request for installing a module package through the managed module engine.
/// </summary>
public sealed class ManagedModuleInstallRequest
{
    /// <summary>
    /// Repository to query and download from.
    /// </summary>
    public ManagedModuleRepository Repository { get; set; } = null!;

    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Exact package version. When omitted, the latest repository version is used.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Minimum package version allowed when <see cref="Version" /> is omitted.
    /// </summary>
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// Maximum package version allowed when <see cref="Version" /> is omitted.
    /// </summary>
    public string? MaximumVersion { get; set; }

    /// <summary>
    /// NuGet-style version range policy used when <see cref="Version" /> is omitted.
    /// </summary>
    public string? VersionPolicy { get; set; }

    /// <summary>
    /// Include prerelease versions when resolving latest.
    /// </summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>
    /// Install scope used when <see cref="ModuleRoot" /> is not supplied.
    /// </summary>
    public ManagedModuleInstallScope Scope { get; set; } = ManagedModuleInstallScope.CurrentUser;

    /// <summary>
    /// PowerShell path family used when resolving default module roots.
    /// </summary>
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>
    /// Explicit module root. Required when scope is custom.
    /// </summary>
    public string? ModuleRoot { get; set; }

    /// <summary>
    /// Optional download cache directory. A temporary directory is used when omitted.
    /// </summary>
    public string? PackageCacheDirectory { get; set; }

    internal bool PackageCacheDirectoryIsOperationLocal { get; set; }

    /// <summary>
    /// Save the selected packages as .nupkg files instead of unpacking them into module folders.
    /// </summary>
    public bool SaveAsNupkg { get; set; }

    /// <summary>
    /// Maximum number of dependency branches to install concurrently. A value of 0 uses the engine default.
    /// </summary>
    public int DependencyConcurrency { get; set; }

    /// <summary>
    /// Optional expected SHA256 hash for the root package being installed or saved.
    /// </summary>
    public string? ExpectedPackageSha256 { get; set; }

    /// <summary>
    /// Optional repository/package trust policy.
    /// </summary>
    public ManagedModuleTrustPolicy? TrustPolicy { get; set; }

    /// <summary>
    /// Repository credential.
    /// </summary>
    public RepositoryCredential? Credential { get; set; }

    /// <summary>
    /// Reinstall when the target module version already exists.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Allow command exports to overlap with other installed modules in the target root.
    /// </summary>
    public bool AllowClobber { get; set; }

    /// <summary>
    /// Accept package licenses when a package declares license acceptance is required.
    /// </summary>
    public bool AcceptLicense { get; set; }

    /// <summary>
    /// Require Authenticode validation for signable files extracted from the package before promotion.
    /// </summary>
    public bool AuthenticodeCheck { get; set; }

    /// <summary>
    /// Skip installing dependencies declared by the package.
    /// </summary>
    public bool SkipDependencyCheck { get; set; }

    internal bool RepairInstalledManifestDependencies { get; set; }
}
