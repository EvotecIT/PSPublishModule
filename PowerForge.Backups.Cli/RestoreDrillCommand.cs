using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ConsoleMonster;
using ConsoleMonster.Capabilities;
using ConsoleMonster.Layout;
using ConsoleMonster.Live;
using ConsoleMonster.Styling;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge.Backups;

namespace PowerForge.Backups.Cli;

/// <summary>
/// Runs a restore drill so that whoever sees it can tell what it is and how far it has got: a header saying what it does
/// and that live data is not touched, one progress row per step, and a closing panel with the outcome. Redirected output
/// gets the same information as plain lines. The drill runs at background priority, and closing the window records the
/// attempt as cancelled.
/// </summary>
internal static class RestoreDrillCommand
{
    private static ConsoleCtrlHandler? _closeHandler;

    internal static async Task<int> RunAsync(BackupOptions options, string folder, string captureSha256, CancellationTokenSource cancellation)
    {
        var record = Path.Combine(Path.GetDirectoryName(options.StatePath)!, "restore-drill.json");
        var set = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
        var priority = LowerPriority();
        using var finished = new ManualResetEventSlim();
        WatchForClose(cancellation, finished);
        try { Console.Title = "PowerForge restore drill: " + set; } catch (IOException) { } catch (PlatformNotSupportedException) { }

        var live = CanDrawLive();
        var view = live ? new LiveView() : null;
        var text = live ? null : new RestoreDrillTextProgress(Console.Out);
        IRestoreDrillProgress progress = view is not null ? view : text!;
        var header = Header(set, record, priority);
        if (view is null) Console.Out.WriteLine(string.Join(Environment.NewLine, header.Select(line => line.Label + ": " + line.Value)));

        string outcome;
        string reason = string.Empty;
        using var heartbeat = text is null ? null : new Timer(_ => { try { text.Heartbeat(); } catch (Exception) { /* output never stops the drill */ } }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        try
        {
            Task Drill() => new RestoreDrill().VerifyAsync(options, folder, captureSha256, progress, null, cancellation.Token);
            if (view is not null) await view.RunAsync(HeaderPanel(header), Drill);
            else await Drill();
            outcome = "passed";
        }
        catch (Exception error)
        {
            outcome = cancellation.IsCancellationRequested ? "cancelled" : "failed";
            // Provider exception messages can contain credentials or customer data; the type names the failure.
            reason = error.GetType().Name;
        }
        finally { finished.Set(); }

        var checkedCount = view?.CheckedDatabases ?? 0;
        if (view is not null) Terminal.Write(ResultPanel(outcome, reason, record, checkedCount));
        else Console.Out.WriteLine(ResultLine(outcome, reason, record));
        return outcome switch { "passed" => 0, "cancelled" => 130, _ => 1 };
    }

    internal static IReadOnlyList<(string Label, string Value)> Header(string set, string record, string priority) =>
    [
        ("Backup set", set),
        ("What it does", "Checks every backup file against its recorded SHA-256, runs SQLite integrity checks, and restores SQL Server backups into a throwaway database that it removes afterwards."),
        ("Live data", "Not touched. " + priority + " It is safe to leave running."),
        ("Record", record),
        ("To stop", "Close this window or press Ctrl+C. The attempt is recorded as cancelled, not failed.")
    ];

    internal static string ResultLine(string outcome, string reason, string record) => outcome switch
    {
        "passed" => "Restore drill passed. Every database restored and checked. Record: " + record + ". Nothing else to do; the health monitor reads this record.",
        "cancelled" => "Restore drill cancelled by the user. Throwaway copies are removed and live data was not touched. Record: " + record + ". Run it again whenever convenient.",
        _ => "Restore drill failed: " + reason + ". Record: " + record + ". The step marked failed above shows where it stopped; fix that and run the drill again."
    };

    private static IWidget HeaderPanel(IReadOnlyList<(string Label, string Value)> header)
    {
        var facts = new KeyValueList();
        foreach (var (label, value) in header)
            facts.Add(label, label switch
            {
                "Backup set" => "[bold]" + Markup.Escape(value) + "[/]",
                "Live data" => "[pass]" + Markup.Escape(value) + "[/]",
                "Record" or "To stop" => "[muted]" + Markup.Escape(value) + "[/]",
                _ => Markup.Escape(value)
            });
        return new Panel(new Rows(TextBlock.FromMarkup("[accent][bold]Checking that a Synology backup can be restored[/][/]"), facts),
            "[accent][bold]PowerForge restore drill[/][/]") { BorderRole = ThemeToken.Accent };
    }

    private static IWidget ResultPanel(string outcome, string reason, string record, int databases)
    {
        var facts = new KeyValueList();
        switch (outcome)
        {
            case "passed":
                facts.Add("Result", "[pass][bold]Passed[/][/]: " + databases + " database" + (databases == 1 ? "" : "s") + " restored and checked");
                facts.Add("Next", "Nothing to do. The health monitor reads this record.");
                break;
            case "cancelled":
                facts.Add("Result", "[warn][bold]Cancelled[/][/] by the user");
                facts.Add("Next", "Throwaway copies are removed and live data was not touched. Run it again whenever convenient.");
                break;
            default:
                facts.Add("Result", "[fail][bold]Failed[/][/]: " + Markup.Escape(reason));
                facts.Add("Next", "The step marked failed above shows where it stopped. Fix that and run the drill again.");
                break;
        }
        facts.Add("Record", "[muted]" + Markup.Escape(record) + "[/]");
        return new Panel(facts, "[bold]Restore drill result[/]")
        {
            Verdict = true,
            Status = outcome switch { "passed" => StatusKind.Pass, "cancelled" => StatusKind.Warn, _ => StatusKind.Fail }
        };
    }

    private static bool CanDrawLive()
    {
        if (Console.IsOutputRedirected) return false;
        var profile = Terminal.GetCurrentProfile();
        return profile.Mode == OutputMode.Interactive && Terminal.IsConsole(Terminal.Target);
    }

    // Background mode lowers CPU, disk and memory priority together on Windows. SQL Server's own restore I/O runs in
    // its service process and keeps that process's priority.
    private static string LowerPriority()
    {
        if (OperatingSystem.IsWindows() && SetPriorityClass(GetCurrentProcess(), ProcessModeBackgroundBegin))
            return "Runs at background CPU and disk priority, so other work goes first.";
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
        return "Runs at below-normal CPU priority.";
    }

    // Closing the console gives a process about five seconds; cancel, then wait for the drill to record the outcome.
    private static void WatchForClose(CancellationTokenSource cancellation, ManualResetEventSlim finished)
    {
        if (!OperatingSystem.IsWindows()) return;
        _closeHandler = signal =>
        {
            if (signal is not (CtrlC or CtrlBreak or CtrlClose)) return false;
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
            if (signal == CtrlClose) finished.Wait(TimeSpan.FromSeconds(4.5));
            return true;
        };
        SetConsoleCtrlHandler(_closeHandler, true);
    }

    private const uint ProcessModeBackgroundBegin = 0x00100000;
    private const uint CtrlC = 0, CtrlBreak = 1, CtrlClose = 2;

    private delegate bool ConsoleCtrlHandler(uint signal);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    /// <summary>The live console view: the header above one ConsoleMonster lane per step.</summary>
    private sealed class LiveView : IRestoreDrillProgress
    {
        private const string LanesId = "drill";
        private readonly object _gate = new();
        private readonly Lanes _lanes = new(LanesId) { MaxLabelWidth = 44 };
        private readonly Dictionary<string, (RestoreDrillUnit Unit, long Done)> _steps = new(StringComparer.Ordinal);
        private LiveSession? _session;

        internal int CheckedDatabases { get; private set; }

        internal LiveView()
        {
            _lanes.Columns = new List<ProgressColumn>
            {
                ProgressColumns.Spinner(), ProgressColumns.Description(44), ProgressColumns.Bar(30),
                ProgressColumns.Custom("Percent", row => PercentText(row), 4, 70), ProgressColumns.Custom("Amount", row => AmountText(row), 19, 60),
                ProgressColumns.Elapsed(), ProgressColumns.Remaining(), ProgressColumns.Status()
            };
        }

        internal async Task RunAsync(IWidget header, Func<Task> drill)
        {
            using var session = new LiveSession(new Rows().Add(header).Add(_lanes), new LiveSessionOptions { Mode = LiveMode.Inline });
            lock (_gate) _session = session;
            session.Start();
            try { await drill(); }
            finally
            {
                session.Stop();
                lock (_gate) _session = null;
            }
        }

        public void Plan(string key, string label, long total, RestoreDrillUnit unit)
        {
            lock (_gate) _steps[key] = (unit, 0);
            Send(new LiveEvent(LanesId, "add", key, text: label, total: total));
        }

        public void Start(string key, string? detail = null)
            => Send(new LiveEvent(LanesId, "state", key, text: detail, status: nameof(StatusKind.Running)));

        public void Report(string key, long done, string? detail = null)
        {
            long delta;
            lock (_gate)
            {
                if (!_steps.TryGetValue(key, out var step)) return;
                delta = done - step.Done;
                if (delta > 0) _steps[key] = (step.Unit, done);
            }
            if (delta > 0) Send(new LiveEvent(LanesId, "advance", key, value: delta));
            if (detail is not null) Send(new LiveEvent(LanesId, "detail", key, text: detail));
        }

        public void Finish(string key, bool succeeded, string? detail = null)
        {
            if (succeeded && key.StartsWith("check:", StringComparison.Ordinal)) lock (_gate) CheckedDatabases++;
            Send(new LiveEvent(LanesId, "complete", key, text: detail, status: succeeded ? nameof(StatusKind.Pass) : nameof(StatusKind.Fail)));
        }

        private string AmountText(ProgressRow row)
        {
            lock (_gate)
                if (!_steps.TryGetValue(row.Key, out var step) || step.Unit != RestoreDrillUnit.Bytes || row.Total <= 0) return string.Empty;
            var (scale, unit) = row.Total >= 1e9 ? (1e9, "GB") : (1e6, "MB");
            return (row.Done / scale).ToString("0.0", CultureInfo.InvariantCulture) + " / " + (row.Total / scale).ToString("0.0", CultureInfo.InvariantCulture) + " " + unit;
        }

        private static string PercentText(ProgressRow row)
            => row.Status == StatusKind.Pending || row.Total <= 0 ? string.Empty
                : ((int)Math.Floor(100 * row.Fraction)).ToString(CultureInfo.InvariantCulture) + "%";

        private void Send(LiveEvent liveEvent)
        {
            LiveSession? session;
            lock (_gate) session = _session;
            if (session is not null) session.Post(liveEvent);
            else lock (_gate) _lanes.Apply(liveEvent);
        }
    }
}
