namespace PSPublishModule;

/// <summary>
/// PowerShell-facing result for a complete module-state management workflow.
/// </summary>
public sealed class ModuleStateWorkflowResult
{
    /// <summary>
    /// Gets or sets the inventory used by the workflow.
    /// </summary>
    public ModuleStateInventoryResult Inventory { get; set; } = new();

    /// <summary>
    /// Gets or sets the plan produced by the workflow.
    /// </summary>
    public ModuleStatePlanResult Plan { get; set; } = new();

    /// <summary>
    /// Gets or sets the compliance result for the plan.
    /// </summary>
    public ModuleStateTestResult Test { get; set; } = new();

    /// <summary>
    /// Gets or sets the prepared or executed apply result.
    /// </summary>
    public ModuleStateApplyResult Apply { get; set; } = new();
}
