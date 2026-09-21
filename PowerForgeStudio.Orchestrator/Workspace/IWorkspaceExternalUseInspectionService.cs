using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Inspects open file handles without reading process command lines or environment data.</summary>
public interface IWorkspaceExternalUseInspectionService
{
    Task<WorkspaceExternalUseEvidence> InspectAsync(string workingCopy, CancellationToken cancellationToken = default);
}
