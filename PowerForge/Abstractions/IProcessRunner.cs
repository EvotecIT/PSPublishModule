using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge;

/// <summary>
/// Request to execute an external process with structured arguments.
/// </summary>
public sealed class ProcessRunRequest
{
    private int _startBoundaryInvoked;
    private Action? _startBoundary;
    private int _startedProcessBoundaryInvoked;
    private Action<int>? _startedProcessBoundary;
    private int _completionBoundaryInvoked;
    private Action<ProcessRunResult>? _completionBoundary;
    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessRunRequest"/> class.
    /// </summary>
    /// <param name="fileName">Executable name or path.</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="arguments">Structured arguments passed to the process.</param>
    /// <param name="timeout">Maximum runtime before the process is terminated.</param>
    /// <param name="environmentVariables">Optional environment variable overrides.</param>
    /// <param name="captureOutput">When true, capture standard output.</param>
    /// <param name="captureError">When true, capture standard error.</param>
    public ProcessRunRequest(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        bool captureOutput = true,
        bool captureError = true)
        : this(
            fileName,
            workingDirectory,
            arguments,
            timeout,
            environmentVariables,
            captureOutput,
            captureError,
            outputLineReceived: null,
            errorLineReceived: null,
            inheritEnvironment: true)
    {
    }

    /// <summary>
    /// Initializes a process request that can opt out of parent-environment inheritance.
    /// </summary>
    /// <param name="fileName">Executable name or path.</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="arguments">Structured arguments passed to the process.</param>
    /// <param name="timeout">Maximum runtime before the process is terminated.</param>
    /// <param name="environmentVariables">Environment variables applied to the child.</param>
    /// <param name="captureOutput">When true, capture standard output.</param>
    /// <param name="captureError">When true, capture standard error.</param>
    /// <param name="inheritEnvironment">When false, start from an empty environment.</param>
    public ProcessRunRequest(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        bool captureOutput,
        bool captureError,
        bool inheritEnvironment)
        : this(
            fileName,
            workingDirectory,
            arguments,
            timeout,
            environmentVariables,
            captureOutput,
            captureError,
            outputLineReceived: null,
            errorLineReceived: null,
            inheritEnvironment: inheritEnvironment)
    {
    }

    /// <summary>
    /// Initializes a streaming process request while retaining captured output.
    /// </summary>
    /// <param name="fileName">Executable name or path.</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="arguments">Structured arguments passed to the process.</param>
    /// <param name="timeout">Maximum runtime before the process is terminated.</param>
    /// <param name="environmentVariables">Optional environment variable overrides.</param>
    /// <param name="captureOutput">When true, capture standard output.</param>
    /// <param name="captureError">When true, capture standard error.</param>
    /// <param name="outputLineReceived">Optional callback for each captured standard-output line.</param>
    /// <param name="errorLineReceived">Optional callback for each captured standard-error line.</param>
    public ProcessRunRequest(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        bool captureOutput,
        bool captureError,
        Action<string>? outputLineReceived,
        Action<string>? errorLineReceived)
        : this(
            fileName,
            workingDirectory,
            arguments,
            timeout,
            environmentVariables,
            captureOutput,
            captureError,
            outputLineReceived,
            errorLineReceived,
            inheritEnvironment: true)
    {
    }

