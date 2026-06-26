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
