using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record PortfolioFocusOption(
    RepositoryPortfolioFocusMode Mode,
    string DisplayName,
    string Description);
