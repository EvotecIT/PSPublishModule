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

    /// <summary>
    /// Repository credential.
    /// </summary>
    public RepositoryCredential? Credential { get; set; }

    /// <summary>
    /// Reinstall when the target module version already exists.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Skip installing dependencies declared by the package.
    /// </summary>
    public bool SkipDependencyCheck { get; set; }
}
