using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class PortfolioViewStatePersistenceService : IPortfolioViewStatePersistenceService
{
    public async Task PersistAsync(
        string databasePath,
        RepositoryPortfolioViewState state,
        string viewId = "default",
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        await stateDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await stateDatabase.PersistPortfolioViewStateAsync(state, viewId, displayName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RepositoryPortfolioSavedView>> ListSavedViewsAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        await stateDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await stateDatabase.LoadSavedPortfolioViewsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string databasePath,
        string viewId,
        CancellationToken cancellationToken = default)
    {
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        await stateDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await stateDatabase.DeletePortfolioViewStateAsync(viewId, cancellationToken).ConfigureAwait(false);
    }
}
