namespace PowerForge;

/// <summary>
/// Result of executing <c>dotnet nuget push</c>.
/// </summary>
public sealed class DotNetNuGetPushResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DotNetNuGetPushResult"/> class.
    /// </summary>
    public DotNetNuGetPushResult(
        int exitCode,
        string stdOut,
        string stdErr,
        string executable,
        TimeSpan duration,
        bool timedOut,
        string? errorMessage)
    {
        ExitCode = exitCode;
        StdOut = stdOut;
        StdErr = stdErr;
        Executable = executable;
        Duration = duration;
        TimedOut = timedOut;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets the process exit code.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Gets the captured standard output.
    /// </summary>
    public string StdOut { get; }

    /// <summary>
    /// Gets the captured standard error.
    /// </summary>
    public string StdErr { get; }

    /// <summary>
    /// Gets the executable used to run the command.
    /// </summary>
    public string Executable { get; }

    /// <summary>
    /// Gets the observed process duration.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets a value indicating whether the command timed out.
    /// </summary>
    public bool TimedOut { get; }

    /// <summary>
    /// Gets the first meaningful error message, when available.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets a value indicating whether the push completed successfully.
    /// </summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
