namespace ReleaseOpsStudio.Orchestrator.Queue;

public sealed record ReleaseBuildExecutionResult(
    string RootPath,
    bool Succeeded,
    string Summary,
    double DurationSeconds,
    IReadOnlyList<ReleaseBuildAdapterResult> AdapterResults);
