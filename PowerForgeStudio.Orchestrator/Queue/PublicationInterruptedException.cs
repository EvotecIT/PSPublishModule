using PowerForgeStudio.Domain.Publish;

namespace PowerForgeStudio.Orchestrator.Queue;

/// <summary>Carries completed adapter receipts to the execution boundary when a later operation throws.</summary>
internal sealed class PublicationInterruptedException(IReadOnlyList<ReleasePublishReceipt> receipts, Exception cause)
    : Exception("Publication interrupted; partial receipts are available.", cause)
{
    public IReadOnlyList<ReleasePublishReceipt> Receipts { get; } = receipts.ToArray();
}
