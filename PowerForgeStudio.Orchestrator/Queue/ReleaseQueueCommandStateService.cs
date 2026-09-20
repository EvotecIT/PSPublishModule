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
        var snapshot = fallbackSession is null ? null
            : await stateDatabase.LoadReleaseCheckpointAsync(fallbackSession.SessionId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null) return EmptyResult(message);

        return new ReleaseQueueCommandResult(
            Changed: changed,
            Message: message,
            QueueSession: snapshot.Session,
            SigningReceipts: snapshot.SigningReceipts,
            PublishReceipts: snapshot.PublishReceipts,
            VerificationReceipts: snapshot.VerificationReceipts);
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
