using PowerForgeStudio.Domain.Automation;

namespace PowerForgeStudio.Orchestrator.Automation;

/// <summary>Reads automation definitions and provider-owned runtime evidence without mutating schedules.</summary>
public interface IWorkspaceAutomationInventoryService
{
    Task<WorkspaceAutomationSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default);
}
