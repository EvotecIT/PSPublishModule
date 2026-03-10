using ReleaseOpsStudio.Domain.Queue;

namespace ReleaseOpsStudio.Orchestrator.Queue;

public sealed record ReleaseQueueTransitionResult(
    ReleaseQueueSession Session,
    bool Changed,
    string Message);
