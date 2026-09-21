namespace PowerForgeStudio.Domain.Workspace;

/// <summary>A process observed by a bounded working-copy lock inspection.</summary>
public sealed record WorkspaceExternalProcess(int ProcessId, string Name)
{
    public string Display => string.IsNullOrWhiteSpace(Name) ? $"PID {ProcessId}" : $"{Name} (PID {ProcessId})";
}

/// <summary>Best-effort operating-system evidence about open files in a working copy.</summary>
public sealed record WorkspaceExternalUseEvidence(
    bool IsAvailable,
    int ResourceCount,
    IReadOnlyList<WorkspaceExternalProcess> Processes,
    string? Warning)
{
    public bool HasDetectedProcesses => Processes.Count > 0;
    public string Display => HasDetectedProcesses
        ? $"{Processes.Count} open-handle process(es)"
        : IsAvailable ? $"No open handles across {ResourceCount} sampled file(s)" : "Automatic scan unavailable";
}
