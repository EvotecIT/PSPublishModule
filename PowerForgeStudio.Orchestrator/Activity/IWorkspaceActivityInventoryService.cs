using PowerForgeStudio.Domain.Activity;

namespace PowerForgeStudio.Orchestrator.Activity;

/// <summary>Projects existing local, GitHub, release, and automation owners into one read-only inbox.</summary>
public interface IWorkspaceActivityInventoryService
{
    Task<WorkspaceActivitySnapshot> InspectAsync(
        string workspaceRoot,
        WorkspaceActivityOptions? options = null,
        CancellationToken cancellationToken = default);
}
