using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

/// <summary>
/// Registers the complete plan in canonical order and updates rows in place.
/// The shared live viewport keeps current work visible without filtering, removing,
/// or re-adding planned tasks.
/// </summary>
internal sealed class SpectreProgressLedger
{
    private readonly ProgressContext _context;
    private readonly SpectreProgressPresentation? _presentation;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProgressTask> _visibleTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    internal SpectreProgressLedger(
        ProgressContext context,
        SpectreProgressPresentation? presentation = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _presentation = presentation;
    }

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

            RefreshTasks();
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

            // Spectre keeps task insertion order. The full plan is materialized
            // before any updates so an out-of-order start cannot move a row.
            RefreshTasks();
            ApplyUpdate(entry, state, detail);
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
                ApplyUpdate(
                    entry,
                    state,
                    success ? "not required" : "skipped after failure");
            }

            RefreshTasks();
            _context.Refresh();
        }
    }

    internal void ClearLiveTasks()
    {
        lock (_sync)
        {
            foreach (var task in _visibleTasks.Values.ToArray())
            {
                _context.RemoveTask(task);
                _presentation?.Remove(task);
            }

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
                    entry.Item.Target,
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
            var result = string.Join(
                " — ",
                new[] { snapshot.Target, snapshot.Detail }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            table.AddRow(
                $"[{color}]{icon}[/]",
                $"[{color}]{Esc($"{ordinal} {snapshot.Title}")}[/]",
                $"[{color}]{Esc(result)}[/]",
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
        _presentation?.Register(task, entry.Item.Kind);
        return task;
    }

    private void ApplyUpdate(
        Entry entry,
        SpectreProgressLedgerState state,
        string? detail)
    {
        entry.State = state;
        entry.Detail = detail;
        entry.ProgressFraction = ResolveProgressFraction(entry.Item, state);

        var task = EnsureVisibleTask(entry);
        task.Description = BuildLiveLabel(entry);

        if (state == SpectreProgressLedgerState.Started)
        {
            if (!task.IsStarted)
                task.StartTask();

            _presentation?.MarkStarted(task, entry.Item.Kind);
            task.IsIndeterminate = entry.Item.ProgressMaximum <= 0;
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
            entry.Duration = entry.Item.Duration ?? task.ElapsedTime ?? TimeSpan.Zero;
            _presentation?.MarkTerminal(task, state, entry.Duration);
        }
    }

    private void RefreshTasks()
    {
        foreach (var entry in _entries.Values
                     .OrderBy(entry => entry.Item.GroupOrder)
                     .ThenBy(entry => entry.Item.Position <= 0 ? int.MaxValue : entry.Item.Position)
                     .ThenBy(entry => entry.Item.Title, StringComparer.OrdinalIgnoreCase))
            EnsureVisibleTask(entry).Description = BuildLiveLabel(entry);
    }

    private string BuildLiveLabel(Entry entry)
    {
        if (_presentation is not null)
            return _presentation.BuildLabel(entry.Item, entry.Detail);

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
    internal string? Target { get; set; }
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
        string? target,
        SpectreProgressLedgerState state,
        string? detail,
        TimeSpan duration)
    {
        GroupTitle = groupTitle;
        Position = position;
        Total = total;
        Title = title;
        Target = target;
        State = state;
        Detail = detail;
        Duration = duration;
    }

    internal string GroupTitle { get; }
    internal int Position { get; }
    internal int Total { get; }
    internal string Title { get; }
    internal string? Target { get; }
    internal SpectreProgressLedgerState State { get; }
    internal string? Detail { get; }
    internal TimeSpan Duration { get; }
}
