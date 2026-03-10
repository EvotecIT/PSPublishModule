using ReleaseOpsStudio.Domain.Portfolio;

namespace ReleaseOpsStudio.Wpf.ViewModels;

public sealed record PortfolioQuickPreset(
    string Key,
    string DisplayName,
    RepositoryPortfolioFocusMode FocusMode,
    string SearchText,
    string Description);
