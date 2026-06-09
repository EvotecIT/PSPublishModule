using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record PortfolioQuickPreset(
    string Key,
    string DisplayName,
    RepositoryPortfolioFocusMode FocusMode,
    string SearchText,
    string Description);
