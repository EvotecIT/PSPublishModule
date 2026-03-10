using ReleaseOpsStudio.Domain.Publish;

namespace ReleaseOpsStudio.Orchestrator.Queue;

public sealed record ReleasePublishExecutionResult(
    string RootPath,
    bool Succeeded,
    string Summary,
    string? SourceCheckpointStateJson,
    IReadOnlyList<ReleasePublishReceipt> Receipts);
