using PowerForgeStudio.Domain.Verification;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleaseVerificationExecutionResult(
    string RootPath,
    bool Succeeded,
    string Summary,
    string? SourceCheckpointStateJson,
    IReadOnlyList<ReleaseVerificationReceipt> Receipts);
