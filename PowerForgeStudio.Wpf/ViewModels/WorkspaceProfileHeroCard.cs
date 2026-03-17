namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record WorkspaceProfileHeroCard(
    string ProfileId,
    string DisplayName,
    string Description,
    string TodayNote,
    string HealthLabel,
    string HealthDetails,
    WorkspaceProfileHeroRouteKind RouteKind,
    string RouteLabel,
    string RouteDetails,
    string WorkspaceLabel,
    string SavedViewLabel,
    string StartupStrategyLabel,
    string QueueScopeLabel,
    string StatusLabel,
    bool IsActive);
