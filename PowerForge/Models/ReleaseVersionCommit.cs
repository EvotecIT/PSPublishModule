namespace PowerForge;

/// <summary>
/// Controls whether a successful release saves the version numbers it wrote into tracked files to Git.
/// </summary>
public enum ReleaseVersionCommitMode
{
    /// <summary>Leave version changes in the working tree (default).</summary>
    Off = 0,

    /// <summary>Commit the stamped version files locally after every publish step succeeds.</summary>
    Commit = 1,

    /// <summary>Commit the stamped version files and push the commit to the current branch.</summary>
    Push = 2
}

/// <summary>
/// Outcome of saving release version changes to Git.
/// </summary>
public enum ReleaseVersionCommitStatus
{
    /// <summary>No version file changed, so nothing was committed.</summary>
    NothingToCommit,

    /// <summary>The version files were committed locally.</summary>
    Committed,

    /// <summary>The version files were committed and pushed.</summary>
    Pushed,

    /// <summary>The build half of a two-phase release recorded its stamps; the publish half commits them.</summary>
    Deferred,

    /// <summary>The version files were left uncommitted, or the commit was not pushed. The release itself is unaffected.</summary>
    Incomplete
}

/// <summary>
/// Result of saving release version changes to Git. This never changes whether the release succeeded.
/// </summary>
public sealed class ReleaseVersionCommitResult
{
    /// <summary>Outcome of the commit step.</summary>
    public ReleaseVersionCommitStatus Status { get; set; }

    /// <summary>Commit created for the version files, when one was created.</summary>
    public string? CommitSha { get; set; }

    /// <summary>Repository-relative paths included in the commit (or that would have been).</summary>
    public string[] Files { get; set; } = Array.Empty<string>();

    /// <summary>Stamped files left out because they already held local edits before the release.</summary>
    public string[] SkippedFiles { get; set; } = Array.Empty<string>();

    /// <summary>The single summary or warning line written to the log.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Command that finishes the step by hand, when something was left undone.</summary>
    public string? FollowUpCommand { get; set; }
}
