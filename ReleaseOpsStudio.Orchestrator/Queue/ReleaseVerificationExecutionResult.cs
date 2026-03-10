using ReleaseOpsStudio.Domain.Verification;

namespace ReleaseOpsStudio.Orchestrator.Queue;

public sealed record ReleaseVerificationExecutionResult(
    string RootPath,
    bool Succeeded,
    string Summary,
    string? SourceCheckpointStateJson,
    IReadOnlyList<ReleaseVerificationReceipt> Receipts);