    /// <summary>
    /// Initializes a streaming process request with explicit parent-environment inheritance policy.
    /// </summary>
    /// <param name="fileName">Executable name or path.</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="arguments">Structured arguments passed to the process.</param>
    /// <param name="timeout">Maximum runtime before the process is terminated.</param>
    /// <param name="environmentVariables">Environment variables applied to the child.</param>
    /// <param name="captureOutput">When true, capture standard output.</param>
    /// <param name="captureError">When true, capture standard error.</param>
    /// <param name="outputLineReceived">Optional callback for each captured standard-output line.</param>
    /// <param name="errorLineReceived">Optional callback for each captured standard-error line.</param>
    /// <param name="inheritEnvironment">When false, start from an empty environment.</param>
    public ProcessRunRequest(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        bool captureOutput,
        bool captureError,
        Action<string>? outputLineReceived,
        Action<string>? errorLineReceived,
        bool inheritEnvironment)
    {
        FileName = fileName;
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
        Timeout = timeout;
        EnvironmentVariables = environmentVariables;
        CaptureOutput = captureOutput;
        CaptureError = captureError;
        OutputLineReceived = outputLineReceived;
        ErrorLineReceived = errorLineReceived;
        InheritEnvironment = inheritEnvironment;
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

    /// <summary>
    /// Gets a value indicating whether the child process inherits the parent environment.
    /// </summary>
    public bool InheritEnvironment { get; }

    /// <summary>
    /// Gets a value indicating whether standard output should be captured.
    /// </summary>
    public bool CaptureOutput { get; }

    /// <summary>
    /// Gets a value indicating whether standard error should be captured.
    /// </summary>
    public bool CaptureError { get; }

    /// <summary>Maximum characters retained independently for standard output and standard error.</summary>
    public int MaxCapturedOutputCharacters { get; set; } = int.MaxValue;

    /// <summary>Optional callback invoked for each captured standard-output line.</summary>
    public Action<string>? OutputLineReceived { get; }

    /// <summary>Optional callback invoked for each captured standard-error line.</summary>
    public Action<string>? ErrorLineReceived { get; }

    internal void SetCompletionBoundary(Action<ProcessRunResult> completionBoundary)
        => _completionBoundary = completionBoundary ?? throw new ArgumentNullException(nameof(completionBoundary));

    internal void SetStartBoundary(Action startBoundary)
        => _startBoundary = startBoundary ?? throw new ArgumentNullException(nameof(startBoundary));

    internal void SetStartedProcessBoundary(Action<int> startedProcessBoundary)
        => _startedProcessBoundary = startedProcessBoundary ?? throw new ArgumentNullException(nameof(startedProcessBoundary));

    /// <summary>
    /// Supplies the operating-system process identifier immediately after a successful start.
    /// Custom runners must invoke this boundary before allowing externally visible child work.
    /// </summary>
    /// <param name="processId">Started operating-system process identifier.</param>
    public void InvokeStartedProcessBoundary(int processId)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        if (_startedProcessBoundary is null || Interlocked.Exchange(ref _startedProcessBoundaryInvoked, 1) != 0)
            return;
        _startedProcessBoundary(processId);
    }

    /// <summary>
    /// Signals that the external process was successfully started and may have begun externally
    /// visible work. Custom <see cref="IProcessRunner"/> implementations must invoke this method
    /// immediately after process start succeeds. The callback is invoked at most once.
    /// </summary>
    public void InvokeStartBoundary()
    {
        if (_startBoundary is null || Interlocked.Exchange(ref _startBoundaryInvoked, 1) != 0)
            return;
        _startBoundary();
    }

