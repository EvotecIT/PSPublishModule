using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

/// <summary>
/// Keeps detailed work visible without allowing a Spectre live region to grow with
/// every planned item. The complete history is retained for a durable post-run ledger.
/// </summary>
internal sealed class SpectreBoundedProgressLedger
{
    private const int RecentCompletedLimit = 3;
    private const int ActiveLimit = 2;
    private const int UpcomingLimit = 2;
    private const int PinnedFailureLimit = 3;

    private readonly ProgressContext _context;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProgressTask> _visibleTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private long _terminalSequence;

    internal SpectreBoundedProgressLedger(ProgressContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    internal void Plan(IEnumerable<SpectreProgressLedgerItem> items)
    {
        if (items is null)
            return;

        lock (_sync)
        {
            foreach (var item in items)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Key))
                    continue;

                if (_entries.TryGetValue(item.Key, out var existing))
                {
                    existing.Item = item;
                    continue;
                }

                _entries[item.Key] = new Entry(item);
            }

            ReconcileVisibleTasks();
        }
    }

    internal void Update(
        SpectreProgressLedgerItem item,
        SpectreProgressLedgerState state,
        string? detail)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Key))
            return;

        lock (_sync)
        {
            if (!_entries.TryGetValue(item.Key, out var entry))
            {
                entry = new Entry(item);
                _entries[item.Key] = entry;
            }
            else
            {
                entry.Item = item;
            }

            entry.State = state;
            entry.Detail = detail;
            entry.ProgressFraction = ResolveProgressFraction(item, state);

            var task = EnsureVisibleTask(entry);
            task.Description = BuildLiveLabel(entry);

            if (state == SpectreProgressLedgerState.Started)
            {
                if (!task.IsStarted)
                    task.StartTask();

                task.IsIndeterminate = item.ProgressMaximum <= 0;
                if (!task.IsIndeterminate)
                    task.Value = task.MaxValue * entry.ProgressFraction;
            }
            else if (IsTerminal(state))
            {
                if (!task.IsStarted)
                    task.StartTask();

                task.IsIndeterminate = false;
                task.Value = task.MaxValue;
                task.StopTask();

                entry.Duration = item.Duration ?? task.ElapsedTime ?? TimeSpan.Zero;
                entry.TerminalSequence = ++_terminalSequence;
            }

            ReconcileVisibleTasks();
            _context.Refresh();
        }
    }

    internal double GetCompletionRatio(string groupKey)
    {
        lock (_sync)
        {
            var entries = _entries.Values
                .Where(entry => string.Equals(entry.Item.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (entries.Length == 0)
                return 0;

            return entries.Sum(entry => entry.ProgressFraction) / entries.Length;
        }
    }

    internal int GetItemCount(string groupKey)
    {
        lock (_sync)
        {
            return _entries.Values.Count(entry =>
                string.Equals(entry.Item.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal int VisibleTaskCount
    {
        get
        {
            lock (_sync)
                return _visibleTasks.Count;
        }
    }

    internal TimeSpan? GetVisibleElapsedTime(string key)
    {
        lock (_sync)
            return _visibleTasks.TryGetValue(key, out var task) ? task.ElapsedTime : null;
    }

    internal void FinishRemaining(bool success)
    {
        lock (_sync)
        {
            foreach (var entry in _entries.Values.Where(entry => !IsTerminal(entry.State)).ToArray())
            {
                var state = entry.State == SpectreProgressLedgerState.Started && !success
                    ? SpectreProgressLedgerState.Failed
                    : SpectreProgressLedgerState.Skipped;
                Update(
                    entry.Item,
                    state,
                    success ? "not required" : "skipped after failure");
            }
        }
    }

    internal void ClearLiveTasks()
    {
        lock (_sync)
        {
            foreach (var task in _visibleTasks.Values.ToArray())
                _context.RemoveTask(task);

            _visibleTasks.Clear();
            _context.Refresh();
        }
    }

    internal IReadOnlyList<SpectreProgressLedgerSnapshot> GetSnapshots()
    {
        lock (_sync)
        {
            return _entries.Values
                .OrderBy(entry => entry.Item.GroupOrder)
                .ThenBy(entry => entry.Item.Position <= 0 ? int.MaxValue : entry.Item.Position)
                .ThenBy(entry => entry.Item.Title, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new SpectreProgressLedgerSnapshot(
                    entry.Item.GroupTitle,
                    entry.Item.Position,
                    entry.Item.Total,
                    entry.Item.Title,
                    entry.State,
                    entry.Detail,
                    entry.Duration ?? entry.Item.Duration ?? TimeSpan.Zero))
                .ToArray();
        }
    }

    internal static void WriteLedger(
        IAnsiConsole console,
        IReadOnlyList<SpectreProgressLedgerSnapshot> snapshots,
        string title)
    {
        if (console is null) throw new ArgumentNullException(nameof(console));
        if (snapshots is null || snapshots.Count == 0) return;

        static string Esc(string? value) => Markup.Escape(value ?? string.Empty);
        var unicode = ConsoleEncoding.ShouldRenderUnicode(console.Profile.Capabilities.Unicode);
        console.Write(new Rule($"[yellow]{Esc(title)}[/]").LeftJustified());

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Status").NoWrap().Width(1))
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Result"))
            .AddColumn(new TableColumn("Duration").RightAligned().NoWrap());

        string? currentGroup = null;
        foreach (var snapshot in snapshots)
        {
            if (!string.Equals(currentGroup, snapshot.GroupTitle, StringComparison.Ordinal))
            {
                currentGroup = snapshot.GroupTitle;
                table.AddRow(
                    string.Empty,
                    $"[grey bold]{Esc(currentGroup)}[/]",
                    string.Empty,
                    string.Empty);
            }

            var (icon, color) = GetStateVisual(snapshot.State, unicode);
            var ordinal = snapshot.Position > 0 && snapshot.Total > 0
                ? $"{snapshot.Position:00}/{snapshot.Total:00}"
                : "–";
            table.AddRow(
                $"[{color}]{icon}[/]",
                $"[{color}]{Esc($"{ordinal} {snapshot.Title}")}[/]",
                $"[{color}]{Esc(snapshot.Detail)}[/]",
                $"[deepskyblue1]{Esc(FormatDuration(snapshot.Duration))}[/]");
        }

        console.Write(table);
    }

    private ProgressTask EnsureVisibleTask(Entry entry)
    {
        if (_visibleTasks.TryGetValue(entry.Item.Key, out var task))
            return task;

        task = _context.AddTask(
            BuildLiveLabel(entry),
            maxValue: 1,
            autoStart: false);
        _visibleTasks[entry.Item.Key] = task;
        return task;
    }

    private void ReconcileVisibleTasks()
    {
        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _entries.Values
                     .Where(entry => entry.State == SpectreProgressLedgerState.Started)
                     .OrderBy(entry => entry.Item.GroupOrder)
                     .ThenBy(entry => entry.Item.Position <= 0 ? int.MaxValue : entry.Item.Position)
                     .Take(ActiveLimit))
        {
            desired.Add(entry.Item.Key);
        }

        foreach (var entry in _entries.Values
                     .Where(entry => entry.State == SpectreProgressLedgerState.Completed ||
                                     entry.State == SpectreProgressLedgerState.Skipped)
                     .OrderByDescending(entry => entry.TerminalSequence)
                     .Take(RecentCompletedLimit))
        {
            desired.Add(entry.Item.Key);
        }

        foreach (var entry in _entries.Values
                     .Where(entry => entry.State == SpectreProgressLedgerState.Failed)
                     .OrderByDescending(entry => entry.TerminalSequence)
                     .Take(PinnedFailureLimit))
        {
            desired.Add(entry.Item.Key);
        }

        foreach (var entry in _entries.Values
                     .Where(entry => entry.State == SpectreProgressLedgerState.Planned)
                     .OrderBy(entry => entry.Item.GroupOrder)
                     .ThenBy(entry => entry.Item.Position <= 0 ? int.MaxValue : entry.Item.Position)
                     .Take(UpcomingLimit))
        {
            desired.Add(entry.Item.Key);
        }

        foreach (var key in _visibleTasks.Keys.Where(key => !desired.Contains(key)).ToArray())
        {
            _context.RemoveTask(_visibleTasks[key]);
            _visibleTasks.Remove(key);
        }

        foreach (var entry in _entries.Values.Where(entry => desired.Contains(entry.Item.Key)))
            EnsureVisibleTask(entry).Description = BuildLiveLabel(entry);
    }

    private static string BuildLiveLabel(Entry entry)
    {
        var unicode = ConsoleEncoding.ShouldRenderUnicode(AnsiConsole.Profile.Capabilities.Unicode);
        var (icon, color) = GetStateVisual(entry.State, unicode);
        var ordinal = entry.Item.Position > 0 && entry.Item.Total > 0
            ? $"{entry.Item.Position:00}/{entry.Item.Total:00} "
            : string.Empty;
        var suffix = string.IsNullOrWhiteSpace(entry.Detail) ? string.Empty : $" — {entry.Detail}";
        return $"[{color}]{Markup.Escape($"  {icon} {ordinal}{entry.Item.Title}{suffix}")}[/]";
    }

    private static (string Icon, string Color) GetStateVisual(
        SpectreProgressLedgerState state,
        bool unicode)
        => state switch
        {
            SpectreProgressLedgerState.Started => (unicode ? "▶" : ">", "cyan"),
            SpectreProgressLedgerState.Completed => (unicode ? "✓" : "+", "green"),
            SpectreProgressLedgerState.Failed => ("x", "red"),
            SpectreProgressLedgerState.Skipped => (unicode ? "–" : "-", "grey"),
            _ => (unicode ? "·" : ".", "grey")
        };

    private static bool IsTerminal(SpectreProgressLedgerState state)
        => state == SpectreProgressLedgerState.Completed ||
           state == SpectreProgressLedgerState.Failed ||
           state == SpectreProgressLedgerState.Skipped;

    private static double ResolveProgressFraction(
        SpectreProgressLedgerItem item,
        SpectreProgressLedgerState state)
    {
        if (IsTerminal(state))
            return 1;
        if (item.ProgressMaximum <= 0)
            return 0;

        return Math.Min(1d, Math.Max(0d, item.ProgressValue / item.ProgressMaximum));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss\.fff");
    }

    private sealed class Entry
    {
        internal Entry(SpectreProgressLedgerItem item)
            => Item = item;

        internal SpectreProgressLedgerItem Item { get; set; }
        internal SpectreProgressLedgerState State { get; set; }
        internal string? Detail { get; set; }
        internal double ProgressFraction { get; set; }
        internal TimeSpan? Duration { get; set; }
        internal long TerminalSequence { get; set; }
    }
}

internal sealed class SpectreProgressLedgerItem
{
    internal string Key { get; set; } = string.Empty;
    internal string GroupKey { get; set; } = string.Empty;
    internal string GroupTitle { get; set; } = string.Empty;
    internal int GroupOrder { get; set; }
    internal string Title { get; set; } = string.Empty;
    internal string? Kind { get; set; }
    internal int Position { get; set; }
    internal int Total { get; set; }
    internal double ProgressValue { get; set; }
    internal double ProgressMaximum { get; set; }
    internal TimeSpan? Duration { get; set; }
}

internal enum SpectreProgressLedgerState
{
    Planned,
    Started,
    Completed,
    Failed,
    Skipped
}

internal sealed class SpectreProgressLedgerSnapshot
{
    internal SpectreProgressLedgerSnapshot(
        string groupTitle,
        int position,
        int total,
        string title,
        SpectreProgressLedgerState state,
        string? detail,
        TimeSpan duration)
    {
        GroupTitle = groupTitle;
        Position = position;
        Total = total;
        Title = title;
        State = state;
        Detail = detail;
        Duration = duration;
    }

    internal string GroupTitle { get; }
    internal int Position { get; }
    internal int Total { get; }
    internal string Title { get; }
    internal SpectreProgressLedgerState State { get; }
    internal string? Detail { get; }
    internal TimeSpan Duration { get; }
}
