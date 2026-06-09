namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryWorkspaceFamilyLaneSnapshot(
    string FamilyKey,
    string DisplayName,
    string Headline,
    string Details,
    int ReadyCount,
    int UsbWaitingCount,
    int PublishReadyCount,
    int VerifyReadyCount,
    int FailedCount,
    int CompletedCount,
    IReadOnlyList<RepositoryWorkspaceFamilyLaneItem> Members);

