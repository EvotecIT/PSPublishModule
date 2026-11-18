using System.Diagnostics;
using System.Threading;

namespace PowerForge;

/// <summary>
/// Forwards child-process lines to a progress detail at a bounded rate, so a chatty process
/// cannot flood the host. Lines arriving inside the interval replace each other, and the newest
/// one is sent when the interval ends, so a process that goes quiet still shows its last line.
/// The detail is a "latest activity" hint, not a log (the full output is still captured by the runner).
/// </summary>
internal static class ProgressDetailThrottle
{
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);
    private const int MaxDetailLength = 200;

    public static Action<string> Create(Action<string> report, TimeSpan? interval = null)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));
        return new Sink(report, interval ?? DefaultInterval).Post;
    }

    private sealed class Sink
    {
        private readonly object _gate = new();
        private readonly Action<string> _report;
        private readonly TimeSpan _interval;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private TimeSpan _nextAllowed = TimeSpan.Zero;
        private string? _pending;
        private Timer? _flush;

        public Sink(Action<string> report, TimeSpan interval)
        {
            _report = report;
            _interval = interval;
        }

        public void Post(string line)
        {
            var detail = Normalize(line);
            if (detail is null) return;

            lock (_gate)
            {
                var now = _clock.Elapsed;
                if (now < _nextAllowed)
                {
                    // Keep only the newest line; one timer sends it when the interval ends.
                    _pending = detail;
                    _flush ??= new Timer(_ => Flush(), null, _nextAllowed - now, Timeout.InfiniteTimeSpan);
                    return;
                }

                _nextAllowed = now + _interval;
                _pending = null;
            }

            Report(detail);
        }

        private void Flush()
        {
            string? detail;
            lock (_gate)
            {
                detail = _pending;
                _pending = null;
                _flush?.Dispose();
                _flush = null;
                if (detail is not null) _nextAllowed = _clock.Elapsed + _interval;
            }

            if (detail is not null) Report(detail);
        }

        private void Report(string detail)
        {
            try { _report(detail); } catch { /* progress is best effort */ }
        }
    }

    /// <summary>Creates a throttled detail sink for a running module pipeline step, or null when the host cannot show details.</summary>
    public static Action<string>? ForStep(IModulePipelineProgressReporter? reporter, ModulePipelineStep? step)
        => reporter is IModulePipelineProgressReporterV3 v3 && step is not null
            ? Create(detail => v3.StepProgress(step, 0, 0, detail))
            : null;

    private static string? Normalize(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var text = line!.Trim();
        var newLine = text.IndexOfAny(new[] { '\r', '\n' });
        if (newLine >= 0) text = text.Substring(0, newLine).TrimEnd();
        return text.Length <= MaxDetailLength ? text : text.Substring(0, MaxDetailLength - 3) + "...";
    }
}
