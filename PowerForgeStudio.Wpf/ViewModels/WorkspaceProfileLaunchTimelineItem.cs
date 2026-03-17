namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record WorkspaceProfileLaunchTimelineItem(
    string Title,
    string StatusLabel,
    string Summary,
    string ExecutedLabel,
    bool IsLatest);
