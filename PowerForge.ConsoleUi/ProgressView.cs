using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ConsoleMonster.Layout;
using ConsoleMonster.Live;
using ConsoleMonster.Styling;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;

namespace PowerForge.ConsoleUi;

/// <summary>State of a row in the progress view.</summary>
internal enum ProgressState
{
    Planned,
    Started,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// The live build view shared by module builds, project and package builds, executable publishing, unified releases
/// and single-operation status: a numbered step ledger (ConsoleMonster <see cref="Phases"/> with a step counter), a
/// "Current:" line that names the furthest running work and its latest detail (or shows that the next step is being
/// prepared), and a message log. Steps keep their planned order; items planned later are listed under their step,
/// and a finished step folds its items into a count while the live area is short. When the view ends, the complete
/// ledger and every message are printed in full. Every member is thread-safe.
/// </summary>
internal sealed partial class ProgressView
{
    // Phase item events address "step U+001F item" (see ConsoleMonster's Phases live operations).
    private const char ItemSeparator = '\u001f';
    private const string LedgerId = "steps";
    private const string LogId = "messages";
    private const int RowDetailLimit = 80;

    private static readonly AsyncLocal<ProgressView?> Active = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, Row> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Row> _steps = new();
    private readonly Phases _ledger = new(LedgerId) { StepCounter = true };
    private readonly LogRegion _log = new(LogId) { Wrap = true, MaxLinesPerEntry = 50, PreferredHeight = 6, ShowElapsed = false, Capacity = 10000 };
    private readonly List<(string Level, string Text)> _messages = new();
    private readonly Func<TimeSpan> _clock;
    private readonly string _separator = ConsoleOutput.CurrentProfile().Glyphs == ConsoleMonster.Capabilities.GlyphTier.Ascii ? " - " : " · ";
    private LiveSession? _session;
    private TimeSpan _idleSince;
    private bool _finished;

    /// <param name="clock">The session clock, or null for real time.</param>
    internal ProgressView(Func<TimeSpan>? clock = null)
    {
        if (clock is null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed;
        }
        _clock = clock;
    }

    /// <summary>The view running in the current flow, if any; loggers send their messages to it.</summary>
    internal static ProgressView? Current => Active.Value;

    /// <summary>Adds a top-level step, or updates the label of an existing one.</summary>
    internal void Step(string key, string label, string? kind = null, string? target = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        lock (_gate)
        {
            if (_rows.TryGetValue(key, out var existing))
            {
                existing.Label = label;
                return;
            }

            var row = new Row(key, null, label, kind, _steps.Count, 0) { Target = target };
            _rows[key] = row;
            _steps.Add(row);
            if (_session is null) _ledger.Set(key, StatusKind.Pending, LabelMarkup(row));
            else _session.Post(new LiveEvent(LedgerId, "add", key, text: LabelMarkup(row)));
        }
    }

    /// <summary>Adds an item under a step (created with <see cref="Step"/>), or updates its label.</summary>
    internal void Item(string step, string key, string label, string? kind = null, string? target = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        lock (_gate)
        {
            if (!_rows.TryGetValue(step, out var parent) || parent.Step is not null) return;
            if (_rows.TryGetValue(key, out var existing))
            {
                // Work can change what it is doing (a publish step that starts signing): keep the row, update its label.
                if (kind is not null) existing.Kind = kind;
                if (existing.Label == label) return;
                existing.Label = label;
                Send(new LiveEvent(LedgerId, "item", EventKey(existing), text: LabelMarkup(existing)));
                return;
            }

            var row = new Row(key, parent, label, kind, parent.Index, ++parent.ItemCount) { Target = target };
            _rows[key] = row;
            Send(new LiveEvent(LedgerId, "item", EventKey(row), text: LabelMarkup(row), status: nameof(StatusKind.Pending)));
        }
    }

    internal bool Contains(string key)
    {
        lock (_gate) return _rows.ContainsKey(key);
    }

    /// <summary>Keys of the top-level steps, in planned order.</summary>
    internal IReadOnlyList<string> StepKeys
    {
        get { lock (_gate) return _steps.Select(step => step.Key).ToArray(); }
    }

