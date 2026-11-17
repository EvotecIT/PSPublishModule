using System.Diagnostics;
using System.Threading;

namespace PowerForge;

/// <summary>
/// Forwards child-process lines to a progress detail at a bounded rate, so a chatty process
/// cannot flood the host. Lines arriving inside the interval are dropped; the detail is a
/// "latest activity" hint, not a log (the full output is still captured by the runner).
/// </summary>
internal static class ProgressDetailThrottle
{
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);
    private const int MaxDetailLength = 200;

    public static Action<string> Create(Action<string> report, TimeSpan? interval = null)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));

        var intervalTicks = (long)((interval ?? DefaultInterval).TotalSeconds * Stopwatch.Frequency);
        long nextAllowed = 0;
        return line =>
        {
            var detail = Normalize(line);
            if (detail is null) return;

            var now = Stopwatch.GetTimestamp();
            var due = Interlocked.Read(ref nextAllowed);
            if (now < due || Interlocked.CompareExchange(ref nextAllowed, now + intervalTicks, due) != due)
                return;

            try { report(detail); } catch { /* progress is best effort */ }
        };
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
