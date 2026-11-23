namespace PowerForge;

/// <summary>
/// Non-mutating plan for a managed module update request.
/// </summary>
public sealed class ManagedModuleUpdatePlan
{
    /// <summary>
    /// Module or package id.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Version selected by the planner.
    /// </summary>
    public string TargetVersion { get; set; } = string.Empty;

    /// <summary>
    /// Highest installed version in the inspected scope.
    /// </summary>
    public string? PreviousVersion { get; set; }

    /// <summary>
    /// All installed versions discovered in the inspected scope.
    /// </summary>
    public IReadOnlyList<string> InstalledVersions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Planned update action.
    /// </summary>
    public ManagedModuleUpdatePlanAction Action { get; set; }

    /// <summary>
    /// Repository name used by the plan.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Repository source used by the plan.
    /// </summary>
    public string RepositorySource { get; set; } = string.Empty;

    /// <summary>
    /// Module root that was inspected.
    /// </summary>
    public string ModuleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Versioned module directory that would be installed or inspected.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

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
    /// True when mutating update delivery would require Authenticode validation before promotion.
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
    /// True when installed source evidence satisfies the requested source policy.
    /// </summary>
    public bool SourcePolicySatisfied { get; set; } = true;

    /// <summary>
    /// Diagnostic reason when source evidence does not satisfy policy.
    /// </summary>
    public string? SourcePolicyReason { get; set; }

    /// <summary>
    /// Managed receipt discovered for the installed version, when available.
    /// </summary>
    public ManagedModuleReceipt? InstalledReceipt { get; set; }

    /// <summary>
    /// Planned actions for related installed modules covered by the family policy.
    /// </summary>
    public IReadOnlyList<ManagedModuleFamilyUpdatePlanItem> FamilyActions { get; set; } = Array.Empty<ManagedModuleFamilyUpdatePlanItem>();

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
