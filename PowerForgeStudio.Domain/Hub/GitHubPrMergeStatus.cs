namespace PowerForgeStudio.Domain.Hub;

public enum GitHubPrMergeStatus
{
    Unknown = 0,
    Clean = 1,
    Blocked = 2,
    Behind = 3,
    Conflicting = 4
}
