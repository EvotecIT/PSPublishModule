namespace PowerForgeStudio.Domain.Hub;

public sealed record ProjectGitStatus(
    bool IsGitRepository,
    string? BranchName,
    string? UpstreamBranch,
    int AheadCount,
    int BehindCount,
    int StagedCount,
    int UnstagedCount,
    int UntrackedCount,
    IReadOnlyList<GitFileChange> StagedChanges,
    IReadOnlyList<GitFileChange> UnstagedChanges,
    IReadOnlyList<GitFileChange> UntrackedFiles,
    IReadOnlyList<string> Branches,
    IReadOnlyList<GitWorktreeEntry> Worktrees)
{
    public bool IsDirty => StagedCount > 0 || UnstagedCount > 0 || UntrackedCount > 0;

    public string BranchDisplay => string.IsNullOrWhiteSpace(BranchName) ? "-" : BranchName!;

    public string AheadBehindDisplay => $"+{AheadCount} / -{BehindCount}";

    public string StatusSummary
    {
        get
        {
            if (!IsDirty)
            {
                return "Clean";
            }

            var parts = new List<string>();
            if (StagedCount > 0)
            {
                parts.Add($"{StagedCount} staged");
            }

            if (UnstagedCount > 0)
            {
                parts.Add($"{UnstagedCount} modified");
            }

            if (UntrackedCount > 0)
            {
                parts.Add($"{UntrackedCount} untracked");
            }

            return string.Join(", ", parts);
        }
    }

    public static ProjectGitStatus NotARepository { get; } = new(
        IsGitRepository: false,
        BranchName: null,
        UpstreamBranch: null,
        AheadCount: 0,
        BehindCount: 0,
        StagedCount: 0,
        UnstagedCount: 0,
        UntrackedCount: 0,
        StagedChanges: [],
        UnstagedChanges: [],
        UntrackedFiles: [],
        Branches: [],
        Worktrees: []);
}
