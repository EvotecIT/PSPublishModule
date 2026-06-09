namespace PowerForgeStudio.Domain.Queue;

public enum ReleaseQueueItemStatus
{
    Pending = 0,
    ReadyToRun = 1,
    WaitingApproval = 2,
    Succeeded = 3,
    Failed = 4,
    Blocked = 5
}

