using PowerForgeStudio.Domain.Publish;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleasePublishExecutionResult(
    string RootPath,
    bool Succeeded,
    string Summary,
    string? SourceCheckpointStateJson,
    IReadOnlyList<ReleasePublishReceipt> Receipts)
{
    /// <summary>The operation ended after cancellation was requested; retained receipts describe completed work.</summary>
    public bool WasCancelled { get; init; }
    /// <summary>Remote completion is uncertain or partial; whole-stage replay must remain blocked until reconciliation.</summary>
    public bool RequiresReconciliation { get; init; }
}
