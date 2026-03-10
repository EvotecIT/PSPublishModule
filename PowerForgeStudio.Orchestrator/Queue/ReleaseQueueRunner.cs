using System.Text.Json;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseQueueRunner
{
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
                updatedItem = item with {
                    Stage = ReleaseQueueStage.Sign,
                    Status = ReleaseQueueItemStatus.WaitingApproval,
                    Summary = "Build stage completed in orchestration mode. USB signing approval is now required.",
                    CheckpointKey = "sign.waiting.usb",
                    CheckpointStateJson = SerializeCheckpoint("Build", "Sign", timestamp),
                    UpdatedAtUtc = timestamp
                };
                message = $"Advanced {item.RepositoryName} from Build to Sign and paused for USB approval.";
                break;
            case ReleaseQueueStage.Sign:
                return new ReleaseQueueTransitionResult(session, false, $"{item.RepositoryName} is marked Sign/ReadyToRun, but signing requires the USB approval gate before execution can continue.");
            case ReleaseQueueStage.Publish:
                updatedItem = item with {
                    Stage = ReleaseQueueStage.Verify,
                    Status = ReleaseQueueItemStatus.ReadyToRun,
                    Summary = "Publish step completed in orchestration mode. Verification is ready to run.",
                    CheckpointKey = "verify.ready",
                    CheckpointStateJson = SerializeCheckpoint("Publish", "Verify", timestamp),
                    UpdatedAtUtc = timestamp
                };
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

        var item = nextEntry.Item;
        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = item with {
            Stage = ReleaseQueueStage.Publish,
            Status = ReleaseQueueItemStatus.ReadyToRun,
            Summary = "USB approval recorded. Publish stage is ready to run.",
            CheckpointKey = "publish.ready",
            CheckpointStateJson = SerializeCheckpoint("Sign", "Publish", timestamp),
            UpdatedAtUtc = timestamp
        };

        return BuildResult(session, nextEntry.Index, updatedItem, $"USB approval recorded for {item.RepositoryName}. Publish is now ready.", timestamp);
    }

    public ReleaseQueueTransitionResult CompleteBuild(ReleaseQueueSession session, string rootPath, ReleaseBuildExecutionResult buildResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(buildResult);

        var entry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(candidate => string.Equals(candidate.Item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = entry.Item with {
            Stage = ReleaseQueueStage.Sign,
            Status = ReleaseQueueItemStatus.WaitingApproval,
            Summary = buildResult.Summary,
            CheckpointKey = "sign.waiting.usb",
            CheckpointStateJson = JsonSerializer.Serialize(buildResult),
            UpdatedAtUtc = timestamp
        };

        return BuildResult(session, entry.Index, updatedItem, $"Build completed for {entry.Item.RepositoryName}. USB signing approval is now required.", timestamp);
    }

    public ReleaseQueueTransitionResult FailBuild(ReleaseQueueSession session, string rootPath, ReleaseBuildExecutionResult buildResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(buildResult);

        var entry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(candidate => string.Equals(candidate.Item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = entry.Item with {
            Stage = ReleaseQueueStage.Build,
            Status = ReleaseQueueItemStatus.Failed,
            Summary = buildResult.Summary,
            CheckpointKey = "build.failed",
            CheckpointStateJson = JsonSerializer.Serialize(buildResult),
            UpdatedAtUtc = timestamp
        };

        return BuildResult(session, entry.Index, updatedItem, $"Build failed for {entry.Item.RepositoryName}. Review the captured queue state before retrying.", timestamp);
    }

    public ReleaseQueueTransitionResult CompleteSigning(ReleaseQueueSession session, string rootPath, ReleaseSigningExecutionResult signingResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(signingResult);

        var entry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(candidate => string.Equals(candidate.Item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = entry.Item with {
            Stage = ReleaseQueueStage.Publish,
            Status = ReleaseQueueItemStatus.ReadyToRun,
            Summary = signingResult.Summary,
            CheckpointKey = "publish.ready",
            CheckpointStateJson = JsonSerializer.Serialize(signingResult),
            UpdatedAtUtc = timestamp
        };

        return BuildResult(session, entry.Index, updatedItem, $"Signing completed for {entry.Item.RepositoryName}. Publish is now ready.", timestamp);
    }

    public ReleaseQueueTransitionResult FailSigning(ReleaseQueueSession session, string rootPath, ReleaseSigningExecutionResult signingResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(signingResult);

        var entry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(candidate => string.Equals(candidate.Item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = entry.Item with {
            Stage = ReleaseQueueStage.Sign,
            Status = ReleaseQueueItemStatus.Failed,
            Summary = signingResult.Summary,
            CheckpointKey = "sign.failed",
            CheckpointStateJson = JsonSerializer.Serialize(signingResult),
            UpdatedAtUtc = timestamp
        };

        return BuildResult(session, entry.Index, updatedItem, $"Signing failed for {entry.Item.RepositoryName}. Review signing receipts before retrying.", timestamp);
    }

    public ReleaseQueueTransitionResult CompletePublish(ReleaseQueueSession session, string rootPath, ReleasePublishExecutionResult publishResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(publishResult);

        var entry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(candidate => string.Equals(candidate.Item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = entry.Item with {
            Stage = ReleaseQueueStage.Verify,
            Status = ReleaseQueueItemStatus.ReadyToRun,
            Summary = publishResult.Summary,
            CheckpointKey = "verify.ready",
            CheckpointStateJson = JsonSerializer.Serialize(publishResult),
            UpdatedAtUtc = timestamp
        };

        return BuildResult(session, entry.Index, updatedItem, $"Publish completed for {entry.Item.RepositoryName}. Verification is now ready.", timestamp);
    }

    public ReleaseQueueTransitionResult FailPublish(ReleaseQueueSession session, string rootPath, ReleasePublishExecutionResult publishResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(publishResult);

        var entry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(candidate => string.Equals(candidate.Item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = entry.Item with {
            Stage = ReleaseQueueStage.Publish,
            Status = ReleaseQueueItemStatus.Failed,
            Summary = publishResult.Summary,
            CheckpointKey = "publish.failed",
            CheckpointStateJson = JsonSerializer.Serialize(publishResult),
            UpdatedAtUtc = timestamp
        };

        return BuildResult(session, entry.Index, updatedItem, $"Publish failed for {entry.Item.RepositoryName}. Review publish receipts before retrying.", timestamp);
    }

    public ReleaseQueueTransitionResult CompleteVerification(ReleaseQueueSession session, string rootPath, ReleaseVerificationExecutionResult verificationResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(verificationResult);

        var entry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(candidate => string.Equals(candidate.Item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = entry.Item with {
            Stage = ReleaseQueueStage.Completed,
            Status = ReleaseQueueItemStatus.Succeeded,
            Summary = verificationResult.Summary,
            CheckpointKey = "completed",
            CheckpointStateJson = JsonSerializer.Serialize(verificationResult),
            UpdatedAtUtc = timestamp
        };

        return BuildResult(session, entry.Index, updatedItem, $"Verification completed for {entry.Item.RepositoryName}. Queue item is now closed.", timestamp);
    }

    public ReleaseQueueTransitionResult FailVerification(ReleaseQueueSession session, string rootPath, ReleaseVerificationExecutionResult verificationResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(verificationResult);

        var entry = session.Items
            .Select((item, index) => new QueueLookupEntry(item, index))
            .FirstOrDefault(candidate => string.Equals(candidate.Item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"Queue item {rootPath} was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updatedItem = entry.Item with {
            Stage = ReleaseQueueStage.Verify,
            Status = ReleaseQueueItemStatus.Failed,
            Summary = verificationResult.Summary,
            CheckpointKey = "verify.failed",
            CheckpointStateJson = JsonSerializer.Serialize(verificationResult),
            UpdatedAtUtc = timestamp
        };

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

        var updatedSession = session with {
            Items = items,
            Summary = BuildSummary(items)
        };

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
        var updatedSession = session with {
            Items = items,
            Summary = BuildSummary(items)
        };

        return new ReleaseQueueTransitionResult(updatedSession, true, message);
    }

    private static ReleaseQueueSummary BuildSummary(IReadOnlyList<ReleaseQueueItem> items)
    {
        return new ReleaseQueueSummary(
            TotalItems: items.Count,
            BuildReadyItems: items.Count(item => item.Stage == ReleaseQueueStage.Build && item.Status == ReleaseQueueItemStatus.ReadyToRun),
            PreparePendingItems: items.Count(item => item.Stage == ReleaseQueueStage.Prepare && item.Status == ReleaseQueueItemStatus.Pending),
            WaitingApprovalItems: items.Count(item => item.Status == ReleaseQueueItemStatus.WaitingApproval),
            BlockedItems: items.Count(item => item.Status == ReleaseQueueItemStatus.Blocked || item.Status == ReleaseQueueItemStatus.Failed),
            VerificationReadyItems: items.Count(item => item.Stage == ReleaseQueueStage.Verify && item.Status == ReleaseQueueItemStatus.ReadyToRun));
    }

    private static string SerializeCheckpoint(string fromStage, string toStage, DateTimeOffset timestamp)
    {
        return JsonSerializer.Serialize(new Dictionary<string, string> {
            ["from"] = fromStage,
            ["to"] = toStage,
            ["updatedAtUtc"] = timestamp.ToString("O")
        });
    }

    private static ReleaseQueueTransitionResult RetryBuild(ReleaseQueueSession session, QueueLookupEntry entry, DateTimeOffset timestamp)
    {
        var updatedItem = BuildRetryItem(entry.Item, timestamp)!;

        return BuildResult(session, entry.Index, updatedItem, $"Build retry armed for {entry.Item.RepositoryName}.", timestamp);
    }

    private static ReleaseQueueTransitionResult RetrySigning(ReleaseQueueSession session, QueueLookupEntry entry, DateTimeOffset timestamp)
    {
        var updatedItem = BuildRetryItem(entry.Item, timestamp);
        if (updatedItem is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"{entry.Item.RepositoryName} cannot retry signing because the build checkpoint was not preserved.");
        }

        return BuildResult(session, entry.Index, updatedItem, $"Signing retry armed for {entry.Item.RepositoryName}. USB approval is required again.", timestamp);
    }

    private static ReleaseQueueTransitionResult RetryPublish(ReleaseQueueSession session, QueueLookupEntry entry, DateTimeOffset timestamp)
    {
        var updatedItem = BuildRetryItem(entry.Item, timestamp);
        if (updatedItem is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"{entry.Item.RepositoryName} cannot retry publish because the signing checkpoint was not preserved.");
        }

        return BuildResult(session, entry.Index, updatedItem, $"Publish retry armed for {entry.Item.RepositoryName}.", timestamp);
    }

    private static ReleaseQueueTransitionResult RetryVerification(ReleaseQueueSession session, QueueLookupEntry entry, DateTimeOffset timestamp)
    {
        var updatedItem = BuildRetryItem(entry.Item, timestamp);
        if (updatedItem is null)
        {
            return new ReleaseQueueTransitionResult(session, false, $"{entry.Item.RepositoryName} cannot retry verification because the publish checkpoint was not preserved.");
        }

        return BuildResult(session, entry.Index, updatedItem, $"Verification retry armed for {entry.Item.RepositoryName}.", timestamp);
    }

    private static ReleaseQueueItem? BuildRetryItem(ReleaseQueueItem item, DateTimeOffset timestamp)
        => item.Stage switch
        {
            ReleaseQueueStage.Build => item with {
                Stage = ReleaseQueueStage.Build,
                Status = ReleaseQueueItemStatus.ReadyToRun,
                Summary = "Build retry armed. The item is ready to rerun the build stage.",
                CheckpointKey = "build.ready",
                CheckpointStateJson = null,
                UpdatedAtUtc = timestamp
            },
            ReleaseQueueStage.Sign => BuildSigningRetryItem(item, timestamp),
            ReleaseQueueStage.Publish => BuildPublishRetryItem(item, timestamp),
            ReleaseQueueStage.Verify => BuildVerificationRetryItem(item, timestamp),
            _ => null
        };

    private static ReleaseQueueItem? BuildSigningRetryItem(ReleaseQueueItem item, DateTimeOffset timestamp)
    {
        var signingResult = TryDeserialize<ReleaseSigningExecutionResult>(item.CheckpointStateJson);
        if (string.IsNullOrWhiteSpace(signingResult?.SourceCheckpointStateJson))
        {
            return null;
        }

        return item with {
            Stage = ReleaseQueueStage.Sign,
            Status = ReleaseQueueItemStatus.WaitingApproval,
            Summary = "Signing retry armed. USB approval is required again before signing resumes.",
            CheckpointKey = "sign.waiting.usb",
            CheckpointStateJson = signingResult.SourceCheckpointStateJson,
            UpdatedAtUtc = timestamp
        };
    }

    private static ReleaseQueueItem? BuildPublishRetryItem(ReleaseQueueItem item, DateTimeOffset timestamp)
    {
        var publishResult = TryDeserialize<ReleasePublishExecutionResult>(item.CheckpointStateJson);
        if (string.IsNullOrWhiteSpace(publishResult?.SourceCheckpointStateJson))
        {
            return null;
        }

        return item with {
            Stage = ReleaseQueueStage.Publish,
            Status = ReleaseQueueItemStatus.ReadyToRun,
            Summary = "Publish retry armed. The item is ready to rerun the publish stage.",
            CheckpointKey = "publish.ready",
            CheckpointStateJson = publishResult.SourceCheckpointStateJson,
            UpdatedAtUtc = timestamp
        };
    }

    private static ReleaseQueueItem? BuildVerificationRetryItem(ReleaseQueueItem item, DateTimeOffset timestamp)
    {
        var verificationResult = TryDeserialize<ReleaseVerificationExecutionResult>(item.CheckpointStateJson);
        if (string.IsNullOrWhiteSpace(verificationResult?.SourceCheckpointStateJson))
        {
            return null;
        }

        return item with {
            Stage = ReleaseQueueStage.Verify,
            Status = ReleaseQueueItemStatus.ReadyToRun,
            Summary = "Verification retry armed. The item is ready to rerun the verification stage.",
            CheckpointKey = "verify.ready",
            CheckpointStateJson = verificationResult.SourceCheckpointStateJson,
            UpdatedAtUtc = timestamp
        };
    }

    private static T? TryDeserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }

    private sealed record QueueLookupEntry(ReleaseQueueItem Item, int Index);
}
