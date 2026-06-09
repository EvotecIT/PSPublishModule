using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Domain.Workspace;

public sealed record PortfolioDashboardSnapshot(
    string Key,
    string Title,
    string CountDisplay,
    string Detail,
    RepositoryPortfolioFocusMode FocusMode,
    string SearchText = "",
    string? PresetKey = null);
