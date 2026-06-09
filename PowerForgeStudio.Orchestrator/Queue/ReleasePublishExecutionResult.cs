using PowerForgeStudio.Domain.Publish;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleasePublishExecutionResult(
    string RootPath,
    bool Succeeded,
    string Summary,
    string? SourceCheckpointStateJson,
    IReadOnlyList<ReleasePublishReceipt> Receipts);
