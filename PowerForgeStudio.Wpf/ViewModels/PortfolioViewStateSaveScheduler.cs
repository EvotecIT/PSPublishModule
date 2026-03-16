using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class PortfolioViewStateSaveScheduler
{
    private readonly IPortfolioViewStatePersistenceService _persistenceService;
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _saveCts;
    private Task? _pendingSaveTask;

    public PortfolioViewStateSaveScheduler(
        IPortfolioViewStatePersistenceService persistenceService,
        TimeSpan? delay = null)
    {
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _delay = delay ?? TimeSpan.FromMilliseconds(350);
    }

    public void Schedule(string databasePath, RepositoryPortfolioViewState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(state);

        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = new CancellationTokenSource();

        _pendingSaveTask = PersistAsync(databasePath, state, _saveCts.Token);
    }

    public IPortfolioViewStatePersistenceService PersistenceService => _persistenceService;

    public async Task FlushAsync()
    {
        var pendingSaveTask = _pendingSaveTask;
        if (pendingSaveTask is null)
        {
            return;
        }

        try
        {
            await pendingSaveTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer save superseded the pending one.
        }
        finally
        {
            if (ReferenceEquals(_pendingSaveTask, pendingSaveTask))
            {
                _pendingSaveTask = null;
            }
        }
    }

    private async Task PersistAsync(
        string databasePath,
        RepositoryPortfolioViewState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            await _persistenceService.PersistAsync(databasePath, state, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer triage-state change.
        }
    }
}
