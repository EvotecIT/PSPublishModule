using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseQueueRunner
{
    private readonly ReleaseQueueCheckpointSerializer _checkpointSerializer = new();
    private readonly ReleaseQueueItemTransitionFactory _itemTransitionFactory;

    public ReleaseQueueRunner()
    {
        _itemTransitionFactory = new ReleaseQueueItemTransitionFactory(_checkpointSerializer);
    }

    public ReleaseQueueTransitionResult AdvanceNextReadyItem(ReleaseQueueSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var nextEntry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(entry => entry.Item.Status == ReleaseQueueItemStatus.ReadyToRun);

        if (nextEntry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, "No queue item is currently ready to run.");
        }

        var item = nextEntry.Item;
        var timestamp = DateTimeOffset.UtcNow;

        ReleaseQueueItem updatedItem;
        string message;
        switch (item.Stage)
        {
            case ReleaseQueueStage.Build:
                updatedItem = _itemTransitionFactory.CreateTransition(
                    item,
                    fromStage: "Build",
                    targetStage: ReleaseQueueStage.Sign,
                    targetStatus: ReleaseQueueItemStatus.WaitingApproval,
                    summary: "Build stage completed in orchestration mode. USB signing approval is now required.",
                    checkpointKey: "sign.waiting.usb",
                    timestamp: timestamp);
                message = $"Advanced {item.RepositoryName} from Build to Sign and paused for USB approval.";
                break;
            case ReleaseQueueStage.Sign:
                return new ReleaseQueueTransitionResult(session, false, $"{item.RepositoryName} is marked Sign/ReadyToRun, but signing requires the USB approval gate before execution can continue.");
            case ReleaseQueueStage.Publish:
                updatedItem = _itemTransitionFactory.CreateTransition(
                    item,
                    fromStage: "Publish",
                    targetStage: ReleaseQueueStage.Verify,
                    targetStatus: ReleaseQueueItemStatus.ReadyToRun,
                    summary: "Publish step completed in orchestration mode. Verification is ready to run.",
                    checkpointKey: "verify.ready",
                    timestamp: timestamp);
                message = $"Advanced {item.RepositoryName} from Publish to Verify.";
                break;
            case ReleaseQueueStage.Verify:
                return new ReleaseQueueTransitionResult(session, false, $"{item.RepositoryName} needs verification evidence before the queue can be completed.");
            default:
                return new ReleaseQueueTransitionResult(session, false, $"{item.RepositoryName} is not on an executable ready stage yet.");
        }

        return BuildResult(session, nextEntry.Index, updatedItem, message, timestamp);
    }

    public ReleaseQueueTransitionResult ApproveNextSigningGate(ReleaseQueueSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var nextEntry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(entry => entry.Item.Status == ReleaseQueueItemStatus.WaitingApproval && entry.Item.Stage == ReleaseQueueStage.Sign);

        if (nextEntry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, "No queue item is currently waiting on USB approval.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var item = nextEntry.Item;
        var updatedItem = _itemTransitionFactory.CreateTransition(
            item,
            fromStage: "Sign",
            targetStage: ReleaseQueueStage.Publish,
            targetStatus: ReleaseQueueItemStatus.ReadyToRun,
            summary: "USB approval recorded. Publish stage is ready to run.",
            checkpointKey: "publish.ready",
            timestamp: timestamp);

        return BuildResult(session, nextEntry.Index, updatedItem, $"USB approval recorded for {item.RepositoryName}. Publish is now ready.", timestamp);
    }

    public ReleaseQueueTransitionResult CompleteBuild(ReleaseQueueSession session, string rootPath, ReleaseBuildExecutionResult buildResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(buildResult);

        var entry = FindEntry(session, rootPath);

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = _itemTransitionFactory.CreateCheckpointUpdate(
            entry.Item,
            targetStage: ReleaseQueueStage.Sign,
            targetStatus: ReleaseQueueItemStatus.WaitingApproval,
            summary: buildResult.Summary,
            checkpointKey: "sign.waiting.usb",
            checkpoint: buildResult,
            timestamp: timestamp);

        return BuildResult(session, entry.Index, updatedItem, $"Build completed for {entry.Item.RepositoryName}. USB signing approval is now required.", timestamp);
    }

    public ReleaseQueueTransitionResult FailBuild(ReleaseQueueSession session, string rootPath, ReleaseBuildExecutionResult buildResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(buildResult);

        var entry = FindEntry(session, rootPath);

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = _itemTransitionFactory.CreateCheckpointUpdate(
            entry.Item,
            targetStage: ReleaseQueueStage.Build,
            targetStatus: ReleaseQueueItemStatus.Failed,
            summary: buildResult.Summary,
            checkpointKey: "build.failed",
            checkpoint: buildResult,
            timestamp: timestamp);

        return BuildResult(session, entry.Index, updatedItem, $"Build failed for {entry.Item.RepositoryName}. Review the captured queue state before retrying.", timestamp);
    }

    public ReleaseQueueTransitionResult CompleteSigning(ReleaseQueueSession session, string rootPath, ReleaseSigningExecutionResult signingResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(signingResult);

        var entry = FindEntry(session, rootPath);

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = _itemTransitionFactory.CreateCheckpointUpdate(
            entry.Item,
            targetStage: ReleaseQueueStage.Publish,
            targetStatus: ReleaseQueueItemStatus.ReadyToRun,
            summary: signingResult.Summary,
            checkpointKey: "publish.ready",
            checkpoint: signingResult,
            timestamp: timestamp);

        return BuildResult(session, entry.Index, updatedItem, $"Signing completed for {entry.Item.RepositoryName}. Publish is now ready.", timestamp);
    }

    public ReleaseQueueTransitionResult FailSigning(ReleaseQueueSession session, string rootPath, ReleaseSigningExecutionResult signingResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(signingResult);

        var entry = FindEntry(session, rootPath);

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = _itemTransitionFactory.CreateCheckpointUpdate(
            entry.Item,
            targetStage: ReleaseQueueStage.Sign,
            targetStatus: ReleaseQueueItemStatus.Failed,
            summary: signingResult.Summary,
            checkpointKey: "sign.failed",
            checkpoint: signingResult,
            timestamp: timestamp);

        return BuildResult(session, entry.Index, updatedItem, $"Signing failed for {entry.Item.RepositoryName}. Review signing receipts before retrying.", timestamp);
    }

    public ReleaseQueueTransitionResult CompletePublish(ReleaseQueueSession session, string rootPath, ReleasePublishExecutionResult publishResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(publishResult);

        var entry = FindEntry(session, rootPath);

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = _itemTransitionFactory.CreateCheckpointUpdate(
            entry.Item,
            targetStage: ReleaseQueueStage.Verify,
            targetStatus: ReleaseQueueItemStatus.ReadyToRun,
            summary: publishResult.Summary,
            checkpointKey: "verify.ready",
            checkpoint: publishResult,
            timestamp: timestamp);

        return BuildResult(session, entry.Index, updatedItem, $"Publish completed for {entry.Item.RepositoryName}. Verification is now ready.", timestamp);
    }

    public ReleaseQueueTransitionResult FailPublish(ReleaseQueueSession session, string rootPath, ReleasePublishExecutionResult publishResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(publishResult);

        var entry = FindEntry(session, rootPath);

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = _itemTransitionFactory.CreateCheckpointUpdate(
            entry.Item,
            targetStage: ReleaseQueueStage.Publish,
            targetStatus: ReleaseQueueItemStatus.Failed,
            summary: publishResult.Summary,
            checkpointKey: "publish.failed",
            checkpoint: publishResult,
            timestamp: timestamp);

        return BuildResult(session, entry.Index, updatedItem, $"Publish failed for {entry.Item.RepositoryName}. Review publish receipts before retrying.", timestamp);
    }

    public ReleaseQueueTransitionResult CompleteVerification(ReleaseQueueSession session, string rootPath, ReleaseVerificationExecutionResult verificationResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(verificationResult);

        var entry = FindEntry(session, rootPath);

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = _itemTransitionFactory.CreateCheckpointUpdate(
            entry.Item,
            targetStage: ReleaseQueueStage.Completed,
            targetStatus: ReleaseQueueItemStatus.Succeeded,
            summary: verificationResult.Summary,
            checkpointKey: "completed",
            checkpoint: verificationResult,
            timestamp: timestamp);

        return BuildResult(session, entry.Index, updatedItem, $"Verification completed for {entry.Item.RepositoryName}. Queue item is now closed.", timestamp);
    }

    public ReleaseQueueTransitionResult FailVerification(ReleaseQueueSession session, string rootPath, ReleaseVerificationExecutionResult verificationResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(verificationResult);

        var entry = FindEntry(session, rootPath);

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = _itemTransitionFactory.CreateCheckpointUpdate(
            entry.Item,
            targetStage: ReleaseQueueStage.Verify,
            targetStatus: ReleaseQueueItemStatus.Failed,
            summary: verificationResult.Summary,
            checkpointKey: "verify.failed",
            checkpoint: verificationResult,
            timestamp: timestamp);

        return BuildResult(session, entry.Index, updatedItem, $"Verification failed for {entry.Item.RepositoryName}. Review verification receipts before retrying.", timestamp);
    }

    public ReleaseQueueTransitionResult RetryFailedItem(ReleaseQueueSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var failedEntry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(entry => entry.Item.Status == ReleaseQueueItemStatus.Failed);

        if (failedEntry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, "No failed queue item is currently available to retry.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var item = failedEntry.Item;

        return item.Stage switch
        {
            ReleaseQueueStage.Build => RetryBuild(session, failedEntry, timestamp),
            ReleaseQueueStage.Sign => RetrySigning(session, failedEntry, timestamp),
            ReleaseQueueStage.Publish => RetryPublish(session, failedEntry, timestamp),
            ReleaseQueueStage.Verify => RetryVerification(session, failedEntry, timestamp),
            _ => new ReleaseQueueTransitionResult(session, false, $"{item.RepositoryName} cannot be retried from the {item.Stage} stage.")
        };
    }

    public ReleaseQueueTransitionResult RetryFailedItems(ReleaseQueueSession session, Func<ReleaseQueueItem, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(predicate);

        var failedEntries = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .Where(entry => entry.Item.Status == ReleaseQueueItemStatus.Failed && predicate(entry.Item))
            .ToArray();

        if (failedEntries.Length == 0)
        {
            return new ReleaseQueueTransitionResult(session, false, "No matching failed queue items are currently available to retry.");
        }

        var items = session.Items.ToList();
        var timestamp = DateTimeOffset.UtcNow;
        var retried = 0;
        foreach (var entry in failedEntries)
        {
            var retryItem = BuildRetryItem(entry.Item, timestamp);
            if (retryItem is null)
            {
                continue;
            }

            items[entry.Index] = retryItem;
            retried++;
        }

        if (retried == 0)
        {
            return new ReleaseQueueTransitionResult(session, false, "Matching failed queue items were found, but none could be safely rearmed.");
        }

        var updatedSession = ReleaseQueueSessionFactory.WithItems(session, items);

        return new ReleaseQueueTransitionResult(updatedSession, true, $"Rearmed {retried} failed queue item(s) for retry.");
    }

    private static ReleaseQueueTransitionResult BuildResult(
        ReleaseQueueSession session,
        int index,
        ReleaseQueueItem updatedItem,
        string message,
        DateTimeOffset timestamp)
    {
        var items = session.Items.ToList();
        items[index] = updatedItem;
        var updatedSession = ReleaseQueueSessionFactory.WithItems(session, items);

        return new ReleaseQueueTransitionResult(updatedSession, true, message);
    }

    private ReleaseQueueTransitionResult RetryBuild(ReleaseQueueSession session, QueueLookupEntry entry, DateTimeOffset timestamp)
    {
        var updatedItem = BuildRetryItem(entry.Item, timestamp)!;

        return BuildResult(session, entry.Index, updatedItem, $"Build retry armed for {entry.Item.RepositoryName}.", timestamp);
    }

    private ReleaseQueueTransitionResult RetrySigning(ReleaseQueueSession session, QueueLookupEntry entry, DateTimeOffset timestamp)
    {
        var updatedItem = BuildRetryItem(entry.Item, timestamp);
        if (updatedItem is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"{entry.Item.RepositoryName} cannot retry signing because the build checkpoint was not preserved.");
        }

        return BuildResult(session, entry.Index, updatedItem, $"Signing retry armed for {entry.Item.RepositoryName}. USB approval is required again.", timestamp);
    }

    private ReleaseQueueTransitionResult RetryPublish(ReleaseQueueSession session, QueueLookupEntry entry, DateTimeOffset timestamp)
    {
        var updatedItem = BuildRetryItem(entry.Item, timestamp);
        if (updatedItem is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"{entry.Item.RepositoryName} cannot retry publish because the signing checkpoint was not preserved.");
        }

        return BuildResult(session, entry.Index, updatedItem, $"Publish retry armed for {entry.Item.RepositoryName}.", timestamp);
    }

    private ReleaseQueueTransitionResult RetryVerification(ReleaseQueueSession session, QueueLookupEntry entry, DateTimeOffset timestamp)
    {
        var updatedItem = BuildRetryItem(entry.Item, timestamp);
        if (updatedItem is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"{entry.Item.RepositoryName} cannot retry verification because the publish checkpoint was not preserved.");
        }

        return BuildResult(session, entry.Index, updatedItem, $"Verification retry armed for {entry.Item.RepositoryName}.", timestamp);
    }

    private ReleaseQueueItem? BuildRetryItem(ReleaseQueueItem item, DateTimeOffset timestamp)
        => item.Stage switch
        {
            ReleaseQueueStage.Build => _itemTransitionFactory.CreateStateUpdate(
                item,
                targetStage: ReleaseQueueStage.Build,
                targetStatus: ReleaseQueueItemStatus.ReadyToRun,
                summary: "Build retry armed. The item is ready to rerun the build stage.",
                checkpointKey: "build.ready",
                checkpointStateJson: null,
                timestamp: timestamp),
            ReleaseQueueStage.Sign => BuildSigningRetryItem(item, timestamp),
            ReleaseQueueStage.Publish => BuildPublishRetryItem(item, timestamp),
            ReleaseQueueStage.Verify => BuildVerificationRetryItem(item, timestamp),
            _ => null
        };

    private ReleaseQueueItem? BuildSigningRetryItem(ReleaseQueueItem item, DateTimeOffset timestamp)
    {
        var signingResult = _checkpointSerializer.TryDeserialize<ReleaseSigningExecutionResult>(item.CheckpointStateJson);
        if (string.IsNullOrWhiteSpace(signingResult?.SourceCheckpointStateJson))
        {
            return null;
        }

        return _itemTransitionFactory.CreateStateUpdate(
            item,
            targetStage: ReleaseQueueStage.Sign,
            targetStatus: ReleaseQueueItemStatus.WaitingApproval,
            summary: "Signing retry armed. USB approval is required again before signing resumes.",
            checkpointKey: "sign.waiting.usb",
            checkpointStateJson: signingResult.SourceCheckpointStateJson,
            timestamp: timestamp);
    }

    private ReleaseQueueItem? BuildPublishRetryItem(ReleaseQueueItem item, DateTimeOffset timestamp)
    {
        var publishResult = _checkpointSerializer.TryDeserialize<ReleasePublishExecutionResult>(item.CheckpointStateJson);
        if (string.IsNullOrWhiteSpace(publishResult?.SourceCheckpointStateJson))
        {
            return null;
        }

        return _itemTransitionFactory.CreateStateUpdate(
            item,
            targetStage: ReleaseQueueStage.Publish,
            targetStatus: ReleaseQueueItemStatus.ReadyToRun,
            summary: "Publish retry armed. The item is ready to rerun the publish stage.",
            checkpointKey: "publish.ready",
            checkpointStateJson: publishResult.SourceCheckpointStateJson,
            timestamp: timestamp);
    }

    private ReleaseQueueItem? BuildVerificationRetryItem(ReleaseQueueItem item, DateTimeOffset timestamp)
    {
        var verificationResult = _checkpointSerializer.TryDeserialize<ReleaseVerificationExecutionResult>(item.CheckpointStateJson);
        if (string.IsNullOrWhiteSpace(verificationResult?.SourceCheckpointStateJson))
        {
            return null;
        }

        return _itemTransitionFactory.CreateStateUpdate(
            item,
            targetStage: ReleaseQueueStage.Verify,
            targetStatus: ReleaseQueueItemStatus.ReadyToRun,
            summary: "Verification retry armed. The item is ready to rerun the verification stage.",
            checkpointKey: "verify.ready",
            checkpointStateJson: verificationResult.SourceCheckpointStateJson,
            timestamp: timestamp);
    }

    private static QueueLookupEntry? FindEntry(ReleaseQueueSession session, string rootPath)
    {
        return session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(candidate => string.Equals(candidate.Item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record QueueLookupEntry(ReleaseQueueItem Item, int Index);
}
