using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseQueueCommandStateService
{
    public async Task<ReleaseStateDatabase> OpenDatabaseAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        await stateDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return stateDatabase;
    }

    public async Task<ReleaseQueueCommandResult> PersistTransitionResultAsync(
        ReleaseStateDatabase stateDatabase,
        ReleaseQueueSession currentSession,
        ReleaseQueueTransitionResult transition,
        CancellationToken cancellationToken = default)
    {
        if (!transition.Changed)
        {
            return await LoadResultAsync(stateDatabase, currentSession, false, transition.Message, cancellationToken).ConfigureAwait(false);
        }

        await stateDatabase.PersistQueueSessionAsync(transition.Session, cancellationToken).ConfigureAwait(false);
        return await LoadResultAsync(stateDatabase, transition.Session, true, transition.Message, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReleaseQueueCommandResult> LoadResultAsync(
        ReleaseStateDatabase stateDatabase,
        ReleaseQueueSession? fallbackSession,
        bool changed,
        string message,
        CancellationToken cancellationToken = default)
    {
        var persistedQueue = fallbackSession is null
            ? null
            : await stateDatabase.LoadLatestQueueSessionAsync(cancellationToken).ConfigureAwait(false) ?? fallbackSession;

        if (persistedQueue is null)
        {
            return EmptyResult(message);
        }

        var signingReceipts = await stateDatabase.LoadSigningReceiptsAsync(persistedQueue.SessionId, cancellationToken).ConfigureAwait(false);
        var publishReceipts = await stateDatabase.LoadPublishReceiptsAsync(persistedQueue.SessionId, cancellationToken).ConfigureAwait(false);
        var verificationReceipts = await stateDatabase.LoadVerificationReceiptsAsync(persistedQueue.SessionId, cancellationToken).ConfigureAwait(false);

        return new ReleaseQueueCommandResult(
            Changed: changed,
            Message: message,
            QueueSession: persistedQueue,
            SigningReceipts: signingReceipts,
            PublishReceipts: publishReceipts,
            VerificationReceipts: verificationReceipts);
    }

    public ReleaseQueueCommandResult EmptyResult(string message)
        => new(
            Changed: false,
            Message: message,
            QueueSession: null,
            SigningReceipts: [],
            PublishReceipts: [],
            VerificationReceipts: []);
}
