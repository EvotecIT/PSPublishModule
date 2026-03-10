using ReleaseOpsStudio.Domain.Signing;

namespace ReleaseOpsStudio.Orchestrator.Queue;

public sealed record ReleaseSigningExecutionResult(
    string RootPath,
    bool Succeeded,
    string Summary,
    string? SourceCheckpointStateJson,
    IReadOnlyList<ReleaseSigningReceipt> Receipts);
