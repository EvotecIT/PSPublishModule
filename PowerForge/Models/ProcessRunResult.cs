namespace PowerForge;

/// <summary>
/// Result of executing an external process.
/// </summary>
public sealed class ProcessRunResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessRunResult"/> class.
    /// </summary>
    /// <param name="exitCode">Process exit code.</param>
    /// <param name="stdOut">Captured standard output.</param>
    /// <param name="stdErr">Captured standard error.</param>
    /// <param name="executable">Executable name or path used to launch the process.</param>
    /// <param name="duration">Observed process duration.</param>
    /// <param name="timedOut">Indicates whether the process timed out.</param>
    /// <param name="standardOutputLimitExceeded">Whether retained standard output exceeded its configured character limit.</param>
    /// <param name="standardErrorLimitExceeded">Whether retained standard error exceeded its configured character limit.</param>
    public ProcessRunResult(
        int exitCode,
        string stdOut,
        string stdErr,
        string executable,
        TimeSpan duration,
        bool timedOut,
        bool standardOutputLimitExceeded = false,
        bool standardErrorLimitExceeded = false)
        : this(exitCode, stdOut, stdErr, executable, duration, timedOut,
            standardOutputLimitExceeded, standardErrorLimitExceeded, startFailed: false) { }

    /// <summary>Creates a result that distinguishes a startup failure from a genuine child exit code.</summary>
    /// <param name="exitCode">Child exit code or the runner's failure code.</param>
    /// <param name="stdOut">Captured standard output.</param>
    /// <param name="stdErr">Captured standard error or startup diagnostic.</param>
    /// <param name="executable">Executable name or path.</param>
    /// <param name="duration">Observed execution duration.</param>
    /// <param name="timedOut">Whether the operation timed out.</param>
    /// <param name="standardOutputLimitExceeded">Whether the standard-output limit was exceeded.</param>
    /// <param name="standardErrorLimitExceeded">Whether the standard-error limit was exceeded.</param>
    /// <param name="startFailed">Whether process startup or its start-boundary callbacks failed.</param>
    public ProcessRunResult(int exitCode, string stdOut, string stdErr, string executable, TimeSpan duration,
        bool timedOut, bool standardOutputLimitExceeded, bool standardErrorLimitExceeded, bool startFailed)
    {
        ExitCode = exitCode;
        StdOut = stdOut;
        StdErr = stdErr;
        Executable = executable;
        Duration = duration;
        TimedOut = timedOut;
        StartFailed = startFailed;
        StandardOutputLimitExceeded = standardOutputLimitExceeded;
        StandardErrorLimitExceeded = standardErrorLimitExceeded;
    }

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
    /// Gets the executable name or path used to launch the process.
    /// </summary>
    public string Executable { get; }

    /// <summary>
    /// Gets the observed process duration.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets a value indicating whether the process timed out.
    /// </summary>
    public bool TimedOut { get; }

    /// <summary>Whether process startup or its start-boundary callbacks failed; never a successful probe, regardless of accepted exit codes.</summary>
    public bool StartFailed { get; }

    /// <summary>Whether standard output exceeded the configured retained-character limit.</summary>
    public bool StandardOutputLimitExceeded { get; }

    /// <summary>Whether standard error exceeded the configured retained-character limit.</summary>
    public bool StandardErrorLimitExceeded { get; }

    /// <summary>
    /// Gets a value indicating whether the process completed successfully.
    /// </summary>
    public bool Succeeded => ExitCode == 0 && !StartFailed && !TimedOut && !StandardOutputLimitExceeded && !StandardErrorLimitExceeded;
}
