namespace PowerForge;

/// <summary>
/// One benchmarkable managed module lifecycle scenario.
/// </summary>
public sealed class ManagedModuleBenchmarkScenario
{
    /// <summary>
    /// Stable scenario identifier used in reports.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Lifecycle operation to measure.
    /// </summary>
    public ManagedModuleBenchmarkOperation Operation { get; set; }

    /// <summary>
    /// Repository to query and download from.
    /// </summary>
    public ManagedModuleRepository Repository { get; set; } = null!;

    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Exact package version.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Minimum package version.
    /// </summary>
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// Maximum package version.
    /// </summary>
    public string? MaximumVersion { get; set; }

    /// <summary>
    /// NuGet-style version range policy.
    /// </summary>
    public string? VersionPolicy { get; set; }

    /// <summary>
    /// Include prerelease versions when resolving versions.
    /// </summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>
    /// Install or update scope used when <see cref="ModuleRoot" /> is omitted.
    /// </summary>
    public ManagedModuleInstallScope Scope { get; set; } = ManagedModuleInstallScope.CurrentUser;

    /// <summary>
    /// PowerShell path family used when resolving default module roots.
    /// </summary>
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>
    /// Explicit module root.
    /// </summary>
    public string? ModuleRoot { get; set; }

    /// <summary>
    /// Optional package cache directory.
    /// </summary>
    public string? PackageCacheDirectory { get; set; }

    /// <summary>
    /// Optional repository credential.
    /// </summary>
    public RepositoryCredential? Credential { get; set; }

    /// <summary>
    /// Force reinstall or update of the target version.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Allow command exports to overlap with other modules in the target root.
    /// </summary>
    public bool AllowClobber { get; set; }

    /// <summary>
    /// Accept package licenses when required.
    /// </summary>
    public bool AcceptLicense { get; set; }

    /// <summary>
    /// Skip package dependency installation.
    /// </summary>
    public bool SkipDependencyCheck { get; set; }

    /// <summary>
    /// Number of times this scenario should be measured.
    /// </summary>
    public int Iterations { get; set; } = 1;
}
