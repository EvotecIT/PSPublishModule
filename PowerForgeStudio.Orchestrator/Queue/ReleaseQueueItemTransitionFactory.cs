using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseQueueItemTransitionFactory
{
    private readonly ReleaseQueueCheckpointSerializer _checkpointSerializer;

    public ReleaseQueueItemTransitionFactory()
        : this(new ReleaseQueueCheckpointSerializer())
    {
    }

    internal ReleaseQueueItemTransitionFactory(ReleaseQueueCheckpointSerializer checkpointSerializer)
    {
        _checkpointSerializer = checkpointSerializer ?? throw new ArgumentNullException(nameof(checkpointSerializer));
    }

    public ReleaseQueueItem CreateTransition(
        ReleaseQueueItem item,
        string fromStage,
        ReleaseQueueStage targetStage,
        ReleaseQueueItemStatus targetStatus,
        string summary,
        string checkpointKey,
        DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromStage);

        return CreateStateUpdate(
            item,
            targetStage,
            targetStatus,
            summary,
            checkpointKey,
            _checkpointSerializer.SerializeTransition(fromStage, targetStage.ToString(), timestamp),
            timestamp);
    }

    public ReleaseQueueItem CreateCheckpointUpdate<TCheckpoint>(
        ReleaseQueueItem item,
        ReleaseQueueStage targetStage,
        ReleaseQueueItemStatus targetStatus,
        string summary,
        string checkpointKey,
        TCheckpoint checkpoint,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        return CreateStateUpdate(
            item,
            targetStage,
            targetStatus,
            summary,
            checkpointKey,
            _checkpointSerializer.Serialize(checkpoint),
            timestamp);
    }

    public ReleaseQueueItem CreateStateUpdate(
        ReleaseQueueItem item,
        ReleaseQueueStage targetStage,
        ReleaseQueueItemStatus targetStatus,
        string summary,
        string checkpointKey,
        string? checkpointStateJson,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointKey);

        return item with {
            Stage = targetStage,
            Status = targetStatus,
            Summary = summary,
            CheckpointKey = checkpointKey,
            CheckpointStateJson = checkpointStateJson,
            UpdatedAtUtc = timestamp
        };
    }
}
