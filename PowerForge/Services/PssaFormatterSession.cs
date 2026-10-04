using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Owns one isolated formatter process for sequential build phases. Each request has
/// its own deadline and captured output; an interrupted host is discarded before reuse.
/// </summary>
internal sealed class PssaFormatterSession : IDisposable
{
    private readonly ICancellablePowerShellRunner _runner;
    private readonly Action _released;
    private readonly object _requests = new();
    private readonly object _output = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PowerForge", "pssa", $"session_{Guid.NewGuid():N}");
    private CancellationTokenSource? _cancellation;
    private Task<PowerShellRunResult>? _process;
    private Phase? _phase;
    private bool _disposed;

    internal PssaFormatterSession(ICancellablePowerShellRunner runner, Action released)
    {
        _runner = runner;
        _released = released;
    }

    internal PowerShellRunResult Run(string script, string batchPath, TimeSpan timeout)
    {
        lock (_requests)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PssaFormatterSession));
            var phase = new Phase();
            lock (_output) _phase = phase;
            using var deadlineCancellation = new CancellationTokenSource();
            var deadline = Task.Delay(timeout > TimeSpan.Zero ? timeout : Timeout.InfiniteTimeSpan, deadlineCancellation.Token);
            try
            {
                Start(script);
                var requestPath = Path.Combine(_directory, "request.json");
                File.WriteAllText(requestPath + ".tmp", JsonSerializer.Serialize(new
                {
                    phase.Id,
                    BatchPath = batchPath,
                    WorkingDirectory = Environment.CurrentDirectory
                }), new UTF8Encoding(false));
                File.Move(requestPath + ".tmp", requestPath);

                var completed = Task.WhenAny(phase.Completion.Task, _process!, deadline).GetAwaiter().GetResult();
                if (completed == phase.Completion.Task)
                    return Capture(phase, 0, string.Empty);

                if (completed == deadline)
                {
                    // Wait for cancellation/output drainage before taking the snapshot, so
                    // completed file errors cannot disappear behind unfinished-file timeouts.
                    Stop(force: true);
                    return Capture(phase, 124, string.Empty);
                }

                var result = _process!.GetAwaiter().GetResult();
                Stop(force: true);
                return Capture(phase, result.ExitCode, result.Executable);
            }
            catch
            {
                Stop(force: true);
                throw;
            }
            finally
            {
                deadlineCancellation.Cancel();
                lock (_output) _phase = null;
            }
        }
    }

    private void Start(string script)
    {
        if (_process?.IsCompleted == true) Stop(force: true);
        if (_process is not null) return;
        Directory.CreateDirectory(_directory);
        var scriptPath = Path.Combine(_directory, "formatter.ps1");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
        _cancellation = new CancellationTokenSource();
        _process = _runner.RunAsync(new PowerShellRunRequest(
            scriptPath,
            new[] { "-SessionDirectory", _directory, "-ParentProcessId", Process.GetCurrentProcess().Id.ToString() },
            Timeout.InfiniteTimeSpan,
            preferPwsh: true,
            workingDirectory: Environment.CurrentDirectory,
            environmentVariables: null,
            executableOverride: null,
            captureOutput: true,
            captureError: true,
            outputLineReceived: ReceiveOutput,
            errorLineReceived: ReceiveError), _cancellation.Token);
    }

    private void ReceiveOutput(string line)
    {
        lock (_output)
        {
            if (_phase is null) return;
            if (line == "PSSA_PHASE_DONE::" + _phase.Id)
                _phase.Completion.TrySetResult(true);
            else
                _phase.Output.AppendLine(line);
        }
    }

    private void ReceiveError(string line)
    {
        lock (_output) _phase?.Error.AppendLine(line);
    }

    private PowerShellRunResult Capture(Phase phase, int exitCode, string executable)
    {
        lock (_output)
            return new PowerShellRunResult(exitCode, phase.Output.ToString(), phase.Error.ToString(), executable);
    }

    private void Stop(bool force)
    {
        if (_process is not null)
        {
            try
            {
                if (!_process.IsCompleted)
                {
                    if (!force)
                    {
                        File.WriteAllText(Path.Combine(_directory, "stop"), string.Empty);
                        force = !_process.Wait(TimeSpan.FromSeconds(2));
                    }
                    if (force) _cancellation!.Cancel();
                }
                _process.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            catch { /* The active request observes process faults; disposal must not mask its failure. */ }
            finally
            {
                _process = null;
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }
        foreach (var name in new[] { "request.json", "request.json.tmp", "stop" })
        {
            try { File.Delete(Path.Combine(_directory, name)); } catch { /* best effort */ }
        }
    }

    public void Dispose()
    {
        lock (_requests)
        {
            if (_disposed) return;
            _disposed = true;
            try { Stop(force: false); }
            finally
            {
                try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
                _released();
            }
        }
    }

    private sealed class Phase
    {
        internal string Id { get; } = Guid.NewGuid().ToString("N");
        internal StringBuilder Output { get; } = new();
        internal StringBuilder Error { get; } = new();
        internal TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
