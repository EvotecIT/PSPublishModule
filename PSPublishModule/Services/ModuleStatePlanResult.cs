namespace PSPublishModule;

/// <summary>
/// PowerShell-facing module-state plan result.
/// </summary>
public sealed class ModuleStatePlanResult
{
    /// <summary>
    /// Gets or sets the path to the inventory JSON used for the plan.
    /// </summary>
    public string InventoryPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path to the desired-state JSON used for the plan.
    /// </summary>
    public string DesiredStatePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maintenance receipt JSON files used for drift checks.
    /// </summary>
    public string[] MaintenanceReceiptPaths { get; set; } = [];

    /// <summary>
    /// Gets or sets whether any finding in the plan is an error.
    /// </summary>
    public bool HasErrors { get; set; }

    /// <summary>
    /// Gets or sets the module actions proposed by the plan.
    /// </summary>
    public ModuleStatePlanActionResult[] Actions { get; set; } = [];

    /// <summary>
    /// Gets or sets conflict findings produced while building the plan.
    /// </summary>
    public ModuleStateConflictFindingResult[] Findings { get; set; } = [];
}

/// <summary>
/// PowerShell-facing module-state plan action.
/// </summary>
public sealed class ModuleStatePlanActionResult
{
    /// <summary>
    /// Gets or sets the action kind.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the module name.
    /// </summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the installed module version, when one was found.
    /// </summary>
    public string? InstalledVersion { get; set; }

    /// <summary>
    /// Gets or sets the desired version policy.
    /// </summary>
    public string VersionPolicy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the reason this action was selected.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the action repairs drift from maintenance receipt evidence.
    /// </summary>
    public bool IsRepair { get; set; }

    /// <summary>
    /// Gets or sets whether this action must force reinstall even when a matching version exists.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Gets or sets whether prerelease versions may satisfy this action.
    /// </summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>
    /// Gets or sets whether license acceptance was supplied for this action.
    /// </summary>
    public bool AcceptLicense { get; set; }

    /// <summary>
    /// Gets or sets whether managed delivery may overwrite exported command conflicts for this action.
    /// </summary>
    public bool AllowClobber { get; set; }

    /// <summary>
    /// Gets or sets whether dependency installation should be skipped for this action.
    /// </summary>
    public bool SkipDependencyCheck { get; set; }

    /// <summary>
    /// Gets or sets the scope targeted by this action when applicable.
    /// </summary>
    public string? TargetScope { get; set; }

    /// <summary>
    /// Gets or sets the filesystem path targeted by this action when applicable.
    /// </summary>
    public string? TargetPath { get; set; }

    /// <summary>
    /// Gets or sets the repository targeted by this action when applicable.
    /// </summary>
    public string? TargetRepository { get; set; }

    /// <summary>
    /// Gets or sets the repository source used for delivery when it differs from the normalized target repository identity.
    /// </summary>
    public string? TargetRepositorySource { get; set; }

    /// <summary>
    /// Gets or sets the expected SHA256 hash for the root package when package integrity is required.
    /// </summary>
    public string? ExpectedPackageSha256 { get; set; }

    /// <summary>
    /// Gets or sets the selected package license when repository metadata exposes it.
    /// </summary>
    public string? License { get; set; }

    /// <summary>
    /// Gets or sets whether this planned package requires explicit license acceptance.
    /// </summary>
    public bool LicenseAcceptanceRequired { get; set; }

    /// <summary>
    /// Gets or sets whether license acceptance was supplied for this planned action.
    /// </summary>
    public bool LicenseAccepted { get; set; }
}

/// <summary>
/// PowerShell-facing module-state conflict finding.
/// </summary>
public sealed class ModuleStateConflictFindingResult
{
    /// <summary>
    /// Gets or sets the finding severity.
    /// </summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stable finding code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable finding message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the module family name associated with the finding.
    /// </summary>
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the module names associated with the finding.
    /// </summary>
    public string[] ModuleNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the versions associated with the finding.
    /// </summary>
    public string[] Versions { get; set; } = [];
}
