namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryDetailSnapshot(
    string RepositoryName,
    string RepositoryBadge,
    string ReadinessDisplay,
    string ReadinessReason,
    string RootPath,
    string BranchDisplay,
    string GitDiagnosticsDisplay,
    string GitDiagnosticsDetail,
    IReadOnlyList<RepositoryGitRemediationStep> GitRemediationSteps,
    IReadOnlyList<RepositoryGitQuickAction> GitQuickActions,
    string LastGitActionDisplay,
    string LastGitActionSummary,
    string LastGitActionOutput,
    string LastGitActionError,
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

