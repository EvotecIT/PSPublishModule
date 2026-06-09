using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public interface IRepositoryGitQuickActionReceiptPersistenceService
{
    Task PersistAsync(
        string databasePath,
        RepositoryGitQuickActionReceipt receipt,
        CancellationToken cancellationToken = default);
}
