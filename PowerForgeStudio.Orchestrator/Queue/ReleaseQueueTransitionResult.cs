using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleaseQueueTransitionResult(
    ReleaseQueueSession Session,
    bool Changed,
    string Message);
