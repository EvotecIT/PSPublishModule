namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleaseBuildAdapterResult(
    ReleaseBuildAdapterKind AdapterKind,
    bool Succeeded,
    string Summary,
    int ExitCode,
    double DurationSeconds,
    IReadOnlyList<string> ArtifactDirectories,
    IReadOnlyList<string> ArtifactFiles,
    string? OutputTail = null,
    string? ErrorTail = null);
