using PowerForgeStudio.Domain.Verification;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleaseVerificationExecutionResult(
    string RootPath,
    bool Succeeded,
    string Summary,
    string? SourceCheckpointStateJson,
    IReadOnlyList<ReleaseVerificationReceipt> Receipts)
{
    /// <summary>True when cancellation prevented completion of all requested checks.</summary>
    public bool WasCancelled { get; init; }
}
