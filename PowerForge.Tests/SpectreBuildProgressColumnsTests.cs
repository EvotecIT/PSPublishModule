using System.Collections.Concurrent;
using PowerForge.ConsoleShared;
using Spectre.Console;

namespace PowerForge.Tests;

public sealed class SpectreBuildProgressColumnsTests
{
    [Fact]
    public void CreateStandard_UsesLeftAnchoredModuleStyleLayout()
    {
        var columns = SpectreBuildProgressColumns.CreateStandard();

        Assert.Collection(
            columns,
            column => Assert.Equal("LeftMarkupDescriptionColumn", column.GetType().Name),
            column => Assert.IsType<ProgressBarColumn>(column),
            column => Assert.IsType<PercentageColumn>(column),
            column => Assert.IsType<ElapsedTimeColumn>(column),
            column => Assert.IsType<SpinnerColumn>(column));
    }

    [Fact]
    public void CreateDetailed_UsesTheSharedModuleAndExecutableLayout()
    {
        var columns = SpectreBuildProgressColumns.CreateDetailed(
            includeBar: true,
            includeElapsed: true,
            barWidth: 18,
            new ConcurrentDictionary<ProgressTask, string>(),
            new ConcurrentDictionary<ProgressTask, DateTimeOffset>(),
            new ConcurrentDictionary<ProgressTask, TimeSpan>());

        Assert.Collection(
            columns,
            column => Assert.Equal("StepIconColumn", column.GetType().Name),
            column => Assert.Equal("LeftDescriptionColumn", column.GetType().Name),
            column =>
            {
                var bar = Assert.IsType<ProgressBarColumn>(column);
                Assert.Equal(18, bar.Width);
            },
            column => Assert.IsType<PercentageColumn>(column),
            column => Assert.Equal("FixedElapsedColumn", column.GetType().Name),
            column => Assert.IsType<SpinnerColumn>(column));
    }

    [Fact]
    public void Run_WritesCompletedProgressFrameAfterLiveDisplayCloses()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes
        });

        SpectreProgressDisplay.Run(
            console,
            [
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new PercentageColumn()
            ],
            context =>
            {
                var task = context.AddTask("Retained build step", maxValue: 1, autoStart: false);
                task.StartTask();
                context.Refresh();
                task.Value = 1;
                task.StopTask();
                context.Refresh();
            });

        var output = writer.ToString().TrimEnd();
        Assert.EndsWith("Retained build step 100%", output, StringComparison.Ordinal);
    }

    private sealed class TerminalConsoleOutput : IAnsiConsoleOutput
    {
        public TerminalConsoleOutput(TextWriter writer)
            => Writer = writer;

        public TextWriter Writer { get; }

        public bool IsTerminal => true;

        public int Width => 120;

        public int Height => 40;

        public void SetEncoding(System.Text.Encoding encoding)
        {
        }
    }
}
