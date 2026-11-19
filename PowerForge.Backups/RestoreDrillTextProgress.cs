using System.Diagnostics;
using System.Globalization;

namespace PowerForge.Backups;

/// <summary>
/// Writes a restore drill's progress as plain lines, for logs and redirected output: one line when a step starts or ends,
/// and a progress line for each running step at most every <see cref="Interval"/>, including steps that report no amount,
/// so the output never looks stalled.
/// </summary>
public sealed class RestoreDrillTextProgress : IRestoreDrillProgress
{
    private readonly object _gate = new();
    private readonly TextWriter _writer;
    private readonly Func<TimeSpan> _clock;
    private readonly Dictionary<string, Step> _steps = new(StringComparer.Ordinal);
    private bool _broken;

    /// <param name="writer">Where lines go.</param>
    /// <param name="clock">The elapsed-time source, or null for real time.</param>
    public RestoreDrillTextProgress(TextWriter writer, Func<TimeSpan>? clock = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        if (clock is null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed;
        }
        _clock = clock;
    }

    /// <summary>The least time between two progress lines of one step (default 10 seconds).</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public void Plan(string key, string label, long total, RestoreDrillUnit unit)
    {
        lock (_gate) _steps[key] = new Step(label, total, unit);
    }

    /// <inheritdoc />
    public void Start(string key, string? detail = null)
    {
        lock (_gate)
        {
            if (!_steps.TryGetValue(key, out var step)) return;
            step.StartedAt = step.LastLine = _clock();
            step.Running = true;
            Write("Started  " + step.Label + Suffix(detail));
        }
    }

    /// <inheritdoc />
    public void Report(string key, long done, string? detail = null)
    {
        lock (_gate)
        {
            if (!_steps.TryGetValue(key, out var step) || !step.Running) return;
            step.Done = done;
            if (detail is not null) step.Detail = detail;
            var now = _clock();
            if (now - step.LastLine < Interval) return;
            step.LastLine = now;
            Write("Working  " + step.Label + ": " + Amount(step, now));
        }
    }

    /// <summary>Writes a progress line for every running step that has been quiet for <see cref="Interval"/>.</summary>
    public void Heartbeat()
    {
        lock (_gate)
        {
            var now = _clock();
            foreach (var step in _steps.Values.Where(s => s.Running && now - s.LastLine >= Interval))
            {
                step.LastLine = now;
                Write("Working  " + step.Label + ": " + Amount(step, now));
            }
        }
    }

    /// <inheritdoc />
    public void Finish(string key, bool succeeded, string? detail = null)
    {
        lock (_gate)
        {
            if (!_steps.TryGetValue(key, out var step)) return;
            var took = step.Running ? _clock() - step.StartedAt : TimeSpan.Zero;
            step.Running = false;
            Write((succeeded ? "Passed   " : "Failed   ") + step.Label + " in " + Duration(took) + Suffix(detail));
        }
    }

    /// <summary>The amount done, the percentage, the running time and, when it can be estimated, the time left.</summary>
    internal static string Amount(long done, long total, RestoreDrillUnit unit, TimeSpan elapsed)
    {
        var parts = new List<string>();
        if (unit == RestoreDrillUnit.Bytes && total > 0)
            parts.Add(Gigabytes(done) + " of " + Gigabytes(total) + " GB (" + Percent(done, total) + ")");
        else if (unit == RestoreDrillUnit.Percent && total > 0)
            parts.Add(Percent(done, total));
        parts.Add(Duration(elapsed) + " elapsed");
        if (total > 0 && done > 0 && done < total && elapsed > TimeSpan.Zero)
            parts.Add("about " + Duration(TimeSpan.FromSeconds(elapsed.TotalSeconds * (total - done) / done)) + " left");
        return string.Join(", ", parts);
    }

    internal static string Duration(TimeSpan value)
        => value.TotalHours >= 1 ? ((int)value.TotalHours).ToString(CultureInfo.InvariantCulture) + "h " + value.Minutes.ToString("00", CultureInfo.InvariantCulture) + "m"
            : value.TotalMinutes >= 1 ? ((int)value.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m " + value.Seconds.ToString("00", CultureInfo.InvariantCulture) + "s"
            : Math.Max(0, (int)value.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";

    internal static string Gigabytes(long bytes) => (bytes / 1e9).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Percent(long done, long total) => ((int)Math.Floor(100.0 * Math.Min(done, total) / total)).ToString(CultureInfo.InvariantCulture) + "%";

    private string Amount(Step step, TimeSpan now) => Amount(step.Done, step.Total, step.Unit, now - step.StartedAt) + Suffix(step.Detail);

    private static string Suffix(string? detail) => string.IsNullOrWhiteSpace(detail) ? string.Empty : " (" + detail + ")";

    // Progress output never decides the drill's outcome: once the log can't be written (a full disk, a closed pipe),
    // further lines are dropped and the drill carries on to record its result.
    private void Write(string line)
    {
        if (_broken) return;
        try
        {
            _writer.WriteLine(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + line);
            _writer.Flush();
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or UnauthorizedAccessException) { _broken = true; }
    }

    private sealed class Step(string label, long total, RestoreDrillUnit unit)
    {
        internal string Label { get; } = label;
        internal long Total { get; } = total;
        internal RestoreDrillUnit Unit { get; } = unit;
        internal long Done { get; set; }
        internal string? Detail { get; set; }
        internal bool Running { get; set; }
        internal TimeSpan StartedAt { get; set; }
        internal TimeSpan LastLine { get; set; }
    }
}
