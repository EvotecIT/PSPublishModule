using System.Linq;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// PowerShell-facing module-state test result.
/// </summary>
public sealed class ModuleStateTestResult
{
    /// <summary>
    /// Gets or sets whether the tested module state satisfies the desired state without error findings or required actions.
    /// </summary>
    public bool IsCompliant { get; set; }

    /// <summary>
    /// Gets or sets the number of plan actions that require a machine change.
    /// </summary>
    public int RequiredActionCount { get; set; }

    /// <summary>
    /// Gets or sets the number of error findings.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Gets or sets the underlying module-state plan.
    /// </summary>
    public ModuleStatePlanResult Plan { get; set; } = new();

    internal static ModuleStateTestResult FromPlan(ModuleStatePlanResult plan)
    {
        var requiredActionCount = plan.Actions.Count(static action => action.Kind != nameof(ModuleStatePlanActionKind.NoAction));
        var errorCount = plan.Findings.Count(static finding => finding.Severity == nameof(ModuleStateConflictSeverity.Error));
        return new ModuleStateTestResult
        {
            Plan = plan,
            RequiredActionCount = requiredActionCount,
            ErrorCount = errorCount,
            IsCompliant = requiredActionCount == 0 && errorCount == 0
        };
    }
}
