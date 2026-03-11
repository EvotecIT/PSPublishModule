using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleaseQueueCommandResult(
    bool Changed,
    string Message,
    ReleaseQueueSession? QueueSession,
    IReadOnlyList<ReleaseSigningReceipt> SigningReceipts,
    IReadOnlyList<ReleasePublishReceipt> PublishReceipts,
    IReadOnlyList<ReleaseVerificationReceipt> VerificationReceipts);
