namespace PSPublishModule;

/// <summary>
/// PowerShell-facing module-state apply and execution result.
/// </summary>
public sealed class ModuleStateApplyResult
{
    /// <summary>
    /// Gets or sets whether the prepared plan can be applied or executed.
    /// </summary>
    public bool CanApply { get; set; }

    /// <summary>
    /// Gets or sets the reason the plan is blocked, when applicable.
    /// </summary>
    public string? BlockedReason { get; set; }

    /// <summary>
    /// Gets or sets the path where the receipt was written.
    /// </summary>
    public string? ReceiptPath { get; set; }

    /// <summary>
    /// Gets or sets the path where the maintenance receipt was written.
    /// </summary>
    public string? MaintenanceReceiptOutputPath { get; set; }

    /// <summary>
    /// Gets or sets the number of actionable install, update, save, or cleanup operations.
    /// </summary>
    public int ActionCount { get; set; }

    /// <summary>
    /// Gets or sets the number of findings in the source plan.
    /// </summary>
    public int FindingCount { get; set; }

    /// <summary>
    /// Gets or sets the private-module delivery commands prepared from the plan.
    /// </summary>
    public ModuleStateDeliveryCommandResult[] Commands { get; set; } = [];

    /// <summary>
    /// Gets or sets whether managed repair execution was requested.
    /// </summary>
    public bool ExecutionRequested { get; set; }

    /// <summary>
    /// Gets or sets delivery and exact-path cleanup results when execution was requested.
    /// </summary>
    public ModuleStateDeliveryExecutionResult[] ExecutionResults { get; set; } = [];

    /// <summary>
    /// Gets or sets post-apply inventory evidence collected from the same physical roots after execution.
    /// </summary>
    public ModuleStateInventoryResult? PostApplyInventory { get; set; }

    /// <summary>
    /// Gets or sets the plan produced from post-apply inventory evidence.
    /// </summary>
    public ModuleStatePlanResult? PostApplyPlan { get; set; }

    /// <summary>
    /// Gets or sets the compliance result produced from post-apply inventory evidence.
    /// </summary>
    public ModuleStateTestResult? PostApplyTest { get; set; }

    /// <summary>
    /// Gets or sets whether every requested execution operation completed without an operational failure.
    /// </summary>
    public bool ExecutionSucceeded { get; set; }

    /// <summary>
    /// Gets or sets whether post-apply inventory converged to a compliant plan.
    /// </summary>
    public bool Converged { get; set; }
}

/// <summary>
/// PowerShell-facing private-module delivery command prepared from a module-state plan.
/// </summary>
public sealed class ModuleStateDeliveryCommandResult
{
    /// <summary>
    /// Gets or sets the originating plan action kind.
    /// </summary>
    public string ActionKind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the module name.
    /// </summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version policy from the originating plan action.
    /// </summary>
    public string VersionPolicy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the command came from a repair action.
    /// </summary>
    public bool IsRepair { get; set; }

    /// <summary>
    /// Gets or sets whether the prepared delivery command will force replacement or reinstall behavior.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Gets or sets the private-module command name.
    /// </summary>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets command arguments as structured values.
    /// </summary>
    public string[] Arguments { get; set; } = [];

    /// <summary>
    /// Gets or sets a display-ready PowerShell command.
    /// </summary>
    public string CommandText { get; set; } = string.Empty;
}

/// <summary>
/// PowerShell-facing result for one private-module delivery workflow invocation.
/// </summary>
public sealed class ModuleStateDeliveryExecutionResult
{
    /// <summary>
    /// Gets or sets whether the operation completed without an operational failure.
    /// </summary>
    public bool Succeeded { get; set; } = true;

    /// <summary>
    /// Gets or sets the operational failure message when the operation did not succeed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the managed delivery or cleanup operation.
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the workflow performed the operation.
    /// </summary>
    public bool OperationPerformed { get; set; }

    /// <summary>
    /// Gets or sets the exact filesystem target associated with the operation when applicable.
    /// </summary>
    public string? TargetPath { get; set; }

    /// <summary>
    /// Gets or sets the repository name used by the workflow.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the transport requested by the caller or plan execution options.
    /// </summary>
    public PowerForge.ModuleStateDeliveryTransport RequestedTransport { get; set; } = PowerForge.ModuleStateDeliveryTransport.PrivateModule;

    /// <summary>
    /// Gets or sets the transport actually used by the workflow.
    /// </summary>
    public PowerForge.ModuleStateDeliveryTransport EffectiveTransport { get; set; } = PowerForge.ModuleStateDeliveryTransport.PrivateModule;

    /// <summary>
    /// Gets or sets the reason the workflow selected the effective transport.
    /// </summary>
    public string DeliveryTransportReason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets dependency results returned by the private-module workflow.
    /// </summary>
    public ModuleStateDependencyResult[] DependencyResults { get; set; } = [];
}

/// <summary>
/// PowerShell-facing dependency result from private-module delivery.
/// </summary>
public sealed class ModuleStateDependencyResult
{
    /// <summary>
    /// Gets or sets the module name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the installed version before the operation.
    /// </summary>
    public string? InstalledVersion { get; set; }

    /// <summary>
    /// Gets or sets the resolved version after the operation.
    /// </summary>
    public string? ResolvedVersion { get; set; }

    /// <summary>
    /// Gets or sets the requested version constraint.
    /// </summary>
    public string? RequestedVersion { get; set; }

    /// <summary>
    /// Gets or sets the dependency status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the installer used by the workflow.
    /// </summary>
    public string? Installer { get; set; }

    /// <summary>
    /// Gets or sets an optional message.
    /// </summary>
    public string? Message { get; set; }
}
