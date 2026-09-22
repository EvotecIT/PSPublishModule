namespace PowerForgeStudio.Domain.Workspace;

/// <summary>Observed local changes in a registered working copy.</summary>
public sealed record WorkspaceWorkingCopyChange(string Path, int ChangeCount);

/// <summary>One bounded, read-only workspace Git observation.</summary>
public sealed record WorkspaceProjectChangeSnapshot(
    DateTimeOffset ObservedAtUtc,
    IReadOnlyDictionary<string, IReadOnlyList<WorkspaceWorkingCopyChange>> ChangedProjects,
    IReadOnlyList<string> UnavailableWorkingCopies);

/// <summary>Progress while checking repository groups for local changes.</summary>
public sealed record WorkspaceProjectChangeProgress(int CompletedProjects, int TotalProjects, string CurrentProject);
