using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class RepositoryGitQuickActionReceiptPersistenceService : IRepositoryGitQuickActionReceiptPersistenceService
{
    public async Task PersistAsync(
        string databasePath,
        RepositoryGitQuickActionReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(receipt);

        await using (await ReleaseStateDatabase.AcquireExclusiveAccessAsync(databasePath, cancellationToken).ConfigureAwait(false))
        {
            var stateDatabase = new ReleaseStateDatabase(databasePath);
            await stateDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await stateDatabase.PersistGitQuickActionReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);
        }
    }
}
