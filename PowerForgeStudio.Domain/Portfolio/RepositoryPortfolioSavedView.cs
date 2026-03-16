namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryPortfolioSavedView(
    string ViewId,
    string DisplayName,
    RepositoryPortfolioViewState State);