    /// <summary>The current state of a step or item.</summary>
    internal ProgressState StateOf(string key)
    {
        lock (_gate) return _rows.TryGetValue(key, out var row) ? row.State : ProgressState.Planned;
    }

    /// <summary>Items under a step, as (state) values.</summary>
    internal IReadOnlyList<ProgressState> ItemStates(string step)
    {
        lock (_gate)
            return _rows.Values.Where(row => row.Step is not null && string.Equals(row.Step.Key, step, StringComparison.OrdinalIgnoreCase))
                .Select(row => row.State).ToArray();
    }

    /// <summary>The key of the furthest running step, if any.</summary>
    internal string? RunningStep
    {
        get
        {
            lock (_gate) return _steps.LastOrDefault(step => step.State == ProgressState.Started)?.Key;
        }
    }

    /// <summary>Marks a row as running (again) with an optional detail.</summary>
    internal void Start(string key, string? detail = null, string? kind = null)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(key, out var row) || IsTerminal(row.State)) return;
            var now = _clock();
            row.LastActivity = now;
            row.Detail = Clean(detail);
            if (kind is not null) row.Kind = kind;
            if (row.State != ProgressState.Started)
            {
                row.State = ProgressState.Started;
                row.StartedAt = now;
                PostState(row, StatusKind.Running, Shorten(row.Detail));
                if (row.Step is not null && row.Step.State == ProgressState.Planned) StartParent(row.Step, now);
            }
            else
            {
                PostDetail(row, Shorten(row.Detail));
            }
        }
    }

    /// <summary>Updates the latest detail of a running row (process output, the file being checked).</summary>
    internal void Detail(string key, string? detail, string? kind = null) => Start(key, detail, kind);

    /// <summary>Finishes a row. Without a detail a finished row shows the time it took.</summary>
    internal void Finish(string key, ProgressState state, string? detail = null, TimeSpan? duration = null)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(key, out var row)) return;
            var now = _clock();
            var ran = row.State == ProgressState.Started || duration.HasValue;
            var took = duration ?? (row.State == ProgressState.Started ? now - row.StartedAt : TimeSpan.Zero);
            row.State = state;
            row.Detail = Clean(detail);
            row.LastActivity = now;
            if (row.Step is null) _idleSince = now;
            var shown = Shorten(row.Detail);
            if (ran && state != ProgressState.Skipped)
                shown = string.IsNullOrEmpty(shown) ? NumberFormat.Duration(took) : shown + _separator + NumberFormat.Duration(took);
            PostState(row, ToStatus(state), string.IsNullOrEmpty(shown) ? null : shown);
        }
    }

    /// <summary>
    /// Finishes every row that is still open: running rows fail when the run failed, the rest are skipped
    /// ("not required" after success, "skipped after failure" otherwise).
    /// </summary>
    internal void FinishRemaining(bool success)
    {
        Row[] open;
        lock (_gate) open = _rows.Values.Where(row => !IsTerminal(row.State)).OrderBy(row => row.Index).ThenBy(row => row.ItemIndex).ToArray();
        foreach (var row in open)
        {
            if (row.State == ProgressState.Started && !success) Finish(row.Key, ProgressState.Failed, "stopped");
            else if (row.State == ProgressState.Started) Finish(row.Key, ProgressState.Completed);
            else Finish(row.Key, ProgressState.Skipped, success ? "not required" : "skipped after failure");
        }
    }

    /// <summary>Adds a message to the live log; it is printed in full when the view ends.</summary>
    internal bool TryMessage(string level, string text)
    {
        LiveSession? session;
        lock (_gate)
        {
            if (_finished) return false;
            _messages.Add((level, text ?? string.Empty));
            session = _session;
        }
        session?.Post(new LiveEvent(LogId, "write", text: text ?? string.Empty, status: level));
        return true;
    }

    /// <summary>
    /// Runs <paramref name="work"/> while the view is drawn inline below the cursor. Afterwards the full ledger is
    /// printed, followed by every message. Exceptions from <paramref name="work"/> propagate after that.
    /// </summary>
    internal void Run(Action work)
    {
        if (work is null) throw new ArgumentNullException(nameof(work));
        var options = new LiveSessionOptions
        {
            Mode = LiveMode.Inline,
            Summary = _ledger,
            LogTarget = LogId,
            MaxQueuedEvents = 100_000,
            Clock = _clock
        };

        var previous = Active.Value;
        try
        {
            using var session = new LiveSession(new Layout(this), options);
            lock (_gate)
            {
                _session = session;
                _idleSince = _clock();
            }
            Active.Value = this;
            try
            {
                session.Start();
                work();
            }
            finally
            {
                Active.Value = previous;
                lock (_gate) _finished = true;
                session.Stop();
                lock (_gate) _session = null;
            }
        }
        finally
        {
            WriteMessages();
        }
    }

    private void StartParent(Row step, TimeSpan now)
    {
        step.State = ProgressState.Started;
        step.StartedAt = now;
        step.LastActivity = now;
        PostState(step, StatusKind.Running, null);
    }

    // A step's state event carries its detail; an item's state and detail are separate events.
    private void PostState(Row row, StatusKind status, string? detail)
    {
        if (row.Step is null)
        {
            Send(new LiveEvent(LedgerId, "state", row.Key, text: detail, status: status.ToString()));
            return;
        }

        Send(new LiveEvent(LedgerId, "item", EventKey(row), status: status.ToString()));
        PostDetail(row, detail);
    }

    private void PostDetail(Row row, string? detail)
        => Send(new LiveEvent(LedgerId, row.Step is null ? "detail" : "item-detail", EventKey(row), text: detail));

    private static string EventKey(Row row) => row.Step is null ? row.Key : row.Step.Key + ItemSeparator + row.Key;

    private void Send(LiveEvent liveEvent)
    {
        if (_session is not null)
        {
            _session.Post(liveEvent);
            return;
        }

        // Before the session starts the widget is updated directly; it has no clock yet.
        _ledger.Apply(liveEvent);
    }

    private void WriteMessages()
    {
        (string Level, string Text)[] messages;
        lock (_gate) messages = _messages.ToArray();
        if (messages.Length == 0) return;
        var lines = new List<IWidget> { new Rule($"[muted]Messages ({messages.Length})[/]") };
        lines.AddRange(messages.Select(message => (IWidget)new MessageLine(message.Level, message.Text)));
        var block = new Rows(lines.ToArray());
        ConsoleOutput.Write(block);
    }

    // The label is markup in the ledger: the title, then the target it works on, muted.
    private static string LabelMarkup(Row row)
        => string.IsNullOrWhiteSpace(row.Target)
            ? Markup.Escape(row.Label)
            : Markup.Escape(row.Label) + " [muted]" + Markup.Escape(row.Target!) + "[/]";

    private static bool IsTerminal(ProgressState state)
        => state is ProgressState.Completed or ProgressState.Failed or ProgressState.Skipped;

    private static StatusKind ToStatus(ProgressState state) => state switch
    {
        ProgressState.Started => StatusKind.Running,
        ProgressState.Completed => StatusKind.Pass,
        ProgressState.Failed => StatusKind.Fail,
        ProgressState.Skipped => StatusKind.Skip,
        _ => StatusKind.Pending
    };

    // Details are single lines: process output and file paths arrive one at a time.
    private static string? Clean(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;
        var line = detail!.Replace("\r\n", "\n").Split('\n').Select(part => part.Trim()).FirstOrDefault(part => part.Length > 0);
        return string.IsNullOrEmpty(line) ? null : line;
    }

    // Rows keep a short detail so a long process line does not wrap the ledger; "Current:" shows it whole.
    private static string? Shorten(string? detail)
        => detail is null || detail.Length <= RowDetailLimit ? detail : detail.Substring(0, RowDetailLimit - 3) + "...";

    private sealed class Row(string key, Row? step, string label, string? kind, int index, int itemIndex)
    {
        internal string Key { get; } = key;
        internal Row? Step { get; } = step;
        internal string Label { get; set; } = label;
        internal string? Target { get; set; }
        internal string? Kind { get; set; } = kind;
        internal int Index { get; } = index;
        internal int ItemIndex { get; } = itemIndex;
        internal int ItemCount { get; set; }
        internal ProgressState State { get; set; }
        internal string? Detail { get; set; }
        internal TimeSpan StartedAt { get; set; }
        internal TimeSpan LastActivity { get; set; }
    }
}
