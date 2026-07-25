namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleaseBuildExecutionResult(
    string RootPath,
    bool Succeeded,
    string Summary,
    double DurationSeconds,
    IReadOnlyList<ReleaseBuildAdapterResult> AdapterResults,
    string? UnifiedReleaseStateJson = null,
    string? UnifiedReleaseConfigSha256 = null);
