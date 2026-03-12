using System.Diagnostics;

namespace PowerForge;

/// <summary>
/// Request to execute an external process with structured arguments.
/// </summary>
public sealed class ProcessRunRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessRunRequest"/> class.
    /// </summary>
    /// <param name="fileName">Executable name or path.</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="arguments">Structured arguments passed to the process.</param>
    /// <param name="timeout">Maximum runtime before the process is terminated.</param>
    /// <param name="environmentVariables">Optional environment variable overrides.</param>
    public ProcessRunRequest(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        FileName = fileName;
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
        Timeout = timeout;
        EnvironmentVariables = environmentVariables;
    }

    /// <summary>
    /// Gets the executable name or path.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the working directory for the process.
    /// </summary>
    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets the structured arguments passed to the process.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// Gets the maximum runtime before the process is terminated.
    /// </summary>
    public TimeSpan Timeout { get; }

    /// <summary>
    /// Gets optional environment variable overrides.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? EnvironmentVariables { get; }
}

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
    public ProcessRunResult(
        int exitCode,
        string stdOut,
        string stdErr,
        string executable,
        TimeSpan duration,
        bool timedOut)
    {
        ExitCode = exitCode;
        StdOut = stdOut;
        StdErr = stdErr;
        Executable = executable;
        Duration = duration;
        TimedOut = timedOut;
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

    /// <summary>
    /// Gets a value indicating whether the process completed successfully.
    /// </summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Executes external processes with structured request/response contracts.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs the provided <paramref name="request"/> and returns the result.
    /// </summary>
    /// <param name="request">Process execution request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured process execution result.</returns>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of <see cref="IProcessRunner"/>.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("Executable name is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            throw new ArgumentException("Working directory is required.", nameof(request));

        using var process = new Process {
            StartInfo = BuildStartInfo(request)
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ProcessRunResult(127, string.Empty, ex.Message, request.FileName, stopwatch.Elapsed, timedOut: false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timedOut = false;

        try
        {
            await WaitForExitAsync(process, request.Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            TryKill(process);
        }

        try
        {
            if (!process.HasExited)
                process.WaitForExit(5000);
        }
        catch
        {
            // Best-effort wait only.
        }

        var stdout = await DrainAsync(stdoutTask).ConfigureAwait(false);
        var stderr = await DrainAsync(stderrTask).ConfigureAwait(false);
        stopwatch.Stop();

        if (timedOut && string.IsNullOrWhiteSpace(stderr))
            stderr = "Timeout";

        var exitCode = timedOut ? 124 : SafeGetExitCode(process);
        return new ProcessRunResult(exitCode, stdout, stderr, process.StartInfo.FileName ?? request.FileName, stopwatch.Elapsed, timedOut);
    }

    private static ProcessStartInfo BuildStartInfo(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ProcessStartInfoEncoding.TryApplyUtf8(startInfo);

        if (request.EnvironmentVariables is not null)
        {
            foreach (var variable in request.EnvironmentVariables)
            {
                if (variable.Value is null)
                {
                    startInfo.EnvironmentVariables.Remove(variable.Key);
                    continue;
                }

                startInfo.EnvironmentVariables[variable.Key] = variable.Value;
            }
        }

#if NET472
        startInfo.Arguments = string.Join(" ", request.Arguments.Select(QuoteArgument));
#else
        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);
#endif

        return startInfo;
    }

    private static async Task WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            timeoutCts.CancelAfter(timeout);

        while (!process.HasExited)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<string> DrainAsync(Task<string> readTask)
    {
        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int SafeGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return 1;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
#if NET472
                process.Kill();
#else
                process.Kill(entireProcessTree: true);
#endif
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

#if NET472
    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return "\"\"";

        if (argument.IndexOfAny(new[] { ' ', '"' }) >= 0)
            return "\"" + argument.Replace("\"", "\\\"") + "\"";

        return argument;
    }
#endif
}
