using PowerForgeStudio.Domain.Signing;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleaseSigningExecutionResult(
    string RootPath,
    bool Succeeded,
    string Summary,
    string? SourceCheckpointStateJson,
    IReadOnlyList<ReleaseSigningReceipt> Receipts)
{
    /// <summary>Signing may have changed artifacts without completing; retry must rebuild before signing again.</summary>
    public bool RequiresRebuild { get; init; }
}
