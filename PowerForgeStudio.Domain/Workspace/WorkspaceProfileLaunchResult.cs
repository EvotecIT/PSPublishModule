namespace PowerForgeStudio.Domain.Workspace;

public sealed record WorkspaceProfileLaunchResult(
    WorkspaceProfileLaunchActionKind ActionKind,
    string ActionTitle,
    bool Succeeded,
    string Summary,
    DateTimeOffset ExecutedAtUtc);
