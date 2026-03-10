namespace ReleaseOpsStudio.Domain.Portfolio;

public sealed record RepositoryDetailSnapshot(
    string RepositoryName,
    string RepositoryBadge,
    string ReadinessDisplay,
    string ReadinessReason,
    string RootPath,
    string BranchDisplay,
    string BuildContractDisplay,
    string QueueLaneDisplay,
    string QueueCheckpointDisplay,
    string QueueSummary,
    string QueueCheckpointPayload,
    string ReleaseDriftDisplay,
    string ReleaseDriftDetail,
    string BuildEngineDisplay,
    string BuildEnginePath,
    string BuildEngineAdvisory,
    IReadOnlyList<RepositoryAdapterEvidence> AdapterEvidence);
