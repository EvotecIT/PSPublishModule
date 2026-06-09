namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record WorkspaceProfileLaunchAction(
    string ProfileId,
    WorkspaceProfileLaunchActionKind Kind,
    string Title,
    string Summary,
    string ExecuteLabel,
    bool IsPrimary,
    string? LastRunLabel = null,
    bool IsLastRun = false,
    bool CanExecute = true);
