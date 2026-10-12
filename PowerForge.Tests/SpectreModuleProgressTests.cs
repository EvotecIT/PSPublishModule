using PowerForge.ConsoleShared;
using Spectre.Console;

namespace PowerForge.Tests;

public sealed class SpectreModuleProgressTests
{
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
