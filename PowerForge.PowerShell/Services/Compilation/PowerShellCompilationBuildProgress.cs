using System.Diagnostics;

namespace PowerForge;

/// <summary>
/// Reports artifact build phases and the generated .NET restore/build output to an optional logger.
/// A null logger makes every call a no-op, so the builder behaves exactly as it did without reporting.
/// </summary>
internal sealed class PowerShellCompilationBuildProgress
{
    private readonly ILogger? _logger;
    private readonly Stopwatch _phase = Stopwatch.StartNew();
    private readonly HashSet<string> _surfacedDiagnostics = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    internal PowerShellCompilationBuildProgress(ILogger? logger) => _logger = logger;

    internal static PowerShellCompilationBuildProgress None { get; } = new(null);

    /// <summary>Writes the opening line of a build and starts timing its first phase.</summary>
    internal void Announce(string message)
    {
        if (_logger is null) return;
        Write(() => _logger.Info(message));
        _phase.Restart();
    }

    /// <summary>Writes one line for a finished quick phase with its elapsed time and starts timing the next one.</summary>
    internal void Completed(string message)
    {
        if (_logger is null) return;
        Write(() => _logger.Info($"{message} ({Format(_phase.Elapsed)})"));
        _phase.Restart();
    }

    /// <summary>
    /// Announces a generated <c>dotnet</c> process, keeps a heartbeat running while it works and streams its
    /// output: warning and error lines are shown as they arrive (once each), everything else goes to Verbose.
    /// </summary>
    internal ProcessScope StartProcess(string label)
    {
        if (_logger is null) return new ProcessScope(this, label, heartbeat: null);
        _phase.Restart();
        Write(() => _logger.Info(label + "..."));
        return new ProcessScope(this, label, OperationHeartbeat.Start(_logger, label));
    }

    /// <summary>Returns the request with line callbacks that stream to the logger, or the request itself when reporting is off.</summary>
    internal ProcessRunRequest Stream(ProcessRunRequest request)
    {
        if (_logger is null) return request;
        return new ProcessRunRequest(
            request.FileName,
            request.WorkingDirectory,
            request.Arguments,
            request.Timeout,
            request.EnvironmentVariables,
            request.CaptureOutput,
            request.CaptureError,
            OnLine,
            OnLine,
            request.InheritEnvironment)
        {
            MaxCapturedOutputCharacters = request.MaxCapturedOutputCharacters,
            RequireDirectStart = request.RequireDirectStart
        };
    }

    private void OnLine(string line)
    {
        if (_logger is null || string.IsNullOrWhiteSpace(line)) return;
        var kind = PowerShellCompilationBuildReport.ClassifyBuildLine(line);
        if (kind == PowerShellCompilationBuildLineKind.Other)
        {
            if (_logger.IsVerbose) Write(() => _logger.Verbose(line.TrimEnd()));
            return;
        }

        var normalized = PowerShellCompilationBuildReport.NormalizeBuildLine(line);
        lock (_gate)
        {
            // MSBuild repeats every warning and error in its closing summary.
            if (!_surfacedDiagnostics.Add(normalized)) return;
        }
        Write(() =>
        {
            if (kind == PowerShellCompilationBuildLineKind.Warning) _logger.Warn(normalized);
            else _logger.Info(normalized);
        });
    }

    private static void Write(Action write)
    {
        try { write(); } catch { /* reporting must never fail the build */ }
    }

    internal static string Format(TimeSpan elapsed) => DotNetRepositoryReleaseService.FormatDuration(elapsed);

    /// <summary>Ends one generated process report.</summary>
    internal sealed class ProcessScope : IDisposable
    {
        private readonly PowerShellCompilationBuildProgress _owner;
        private readonly string _label;
        private readonly OperationHeartbeat? _heartbeat;
        private bool _disposed;

        internal ProcessScope(PowerShellCompilationBuildProgress owner, string label, OperationHeartbeat? heartbeat)
        {
            _owner = owner;
            _label = label;
            _heartbeat = heartbeat;
        }

        /// <summary>Reports how the process ended.</summary>
        internal void Finished(int exitCode, bool timedOut)
        {
            var logger = _owner._logger;
            if (logger is null) return;
            var elapsed = Format(_owner._phase.Elapsed);
            Write(() =>
            {
                if (timedOut) logger.Warn($"{_label} timed out after {elapsed}");
                else if (exitCode != 0) logger.Warn($"{_label} failed with exit code {exitCode} after {elapsed}");
                else logger.Info($"{_label} finished in {elapsed}");
            });
            _owner._phase.Restart();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _heartbeat?.Dispose();
        }
    }
}
