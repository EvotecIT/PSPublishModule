namespace PowerForge;

/// <summary>
/// Non-mutating plan for a managed module install request.
/// </summary>
public sealed class ManagedModuleInstallPlan
{
    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Version selected by the planner.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Planned action.
    /// </summary>
    public ManagedModuleInstallPlanAction Action { get; set; }

    /// <summary>
    /// Repository name used by the plan.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Repository source used by the plan.
    /// </summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>
    /// Module root that would contain module folders.
    /// </summary>
    public string ModuleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Versioned module directory that would be installed or inspected.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// True when the operation would save a .nupkg file instead of materializing an unpacked module directory.
    /// </summary>
    public bool SaveAsNupkg { get; set; }

    /// <summary>
    /// True when the target module version already exists.
    /// </summary>
    public bool ExistingVersionFound { get; set; }

    /// <summary>
    /// True when the plan would write files if invoked.
    /// </summary>
    public bool WouldWriteFiles { get; set; }

    /// <summary>
    /// Exact version requested by the caller, when supplied.
    /// </summary>
    public string? RequestedVersion { get; set; }

    /// <summary>
    /// Minimum version requested by the caller, when supplied.
    /// </summary>
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// Maximum version requested by the caller, when supplied.
    /// </summary>
    public string? MaximumVersion { get; set; }

    /// <summary>
    /// NuGet-style version policy requested by the caller, when supplied.
    /// </summary>
    public string? VersionPolicy { get; set; }

    /// <summary>
    /// Expected SHA256 hash supplied by the caller, when package integrity verification was requested.
    /// </summary>
    public string? ExpectedPackageSha256 { get; set; }

    /// <summary>
    /// True when the mutating operation would require Authenticode validation before promotion.
    /// </summary>
    public bool AuthenticodeCheck { get; set; }

    /// <summary>
    /// True when the caller required the repository to be trusted.
    /// </summary>
    public bool RequireTrustedRepository { get; set; }

    /// <summary>
    /// Package authors allowed by the caller's trust policy.
    /// </summary>
    public IReadOnlyList<string> AllowedAuthors { get; set; } = Array.Empty<string>();

    /// <summary>
    /// License expression, license URL, or license file reference for the selected package when known.
    /// </summary>
    public string? License { get; set; }

    /// <summary>
    /// True when repository metadata indicates the selected package requires explicit license acceptance.
    /// </summary>
    public bool LicenseAcceptanceRequired { get; set; }

    /// <summary>
    /// True when the caller supplied license acceptance for this plan.
    /// </summary>
    public bool LicenseAccepted { get; set; }
}
