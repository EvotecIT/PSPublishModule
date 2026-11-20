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
/// gets the same information as plain lines. The drill runs at reduced CPU and disk priority, and closing the window records
/// the attempt as cancelled.
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
        var steps = new StepTracker(view is not null ? view : text!);
        IRestoreDrillProgress progress = steps;
        var header = Header(set, record, priority);
        if (view is null) Console.Out.WriteLine(string.Join(Environment.NewLine, header.Select(line => line.Label + ": " + line.Value)));

        string outcome;
        string reason = string.Empty;
        string next = string.Empty;
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
            (outcome, reason, next) = Explain(error, cancellation.IsCancellationRequested, steps.Stopped, options.RestoreDrillTimeoutMinutes);
        }
        finally { finished.Set(); }

        var checkedCount = view?.CheckedDatabases ?? 0;
        if (view is not null) Terminal.Write(ResultPanel(outcome, reason, next, record, checkedCount));
        else Console.Out.WriteLine(ResultLine(outcome, reason, next, record));
        return outcome switch { "passed" => 0, "cancelled" => 130, _ => 1 };
    }

    /// <summary>
    /// Says what stopped the drill and what to do about it. A stop by the user and a time limit say nothing about the backup;
    /// a failed check does. Only this library's own check messages are shown: provider messages can contain credentials or
    /// customer data, so other failures are named by type.
    /// </summary>
    internal static (string Outcome, string Reason, string Next) Explain(Exception error, bool cancelledByUser, string? step, int? limitMinutes)
    {
        var where = step is null ? "" : " while " + Gerund(step);
        if (cancelledByUser)
            return ("cancelled", "Stopped by the user" + where + " (Ctrl+C or the window was closed).",
                "Throwaway copies are removed and live data was not touched. Run it again whenever convenient.");
        if (error is TimeoutException)
            return ("timed out", "Stopped at the " + Limit(limitMinutes) + " time limit" + where + ". Nothing wrong was found in what it had checked.",
                "This is not a problem with the backup. Raise or remove restoreDrillTimeoutMinutes and run it again.");
        if (error is InvalidDataException)
            return ("failed", "A check failed" + where + ": " + error.Message,
                "Do not rely on this backup set until the cause is known. Check that set's files, or run the drill on an earlier set.");
        return ("failed", "The drill could not finish" + where + ": " + error.GetType().Name + ".",
            "This does not show that the backup is damaged. Fix the cause (space, access, SQL Server) and run it again.");
    }

    private static string Limit(int? minutes) => minutes switch
    {
        null => "configured",
        { } m when m % 60 == 0 => (m / 60).ToString(CultureInfo.InvariantCulture) + "-hour",
        { } m => m.ToString(CultureInfo.InvariantCulture) + "-minute"
    };

    // Step labels start with a verb ("Check ddnext (SQLite integrity)"); "while checking ddnext (SQLite integrity)" reads better.
    internal static string Gerund(string label)
    {
        var space = label.IndexOf(' ');
        if (space < 0) return label;
        var rest = label[(space + 1)..];
        return label[..space] switch
        {
            "Check" => "checking " + rest,
            "Checksum" => "verifying the checksum of " + rest,
            "Copy" => "copying " + rest,
            "Restore" => "restoring " + rest,
            _ => char.ToLowerInvariant(label[0]) + label[1..]
        };
    }

    internal static IReadOnlyList<(string Label, string Value)> Header(string set, string record, string priority) =>
    [
        ("Backup set", set),
        ("What it does", "Checks every backup file against its recorded SHA-256, runs SQLite integrity checks, and restores SQL Server backups into a throwaway database that it removes afterwards."),
        ("Live data", "Not touched. " + priority + " It is safe to leave running."),
        ("Record", record),
        ("To stop", "Close this window or press Ctrl+C. The attempt is recorded as cancelled, not failed.")
    ];

    internal static string ResultLine(string outcome, string reason, string next, string record) => outcome switch
    {
        "passed" => "Restore drill passed. Every database restored and checked. Record: " + record + ". Nothing else to do; the health monitor reads this record.",
        "cancelled" => "Restore drill cancelled. " + reason + " " + next + " Record: " + record + ".",
        "timed out" => "Restore drill stopped. " + reason + " " + next + " Record: " + record + ".",
        _ => "Restore drill failed. " + reason + " " + next + " Record: " + record + "."
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

    private static IWidget ResultPanel(string outcome, string reason, string next, string record, int databases)
    {
        var facts = new KeyValueList();
        switch (outcome)
        {
            case "passed":
                facts.Add("Result", "[pass][bold]Passed[/][/]: " + databases + " database" + (databases == 1 ? "" : "s") + " restored and checked");
                facts.Add("Next", "Nothing to do. The health monitor reads this record.");
                break;
            case "cancelled":
                facts.Add("Result", "[warn][bold]Cancelled[/][/]: " + Markup.Escape(reason));
                facts.Add("Next", Markup.Escape(next));
                break;
            case "timed out":
                facts.Add("Result", "[warn][bold]Stopped[/][/]: " + Markup.Escape(reason));
                facts.Add("Next", Markup.Escape(next));
                break;
            default:
                facts.Add("Result", "[fail][bold]Failed[/][/]: " + Markup.Escape(reason));
                facts.Add("Next", Markup.Escape(next));
                break;
        }
        facts.Add("Record", "[muted]" + Markup.Escape(record) + "[/]");
        return new Panel(facts, "[bold]Restore drill result[/]")
        {
            Verdict = true,
            Status = outcome switch { "passed" => StatusKind.Pass, "cancelled" or "timed out" => StatusKind.Warn, _ => StatusKind.Fail }
        };
    }

    private static bool CanDrawLive()
    {
        if (Console.IsOutputRedirected) return false;
        var profile = Terminal.GetCurrentProfile();
        return profile.Mode == OutputMode.Interactive && Terminal.IsConsole(Terminal.Target);
    }

    // Below-normal CPU, low disk and below-normal memory priority. Background mode (very low disk and memory priority)
    // let a busy SQL Server starve the drill: a 56 GB SQLite check ran past four hours. SQL Server's own restore I/O runs
    // in its service process and keeps that process's priority.
    private static string LowerPriority()
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (Exception) { return "Runs at normal priority."; }
        if (!OperatingSystem.IsWindows()) return "Runs at below-normal CPU priority.";
        var process = GetCurrentProcess();
        var memory = new MemoryPriorityInformation { MemoryPriority = MemoryPriorityBelowNormal };
        SetProcessInformation(process, ProcessMemoryPriority, ref memory, (uint)Marshal.SizeOf<MemoryPriorityInformation>());
        var io = IoPriorityLow;
        return NtSetInformationProcess(process, ProcessIoPriority, ref io, sizeof(int)) == 0
            ? "Runs at below-normal CPU and low disk priority, so other work goes first."
            : "Runs at below-normal CPU priority.";
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

    private const int ProcessMemoryPriority = 0, ProcessIoPriority = 33;
    private const uint MemoryPriorityBelowNormal = 4;
    private const int IoPriorityLow = 1;
    private const uint CtrlC = 0, CtrlBreak = 1, CtrlClose = 2;

    private delegate bool ConsoleCtrlHandler(uint signal);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryPriorityInformation { public uint MemoryPriority; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref MemoryPriorityInformation information, uint size);

    // The process I/O priority has no documented Win32 setter; Process Explorer and Task Manager use this call. Low (1)
    // sits between background's very low (0) and normal (2). A failure leaves the normal I/O priority.
    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(IntPtr process, int informationClass, ref int information, int length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    /// <summary>Passes progress on and remembers the label of the step that ended unfinished when the drill stopped.</summary>
    private sealed class StepTracker(IRestoreDrillProgress inner) : IRestoreDrillProgress
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);

        internal string? Stopped { get; private set; }

        public void Plan(string key, string label, long total, RestoreDrillUnit unit)
        {
            lock (_gate) _labels[key] = label;
            inner.Plan(key, label, total, unit);
        }

        public void Start(string key, string? detail = null) => inner.Start(key, detail);

        public void Report(string key, long done, string? detail = null) => inner.Report(key, done, detail);

        public void Finish(string key, bool succeeded, string? detail = null)
        {
            if (!succeeded) lock (_gate) if (_labels.TryGetValue(key, out var label)) Stopped = label;
            inner.Finish(key, succeeded, detail);
        }
    }

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