    /// <summary>
    /// Signals that the external process has completed and its final result is available.
    /// Custom <see cref="IProcessRunner"/> implementations must invoke this method immediately
    /// after observing process exit and before returning or performing any post-exit mutation.
    /// The exit state is final, but captured output may still be draining. The callback is invoked
    /// at most once, so service-level fallback calls are safe.
    /// </summary>
    /// <param name="result">The final process result.</param>
    public void InvokeCompletionBoundary(ProcessRunResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (_completionBoundary is null || Interlocked.Exchange(ref _completionBoundaryInvoked, 1) != 0)
            return;
        _completionBoundary(result);
    }
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
    {
        ExitCode = exitCode;
        StdOut = stdOut;
        StdErr = stdErr;
        Executable = executable;
        Duration = duration;
        TimedOut = timedOut;
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

    /// <summary>Whether standard output exceeded the configured retained-character limit.</summary>
    public bool StandardOutputLimitExceeded { get; }

    /// <summary>Whether standard error exceeded the configured retained-character limit.</summary>
    public bool StandardErrorLimitExceeded { get; }

    /// <summary>
    /// Gets a value indicating whether the process completed successfully.
    /// </summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut && !StandardOutputLimitExceeded && !StandardErrorLimitExceeded;
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
    /// <remarks>
    /// Implementations must call <see cref="ProcessRunRequest.InvokeStartedProcessBoundary"/> and then
    /// <see cref="ProcessRunRequest.InvokeStartBoundary"/> immediately after the process starts successfully.
    /// They must also call
    /// <see cref="ProcessRunRequest.InvokeCompletionBoundary"/> immediately
    /// after the process exits and the final result is constructed, before returning from this method
    /// or performing any post-exit mutation of producer outputs.
    /// </remarks>
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
        if (request.MaxCapturedOutputCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "The captured-output character limit must be positive.");

        using var process = new Process {
            StartInfo = BuildStartInfo(request)
        };

        var stopwatch = Stopwatch.StartNew();
        var started = false;
        try
        {
            process.Start();
            started = true;
            request.InvokeStartedProcessBoundary(process.Id);
            request.InvokeStartBoundary();
        }
        catch (Exception ex)
        {
            if (started) TryKill(process);
            stopwatch.Stop();
            var boundaryTimedOut = started && ex is TimeoutException;
            var failedStart = new ProcessRunResult(
                boundaryTimedOut ? 124 : 127,
                string.Empty,
                boundaryTimedOut ? "Timeout" : ex.Message,
                request.FileName,
                stopwatch.Elapsed,
                boundaryTimedOut);
            request.InvokeCompletionBoundary(failedStart);
            return failedStart;
        }

        var stdoutTask = request.CaptureOutput
            ? ReadOutputAsync(process.StandardOutput, request.OutputLineReceived, request.MaxCapturedOutputCharacters)
            : Task.FromResult(CapturedOutput.Empty);
        var stderrTask = request.CaptureError
            ? ReadOutputAsync(process.StandardError, request.ErrorLineReceived, request.MaxCapturedOutputCharacters)
            : Task.FromResult(CapturedOutput.Empty);
        var timedOut = false;

        try
        {
            var remainingTimeout = request.Timeout;
            if (request.Timeout > TimeSpan.Zero && request.Timeout != Timeout.InfiniteTimeSpan)
            {
                remainingTimeout = request.Timeout - stopwatch.Elapsed;
                if (remainingTimeout <= TimeSpan.Zero)
                    throw new OperationCanceledException();
            }
            await WaitForExitAsync(process, remainingTimeout, cancellationToken).ConfigureAwait(false);
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

        // Bind producer-owned filesystem output at the first observable process-exit
        // boundary. Stream drainage happens afterward so a blocked or inherited pipe
        // cannot create an unmonitored post-exit replacement window.
        var exitCode = timedOut ? 124 : SafeGetExitCode(process);
        var boundaryResult = new ProcessRunResult(
            exitCode,
            string.Empty,
            timedOut ? "Timeout" : string.Empty,
            process.StartInfo.FileName ?? request.FileName,
            stopwatch.Elapsed,
            timedOut);
        request.InvokeCompletionBoundary(boundaryResult);

        var stdout = request.CaptureOutput
            ? await DrainAsync(stdoutTask).ConfigureAwait(false)
            : CapturedOutput.Empty;
        var stderr = request.CaptureError
            ? await DrainAsync(stderrTask).ConfigureAwait(false)
            : CapturedOutput.Empty;
        stopwatch.Stop();

        if (timedOut && string.IsNullOrWhiteSpace(stderr.Value))
            stderr = new CapturedOutput("Timeout", stderr.LimitExceeded);

        var result = new ProcessRunResult(
            exitCode,
            stdout.Value,
            stderr.Value,
            process.StartInfo.FileName ?? request.FileName,
            stopwatch.Elapsed,
            timedOut,
            stdout.LimitExceeded,
            stderr.LimitExceeded);
        request.InvokeCompletionBoundary(result);
        return result;
    }

    private static async Task<CapturedOutput> ReadOutputAsync(
        StreamReader reader,
        Action<string>? lineReceived,
        int maximumCharacters)
    {
        if (lineReceived is null)
        {
            var directOutput = new StringBuilder(Math.Min(maximumCharacters, 4096));
            var buffer = new char[4096];
            var exceeded = false;
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                var remaining = maximumCharacters - directOutput.Length;
                if (remaining > 0) directOutput.Append(buffer, 0, Math.Min(remaining, read));
                if (read > remaining) exceeded = true;
            }
            return new CapturedOutput(directOutput.ToString(), exceeded);
        }

        var output = new StringBuilder();
        var limitExceeded = false;
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            var retainedLine = line + Environment.NewLine;
            var remaining = maximumCharacters - output.Length;
            if (remaining > 0) output.Append(retainedLine, 0, Math.Min(remaining, retainedLine.Length));
            if (retainedLine.Length > remaining) limitExceeded = true;
            try { lineReceived(line); } catch { }
        }

        return new CapturedOutput(output.ToString(), limitExceeded);
    }

    private static ProcessStartInfo BuildStartInfo(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = request.CaptureOutput,
            RedirectStandardError = request.CaptureError,
            UseShellExecute = false,
            CreateNoWindow = request.CaptureOutput || request.CaptureError
        };

        ProcessStartInfoEncoding.TryApplyUtf8(startInfo);

        if (!request.InheritEnvironment)
            startInfo.EnvironmentVariables.Clear();

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

    private static async Task<CapturedOutput> DrainAsync(Task<CapturedOutput> readTask)
    {
        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch
        {
            return CapturedOutput.Empty;
        }
    }

    private sealed class CapturedOutput
    {
        internal static CapturedOutput Empty { get; } = new(string.Empty, false);

        internal CapturedOutput(string value, bool limitExceeded)
        {
            Value = value;
            LimitExceeded = limitExceeded;
        }

        internal string Value { get; }
        internal bool LimitExceeded { get; }
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
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        using var treeKill = Process.Start(new ProcessStartInfo
                        {
                            FileName = GetWindowsTaskKillPath(),
                            Arguments = $"/PID {process.Id} /T /F",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        treeKill?.WaitForExit(5000);
                    }
                    catch
                    {
                        // Fall through to the direct-process kill below.
                    }
                }
                if (!process.HasExited)
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
    internal static string GetWindowsTaskKillPath()
        => Path.Combine(Environment.SystemDirectory, "taskkill.exe");
#endif

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
