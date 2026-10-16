using PowerForge.ConsoleShared;
using Spectre.Console;
using System.Text.RegularExpressions;

namespace PowerForge.Tests;

public sealed class SpectreModuleProgressTests
{
    [Fact]
    public void TimerRefresh_DoesNotWaitForAnExternalWarningPrompt()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, 80);
        using var promptReady = new ManualResetEventSlim();
        using var releasePrompt = new ManualResetEventSlim();
        using var timerReturned = new ManualResetEventSlim();
        SpectreProgressDisplay.Run(console, [new TaskDescriptionColumn()], context =>
        {
            context.AddTask("Build module", maxValue: 1);
            // Invoke the real timer entry point while an external host write owns
            // the display gate. A separate thread makes the lock contention
            // deterministic without measuring or exhausting the thread pool.
            var current = typeof(SpectreProgressDisplay).GetField("Current",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            var session = current.GetType().GetProperty("Value")!.GetValue(current)!;
            var refresh = session.GetType().GetMethod("RefreshFromTimer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var prompt = Task.Run(() => SpectreProgressDisplay.WriteExternal(() =>
            {
                promptReady.Set();
                releasePrompt.Wait();
            }));
            var tick = new Thread(() =>
            {
                try { refresh.Invoke(session, null); }
                finally { timerReturned.Set(); }
            }) { IsBackground = true };
            try
            {
                Assert.True(promptReady.Wait(TimeSpan.FromSeconds(10)));
                tick.Start();
                Assert.True(timerReturned.Wait(TimeSpan.FromSeconds(2)),
                    "A timer callback waited for the external warning prompt.");
            }
            finally
            {
                releasePrompt.Set();
                Assert.True(prompt.Wait(TimeSpan.FromSeconds(10)));
                if ((tick.ThreadState & ThreadState.Unstarted) == 0)
                    Assert.True(tick.Join(TimeSpan.FromSeconds(10)));
            }
        });
    }

    [Fact]
    public async Task CapturedLoggerContext_RetainsMessagesBeforeAndAfterDisplayCloses()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, 80);
        var logger = new SpectreConsoleLogger(console);
        using var ready = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        Task? producer = null;
        try
        {
            SpectreProgressDisplay.Run(console, [new TaskDescriptionColumn()], context =>
            {
                context.AddTask("Build module", maxValue: 1);
                logger.Warn("Diagnostic accepted before closure.");
                producer = Task.Run(() =>
                {
                    ready.Set();
                    resume.Wait();
                    logger.Error("Diagnostic delivered after closure.");
                });
                Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
            });
        }
        finally
        {
            resume.Set();
            if (producer is not null) await producer;
        }
        var plain = Regex.Replace(writer.ToString(), @"\x1b\[[0-?]*[ -/]*[@-~]", string.Empty);
        Assert.Contains("Diagnostic accepted before closure.", plain);
        Assert.Contains("Diagnostic delivered after closure.", plain);
    }

    [Theory]
    [InlineData(60, false)]
    [InlineData(120, false)]
    [InlineData(60, true)]
    public void Diagnostics_FollowProgressWithBlankLinesAndRetainCompleteMessages(int width, bool fail)
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, width);
        var logger = new SpectreConsoleLogger(console);
        var error = Record.Exception(() => SpectreProgressDisplay.Run(console, [new TaskDescriptionColumn()], context =>
        {
            var task = context.AddTask("Build module", maxValue: 1);
            logger.Warn("Warning: certificate provider may require a PIN.");
            logger.Error("Error: " + new string('x', 500) + " complete diagnostic suffix.");
            SpectreProgressDisplay.Refresh(context);
            if (fail) throw new InvalidOperationException("build failed");
            task.Value = 1;
            task.StopTask();
        }));
        if (fail) Assert.Equal("build failed", Assert.IsType<InvalidOperationException>(error).Message);
        else Assert.Null(error);
        writer.WriteLine("Next section");
        var plain = Regex.Replace(writer.ToString(), @"\x1b\[[0-?]*[ -/]*[@-~]", string.Empty);
        var finalProgress = plain.LastIndexOf("Build module", StringComparison.Ordinal);
        var finalLog = plain[finalProgress..];
        Assert.Contains("Messages (2; complete log follows progress)", plain);
        Assert.Matches(@"Build module[^\r\n]*\r?\n[ \t]*\r?\n", finalLog);
        Assert.Contains("Warning: certificate provider may require a PIN.", finalLog);
        Assert.Contains("complete diagnostic suffix.", finalLog);
        Assert.Matches(@"complete diagnostic suffix\.[^\r\n]*\r?\n[ \t]*\r?\nNext section", finalLog);
    }

    [Fact]
    public void Completion_LeavesBlankLineBeforeNextSectionWithoutDiagnostics()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, 80);
        SpectreProgressDisplay.Run(console, [new TaskDescriptionColumn()], context =>
        {
            var task = context.AddTask("Completed build", maxValue: 1);
            task.Value = 1;
            task.StopTask();
        });
        writer.WriteLine("Next section");
        var plain = Regex.Replace(writer.ToString(), @"\x1b\[[0-?]*[ -/]*[@-~]", string.Empty);
        Assert.Matches(@"Completed build[^\r\n]*\r?\n[ \t]*\r?\nNext section", plain);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(80)]
    [InlineData(120)]
    public void Signing_ShowsFullActiveDetailAndLiveElapsedBeforeCompleting(int width)
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, width);
        var presentation = SpectreProgressPresentation.Create(console);
        var sign = new SpectreProgressLedgerItem
        {
            Key = "sign", Title = "Sign module", Kind = "Signing", Position = 1, Total = 2
        };
        var queued = new SpectreProgressLedgerItem { Key = "pack", Title = "Pack artefact", Position = 2, Total = 2 };
        SpectreProgressDisplay.Run(console, presentation.CreateColumns(), context =>
        {
            var ledger = new SpectreProgressLedger(context, presentation);
            ledger.Plan([sign, queued]);
            ledger.Update(sign, SpectreProgressLedgerState.Started,
                "Signing and timestamping 1/2: Modules/LongDirectoryName/FullFileName.psm1");
            Thread.Sleep(1150); // Exercise a blocked signer with no callbacks while the timer refreshes.
            ledger.Update(sign, SpectreProgressLedgerState.Completed, "2 signed files");
            ledger.Update(queued, SpectreProgressLedgerState.Skipped, "not required");
        }, activeStatus: presentation.RenderActiveStatus);

        var output = writer.ToString();
        Assert.Contains("queued", output);
        Assert.Contains("running", output);
        Assert.Contains("00:01", output);
        Assert.Contains("FullFileName.psm1", output);
        Assert.Contains("PIN", output);
        Assert.Contains("done", output);
        Assert.Contains("skipped", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalWarning_PreservesCompleteDiagnosticAndSynchronousStop(bool stop)
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, 80);
        SpectreProgressDisplay.Run(console, [new TaskDescriptionColumn()], context =>
        {
            var task = context.AddTask("Build module", maxValue: 1);
            SpectreProgressDisplay.Refresh(context);
            var error = Record.Exception(() => SpectreProgressDisplay.WriteExternal(() =>
            {
                writer.WriteLine("WARNING: Complete diagnostic including final recovery instructions.");
                if (stop) throw new InvalidOperationException("warning stop");
            }));
            if (stop) Assert.Equal("warning stop", Assert.IsType<InvalidOperationException>(error).Message);
            else Assert.Null(error);
            task.Value = 1;
            task.StopTask();
            SpectreProgressDisplay.Refresh(context);
        });
        var output = writer.ToString();
        var warningAt = output.IndexOf("WARNING:", StringComparison.Ordinal);
        Assert.True(warningAt > 0);
        Assert.Contains("final recovery instructions.", output);
        Assert.Contains("Build module", output[warningAt..]);
    }

    private static IAnsiConsole CreateConsole(TextWriter writer, int width)
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalOutput(writer, width), Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor, Interactive = InteractionSupport.Yes
        });

    private sealed class TerminalOutput(TextWriter writer, int width) : IAnsiConsoleOutput
    {
        public TextWriter Writer => writer;
        public bool IsTerminal => true;
        public int Width => width;
        public int Height => 18;
        public void SetEncoding(System.Text.Encoding encoding) { }
    }
}
