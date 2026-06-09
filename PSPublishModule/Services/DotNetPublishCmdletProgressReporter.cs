using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal sealed class DotNetPublishCmdletProgressReporter : IDotNetPublishProgressReporter
{
    private const int ActivityId = 3081;
    private readonly Action<string> _writeLine;
    private readonly Action<ProgressRecord> _writeProgress;
    private readonly int _totalSteps;
    private readonly Dictionary<string, ActiveStep> _activeSteps = new(StringComparer.Ordinal);
    private int _startedSteps;

    public DotNetPublishCmdletProgressReporter(
        Action<string> writeLine,
        Action<ProgressRecord> writeProgress,
        int totalSteps)
    {
        _writeLine = writeLine ?? throw new ArgumentNullException(nameof(writeLine));
        _writeProgress = writeProgress ?? throw new ArgumentNullException(nameof(writeProgress));
        _totalSteps = Math.Max(totalSteps, 1);
    }

    public void StepStarting(DotNetPublishStep step)
    {
        var index = Math.Min(++_startedSteps, _totalSteps);
        var description = Describe(step);
        _activeSteps[StepKey(step)] = new ActiveStep(index, Stopwatch.StartNew(), description);
        WriteLine($"[{index}/{_totalSteps}] {description}...");
        WriteProgress(index, description, ProgressRecordType.Processing);
    }

    public void StepCompleted(DotNetPublishStep step)
    {
        var active = Complete(step);
        WriteLine($"[{active.Index}/{_totalSteps}] {active.Description} completed in {FormatElapsed(active.Elapsed)}.");
        WriteProgress(active.Index, active.Description, ProgressRecordType.Processing);
        if (active.Index >= _totalSteps)
            WriteProgress(active.Index, "Completed", ProgressRecordType.Completed);
    }

    public void StepFailed(DotNetPublishStep step, Exception error)
    {
        var active = Complete(step);
        WriteLine($"[{active.Index}/{_totalSteps}] {active.Description} failed after {FormatElapsed(active.Elapsed)}: {error.GetBaseException().Message}");
        WriteProgress(active.Index, active.Description, ProgressRecordType.Completed);
    }

    private ActiveStep Complete(DotNetPublishStep step)
    {
        var key = StepKey(step);
        if (_activeSteps.TryGetValue(key, out var active))
        {
            active.Stopwatch.Stop();
            _activeSteps.Remove(key);
            active.Elapsed = active.Stopwatch.Elapsed;
            return active;
        }

        return new ActiveStep(Math.Max(_startedSteps, 1), Stopwatch.StartNew(), Describe(step));
    }

    private void WriteProgress(int index, string status, ProgressRecordType recordType)
    {
        var percent = recordType == ProgressRecordType.Completed
            ? 100
            : Math.Min(99, Math.Max(0, (int)Math.Round(index * 100d / _totalSteps)));
        SafeInvoke(() => _writeProgress(new ProgressRecord(ActivityId, "DotNet publish", status)
        {
            PercentComplete = percent,
            RecordType = recordType
        }));
    }

    private void WriteLine(string message)
        => SafeInvoke(() => _writeLine(message));

    private static void SafeInvoke(Action action)
    {
        try { action(); }
        catch { }
    }

    private static string StepKey(DotNetPublishStep step)
        => string.IsNullOrWhiteSpace(step.Key) ? step.Kind.ToString() : step.Key!;

    private static string Describe(DotNetPublishStep step)
    {
        var title = string.IsNullOrWhiteSpace(step.Title) ? step.Kind.ToString() : step.Title.Trim();
        var details = new List<string>();
        Add(details, step.InstallerId);
        Add(details, step.TargetName);
        Add(details, step.BundleId);
        Add(details, step.StorePackageId);
        Add(details, step.Framework);
        Add(details, step.Runtime);
        if (step.Style.HasValue)
            details.Add(step.Style.Value.ToString());

        return details.Count == 0
            ? title
            : title + " " + string.Join(" ", details);
    }

    private static void Add(List<string> values, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        values.Add(value!.Trim());
    }

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalSeconds < 60
            ? elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
            : elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private sealed class ActiveStep
    {
        public ActiveStep(int index, Stopwatch stopwatch, string description)
        {
            Index = index;
            Stopwatch = stopwatch;
            Description = description;
        }

        public int Index { get; }

        public Stopwatch Stopwatch { get; }

        public string Description { get; }

        public TimeSpan Elapsed { get; set; }
    }
}
