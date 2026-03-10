using PowerForgeStudio.Domain.Catalog;

namespace PowerForgeStudio.Domain.Queue;

public sealed record ReleaseQueueItem(
    string RootPath,
    string RepositoryName,
    ReleaseRepositoryKind RepositoryKind,
    ReleaseWorkspaceKind WorkspaceKind,
    int QueueOrder,
    ReleaseQueueStage Stage,
    ReleaseQueueItemStatus Status,
    string Summary,
    string? CheckpointKey,
    string? CheckpointStateJson,
    DateTimeOffset UpdatedAtUtc)
{
    public string OrderDisplay => QueueOrder.ToString("00");

    public string StageDisplay => Stage.ToString();

    public string StatusDisplay => Status switch
    {
        ReleaseQueueItemStatus.ReadyToRun => "Ready",
        ReleaseQueueItemStatus.WaitingApproval => "Waiting",
        _ => Status.ToString()
    };

    public string LaneDisplay => $"{StageDisplay} / {StatusDisplay}";

    public string RepositoryBadge => $"{RepositoryKind} / {WorkspaceKind}";
}

