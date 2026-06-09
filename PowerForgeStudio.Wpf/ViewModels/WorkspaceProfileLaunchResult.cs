namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record WorkspaceProfileLaunchResult(
    WorkspaceProfileLaunchActionKind ActionKind,
    string ActionTitle,
    bool Succeeded,
    string Summary,
    DateTimeOffset ExecutedAtUtc);
