using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public interface IPortfolioViewStatePersistenceService
{
    Task PersistAsync(
        string databasePath,
        RepositoryPortfolioViewState state,
        string viewId = "default",
        string? displayName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryPortfolioSavedView>> ListSavedViewsAsync(
        string databasePath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string databasePath,
        string viewId,
        CancellationToken cancellationToken = default);
}
