using PowerForgeStudio.Domain.Connections;

namespace PowerForgeStudio.Orchestrator.Connections;

/// <summary>Reads secret-free capability and availability evidence without changing provider configuration.</summary>
public interface IWorkspaceConnectionInventoryService
{
    Task<WorkspaceConnectionSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default);
}
