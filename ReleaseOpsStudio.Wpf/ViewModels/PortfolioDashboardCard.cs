using ReleaseOpsStudio.Domain.Portfolio;

namespace ReleaseOpsStudio.Wpf.ViewModels;

public sealed record PortfolioDashboardCard(
    string Key,
    string Title,
    string CountDisplay,
    string Detail,
    RepositoryPortfolioFocusMode FocusMode,
    string SearchText = "",
    string? PresetKey = null);
