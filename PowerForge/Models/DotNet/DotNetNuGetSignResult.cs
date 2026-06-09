namespace PowerForge;

/// <summary>
/// Result of executing <c>dotnet nuget sign</c>.
/// </summary>
public sealed class DotNetNuGetSignResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DotNetNuGetSignResult"/> class.
    /// </summary>
    public DotNetNuGetSignResult(
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
    /// Gets a value indicating whether the sign completed successfully.
    /// </summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
