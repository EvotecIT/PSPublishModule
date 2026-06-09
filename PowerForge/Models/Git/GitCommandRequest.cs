namespace PowerForge;

/// <summary>
/// Typed request for a git command execution.
/// </summary>
public sealed class GitCommandRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitCommandRequest"/> class.
    /// </summary>
    /// <param name="workingDirectory">Repository working directory.</param>
    /// <param name="commandKind">Typed git command to execute.</param>
    /// <param name="branchName">Optional branch name used by branch-aware commands.</param>
    /// <param name="remoteName">Optional remote name used by remote-aware commands.</param>
    /// <param name="timeout">Optional timeout override.</param>
    public GitCommandRequest(
        string workingDirectory,
        GitCommandKind commandKind,
        string? branchName = null,
        string? remoteName = null,
        TimeSpan? timeout = null)
    {
        WorkingDirectory = workingDirectory;
        CommandKind = commandKind;
        BranchName = branchName;
        RemoteName = remoteName;
        Timeout = timeout;
    }

    /// <summary>
    /// Gets the repository working directory.
    /// </summary>
    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets the typed git command to execute.
    /// </summary>
    public GitCommandKind CommandKind { get; }

    /// <summary>
    /// Gets the optional branch name.
    /// </summary>
    public string? BranchName { get; }

    /// <summary>
    /// Gets the optional remote name.
    /// </summary>
    public string? RemoteName { get; }

    /// <summary>
    /// Gets the optional timeout override.
    /// </summary>
    public TimeSpan? Timeout { get; }
}
