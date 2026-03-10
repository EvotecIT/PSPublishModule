using ReleaseOpsStudio.Domain.Portfolio;

namespace ReleaseOpsStudio.Wpf.ViewModels;

public sealed record PortfolioFocusOption(
    RepositoryPortfolioFocusMode Mode,
    string DisplayName,
    string Description);
