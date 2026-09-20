using PowerForgeStudio.Domain.Connections;

namespace PowerForgeStudio.Orchestrator.Connections;

internal interface IWorkspaceConnectionSource
{
    string Provider { get; }
    Task<ConnectionSourceResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken);
}

internal sealed record ConnectionSourceResult(
    IReadOnlyList<WorkspaceConnectionEntry> Entries,
    WorkspaceConnectionSourceState State);
