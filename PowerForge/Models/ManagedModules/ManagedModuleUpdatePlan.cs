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
}
