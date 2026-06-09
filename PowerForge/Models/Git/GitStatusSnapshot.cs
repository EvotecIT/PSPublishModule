namespace PowerForge;

/// <summary>
/// Parsed repository status snapshot derived from <c>git status --porcelain=2 --branch</c>.
/// </summary>
public sealed class GitStatusSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitStatusSnapshot"/> class.
    /// </summary>
    /// <param name="isGitRepository">Indicates whether git metadata was available.</param>
    /// <param name="branchName">Current branch name.</param>
    /// <param name="upstreamBranch">Current upstream branch.</param>
    /// <param name="aheadCount">Ahead count versus upstream.</param>
    /// <param name="behindCount">Behind count versus upstream.</param>
    /// <param name="trackedChangeCount">Tracked change count.</param>
    /// <param name="untrackedChangeCount">Untracked change count.</param>
    /// <param name="commandResult">Underlying typed git command result.</param>
    public GitStatusSnapshot(
        bool isGitRepository,
        string? branchName,
        string? upstreamBranch,
        int aheadCount,
        int behindCount,
        int trackedChangeCount,
        int untrackedChangeCount,
        GitCommandResult commandResult)
    {
        IsGitRepository = isGitRepository;
        BranchName = branchName;
        UpstreamBranch = upstreamBranch;
        AheadCount = aheadCount;
        BehindCount = behindCount;
        TrackedChangeCount = trackedChangeCount;
        UntrackedChangeCount = untrackedChangeCount;
        CommandResult = commandResult;
    }

    /// <summary>
    /// Gets a value indicating whether git metadata was available.
    /// </summary>
    public bool IsGitRepository { get; }

    /// <summary>
    /// Gets the current branch name.
    /// </summary>
    public string? BranchName { get; }

    /// <summary>
    /// Gets the current upstream branch.
    /// </summary>
    public string? UpstreamBranch { get; }

    /// <summary>
    /// Gets the ahead count versus upstream.
    /// </summary>
    public int AheadCount { get; }

    /// <summary>
    /// Gets the behind count versus upstream.
    /// </summary>
    public int BehindCount { get; }

    /// <summary>
    /// Gets the tracked change count.
    /// </summary>
    public int TrackedChangeCount { get; }

    /// <summary>
    /// Gets the untracked change count.
    /// </summary>
    public int UntrackedChangeCount { get; }

    /// <summary>
    /// Gets the underlying typed git command result.
    /// </summary>
    public GitCommandResult CommandResult { get; }
}
