namespace PowerForge;

/// <summary>
/// Request for updating a module through the managed module engine.
/// </summary>
public sealed class ManagedModuleUpdateRequest
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
    /// Exact target version. When omitted, the latest repository version is used.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Minimum target version allowed when <see cref="Version" /> is omitted.
    /// </summary>
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// Maximum target version allowed when <see cref="Version" /> is omitted.
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
    /// Scope to inspect and update.
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
    /// Optional package cache directory.
    /// </summary>
    public string? PackageCacheDirectory { get; set; }

    /// <summary>
    /// Maximum number of dependency branches to install concurrently. A value of 0 uses the engine default.
    /// </summary>
    public int DependencyConcurrency { get; set; }

    /// <summary>
    /// Optional expected SHA256 hash for the requested module package.
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
    /// Reinstall the target version when it is already installed.
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
    /// Require Authenticode validation for signable files extracted from delivered packages before promotion.
    /// </summary>
    public bool AuthenticodeCheck { get; set; }

    /// <summary>
    /// Skip installing dependencies declared by the package.
    /// </summary>
    public bool SkipDependencyCheck { get; set; }

    /// <summary>
    /// Loaded module evidence supplied by the host or inventory engine.
    /// </summary>
    public IReadOnlyList<ManagedModuleLoadedModule> LoadedModules { get; set; } = Array.Empty<ManagedModuleLoadedModule>();

    /// <summary>
    /// Optional family policy used to keep related installed modules version-coherent.
    /// </summary>
    public ManagedModuleFamilyPolicy? FamilyPolicy { get; set; }

    /// <summary>
    /// Optional source policy used to require managed receipt evidence from the requested repository.
    /// </summary>
    public ManagedModuleSourcePolicy? SourcePolicy { get; set; }

    /// <summary>
    /// Allow update when the requested module is already loaded in the current session.
    /// </summary>
    public bool AllowLoadedModuleUpdate { get; set; }
}
