namespace ReleaseOpsStudio.Domain.Portfolio;

public sealed record RepositoryGitSnapshot(
    bool IsGitRepository,
    string? BranchName,
    string? UpstreamBranch,
    int AheadCount,
    int BehindCount,
    int TrackedChangeCount,
    int UntrackedChangeCount)
{
    public bool IsDirty => TrackedChangeCount > 0 || UntrackedChangeCount > 0;

    public string BranchDisplay => string.IsNullOrWhiteSpace(BranchName) ? "-" : BranchName!;

    public string AheadBehindDisplay => $"+{AheadCount} / -{BehindCount}";
}
