using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record PortfolioSavedViewItem(
    string ViewId,
    string DisplayName,
    string Summary,
    string UpdatedAtDisplay,
    RepositoryPortfolioViewState State);
