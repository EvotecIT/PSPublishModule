namespace PowerForge;

/// <summary>
/// Result of executing a typed git command.
/// </summary>
public sealed class GitCommandResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitCommandResult"/> class.
    /// </summary>
    /// <param name="commandKind">Typed git command that was executed.</param>
    /// <param name="workingDirectory">Repository working directory.</param>
    /// <param name="displayCommand">Display-friendly git command text.</param>
    /// <param name="exitCode">Process exit code.</param>
    /// <param name="stdOut">Captured standard output.</param>
    /// <param name="stdErr">Captured standard error.</param>
    /// <param name="executable">Executable used to launch git.</param>
    /// <param name="duration">Observed execution duration.</param>
    /// <param name="timedOut">Indicates whether the command timed out.</param>
    public GitCommandResult(
        GitCommandKind commandKind,
        string workingDirectory,
        string displayCommand,
        int exitCode,
        string stdOut,
        string stdErr,
        string executable,
        TimeSpan duration,
        bool timedOut)
    {
        CommandKind = commandKind;
        WorkingDirectory = workingDirectory;
        DisplayCommand = displayCommand;
        ExitCode = exitCode;
        StdOut = stdOut;
        StdErr = stdErr;
        Executable = executable;
        Duration = duration;
        TimedOut = timedOut;
    }

    /// <summary>
    /// Gets the typed git command that was executed.
    /// </summary>
    public GitCommandKind CommandKind { get; }

    /// <summary>
    /// Gets the repository working directory.
    /// </summary>
    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets the display-friendly git command text.
    /// </summary>
    public string DisplayCommand { get; }

    /// <summary>
    /// Gets the process exit code.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Gets captured standard output.
    /// </summary>
    public string StdOut { get; }

    /// <summary>
    /// Gets captured standard error.
    /// </summary>
    public string StdErr { get; }

    /// <summary>
    /// Gets the executable used to launch git.
    /// </summary>
    public string Executable { get; }

    /// <summary>
    /// Gets the observed execution duration.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets a value indicating whether the command timed out.
    /// </summary>
    public bool TimedOut { get; }

    /// <summary>
    /// Gets a value indicating whether the command completed successfully.
    /// </summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
