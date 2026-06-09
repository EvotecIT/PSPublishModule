namespace PowerForgeStudio.Domain.Queue;

public sealed record ReleaseQueueSummary(
    int TotalItems,
    int BuildReadyItems,
    int PreparePendingItems,
    int WaitingApprovalItems,
    int BlockedItems,
    int VerificationReadyItems);

