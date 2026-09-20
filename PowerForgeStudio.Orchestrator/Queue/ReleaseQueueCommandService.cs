using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseQueueCommandService : IReleaseQueueCommandService
{
    private readonly ReleaseQueueCommandStateService _commandStateService;
    private readonly ReleaseQueuePlanner _queuePlanner;
    private readonly ReleaseQueueRunner _queueRunner;
    private readonly IReleaseBuildExecutionService _buildExecutionService;
    private readonly IReleaseSigningExecutionService _signingExecutionService;
    private readonly IReleasePublishExecutionService _publishExecutionService;
    private readonly IReleaseVerificationExecutionService _verificationExecutionService;

    public ReleaseQueueCommandService()
        : this(
            new ReleaseQueuePlanner(),
            new ReleaseQueueRunner(),
            new ReleaseBuildExecutionService(),
            new ReleaseSigningExecutionService(),
            new ReleasePublishExecutionService(),
            new ReleaseVerificationExecutionService()) {
    }

    public ReleaseQueueCommandService(
        ReleaseQueuePlanner queuePlanner,
        ReleaseQueueRunner queueRunner,
        IReleaseBuildExecutionService buildExecutionService,
        IReleaseSigningExecutionService signingExecutionService,
        IReleasePublishExecutionService publishExecutionService,
        IReleaseVerificationExecutionService verificationExecutionService)
    {
        _commandStateService = new ReleaseQueueCommandStateService();
        _queuePlanner = queuePlanner;
        _queueRunner = queueRunner;
        _buildExecutionService = buildExecutionService;
        _signingExecutionService = signingExecutionService;
        _publishExecutionService = publishExecutionService;
        _verificationExecutionService = verificationExecutionService;
    }

    public async Task<ReleaseQueueCommandResult> RunNextReadyItemAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        var stateDatabase = await _commandStateService.OpenDatabaseAsync(databasePath, cancellationToken).ConfigureAwait(false);

        var currentSession = await stateDatabase.LoadLatestQueueSessionAsync(cancellationToken).ConfigureAwait(false);
        if (currentSession is null)
        {
            return _commandStateService.EmptyResult("Queue state is not available yet. Prepare the queue first.");
        }

        var nextReadyItem = currentSession.Items.FirstOrDefault(item => item.Status == ReleaseQueueItemStatus.ReadyToRun);
        if (nextReadyItem is null)
        {
            return await _commandStateService.LoadResultAsync(
                stateDatabase,
                currentSession,
                changed: false,
                message: "No queue item is currently ready to run.",
                cancellationToken).ConfigureAwait(false);
        }

        ReleaseQueueTransitionResult transition;
        IReadOnlyList<ReleasePublishReceipt>? publishReceipts = null;
        IReadOnlyList<ReleaseVerificationReceipt>? verificationReceipts = null;
        if (nextReadyItem.Stage == ReleaseQueueStage.Build)
        {
            var buildResult = await _buildExecutionService.ExecuteAsync(nextReadyItem.RootPath, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) buildResult = buildResult with { Succeeded = false, Summary = "Build was cancelled; completed artifact evidence retained." };
            transition = buildResult.Succeeded
                ? _queueRunner.CompleteBuild(currentSession, nextReadyItem.RootPath, buildResult)
                : _queueRunner.FailBuild(currentSession, nextReadyItem.RootPath, buildResult);
        }
        else if (nextReadyItem.Stage == ReleaseQueueStage.Publish)
        {
            var publishResult = await _publishExecutionService.ExecuteAsync(nextReadyItem, cancellationToken).ConfigureAwait(false);
            publishReceipts = publishResult.Receipts;
            transition = publishResult.Succeeded
                ? _queueRunner.CompletePublish(currentSession, nextReadyItem.RootPath, publishResult)
                : _queueRunner.FailPublish(currentSession, nextReadyItem.RootPath, publishResult);
        }
        else if (nextReadyItem.Stage == ReleaseQueueStage.Verify)
        {
            var verificationResult = await _verificationExecutionService.ExecuteAsync(nextReadyItem, cancellationToken).ConfigureAwait(false);
            verificationReceipts = verificationResult.Receipts;
            transition = verificationResult.Succeeded
                ? _queueRunner.CompleteVerification(currentSession, nextReadyItem.RootPath, verificationResult)
                : _queueRunner.FailVerification(currentSession, nextReadyItem.RootPath, verificationResult);
        }
        else
        {
            transition = _queueRunner.AdvanceNextReadyItem(currentSession);
        }

        // Once an executor returns evidence, cancellation must not discard its local receipts/checkpoint.
        using var finalization = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (transition.Changed)
            await stateDatabase.PersistReleaseCheckpointAsync(transition.Session, publishReceipts: publishReceipts,
                verificationReceipts: verificationReceipts, cancellationToken: finalization.Token, receiptRootPath: nextReadyItem.RootPath).ConfigureAwait(false);
        return await _commandStateService.LoadResultAsync(stateDatabase, transition.Session, transition.Changed, transition.Message, finalization.Token).ConfigureAwait(false);
    }

    public async Task<ReleaseQueueCommandResult> ApproveUsbAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        var stateDatabase = await _commandStateService.OpenDatabaseAsync(databasePath, cancellationToken).ConfigureAwait(false);

        var currentSession = await stateDatabase.LoadLatestQueueSessionAsync(cancellationToken).ConfigureAwait(false);
        if (currentSession is null)
        {
            return _commandStateService.EmptyResult("Queue state is not available yet. Prepare the queue first.");
        }

        var waitingItem = currentSession.Items.FirstOrDefault(item => item.Stage == ReleaseQueueStage.Sign && item.Status == ReleaseQueueItemStatus.WaitingApproval);
        if (waitingItem is null)
        {
            return await _commandStateService.LoadResultAsync(
                stateDatabase,
                currentSession,
                changed: false,
                message: "No queue item is currently waiting on USB approval.",
                cancellationToken).ConfigureAwait(false);
        }

        var signingResult = await _signingExecutionService.ExecuteAsync(waitingItem, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested) signingResult = signingResult with { Succeeded = false, RequiresRebuild = true, Summary = "Signing was cancelled; completed artifact receipts retained. Rebuild before retrying." };
        using var finalization = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var transition = signingResult.Succeeded
            ? _queueRunner.CompleteSigning(currentSession, waitingItem.RootPath, signingResult)
            : _queueRunner.FailSigning(currentSession, waitingItem.RootPath, signingResult);

        if (transition.Changed)
            await stateDatabase.PersistReleaseCheckpointAsync(transition.Session, signingReceipts: signingResult.Receipts,
                cancellationToken: finalization.Token, receiptRootPath: waitingItem.RootPath).ConfigureAwait(false);
        return await _commandStateService.LoadResultAsync(stateDatabase, transition.Session, transition.Changed, transition.Message, finalization.Token).ConfigureAwait(false);
    }

    public Task<ReleaseQueueCommandResult> RetryFailedAsync(string databasePath, CancellationToken cancellationToken = default)
        => UpdateQueueAsync(databasePath, _queueRunner.RetryFailedItem, cancellationToken);

    public Task<ReleaseQueueCommandResult> RetryFailedAsync(
        string databasePath,
        Func<ReleaseQueueItem, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return UpdateQueueAsync(databasePath, session => _queueRunner.RetryFailedItems(session, predicate), cancellationToken);
    }

    public async Task<ReleaseQueueCommandResult> PrepareQueueAsync(
        string databasePath,
        string workspaceRoot,
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        string? scopeKey = null,
        string? scopeDisplayName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(portfolioItems);

        if (portfolioItems.Count == 0)
        {
            return _commandStateService.EmptyResult("No portfolio items are currently available for queue preparation.");
        }

        var stateDatabase = await _commandStateService.OpenDatabaseAsync(databasePath, cancellationToken).ConfigureAwait(false);

        var queueSession = _queuePlanner.CreateDraftQueue(
            workspaceRoot,
            portfolioItems,
            scopeKey,
            scopeDisplayName);

        await stateDatabase.PersistQueueSessionAsync(queueSession, cancellationToken).ConfigureAwait(false);
        return await _commandStateService.LoadResultAsync(
            stateDatabase,
            queueSession,
            changed: true,
            message: string.IsNullOrWhiteSpace(scopeDisplayName)
                ? $"Prepared a queue with {portfolioItems.Count} repository row(s)."
                : $"Prepared a family-scoped queue for {scopeDisplayName} with {portfolioItems.Count} repository row(s).",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReleaseQueueCommandResult> UpdateQueueAsync(
        string databasePath,
        Func<ReleaseQueueSession, ReleaseQueueTransitionResult> transition,
        CancellationToken cancellationToken)
    {
        var stateDatabase = await _commandStateService.OpenDatabaseAsync(databasePath, cancellationToken).ConfigureAwait(false);

        var currentSession = await stateDatabase.LoadLatestQueueSessionAsync(cancellationToken).ConfigureAwait(false);
        if (currentSession is null)
        {
            return _commandStateService.EmptyResult("Queue state is not available yet. Prepare the queue first.");
        }

        var result = transition(currentSession);
        return await _commandStateService.PersistTransitionResultAsync(stateDatabase, currentSession, result, cancellationToken).ConfigureAwait(false);
    }
}
