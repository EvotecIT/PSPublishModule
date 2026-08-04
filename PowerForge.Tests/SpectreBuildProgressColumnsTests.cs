using System.Collections.Concurrent;
using PowerForge.ConsoleShared;
using Spectre.Console;
using Spectre.Console.Rendering;

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

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3900, "01:05:00")]
    [InlineData(90061, "25:01:01")]
    public void FormatElapsed_PreservesHoursWithoutWrapping(long seconds, string expected)
        => Assert.Equal(
            expected,
            SpectreBuildProgressColumns.FormatElapsed(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Presentation_DoesNotReserveTargetWidthForTargetlessRows()
    {
        var presentation = new SpectreProgressPresentation(viewportWidth: 120, unicode: false);
        var targetless = new SpectreProgressLedgerItem
        {
            Title = "Publish PowerForgeWeb net10.0 win-x64 SingleContained",
            Position = 1,
            Total = 2
        };
        var targeted = new SpectreProgressLedgerItem
        {
            Title = "Publish",
            Target = "PowerShellGallery",
            Position = 2,
            Total = 2
        };

        var targetlessLabel = presentation.BuildLabel(targetless, "packing");
        var targetedLabel = presentation.BuildLabel(targeted, null);

        Assert.Contains("SingleContained", targetlessLabel, StringComparison.Ordinal);
        Assert.Contains("packing", targetlessLabel, StringComparison.Ordinal);
        Assert.EndsWith("PowerShellGallery", targetedLabel, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Module", 17, 18, "Module 17/18")]
    [InlineData("Tool", 1, 10, "Tool 01/10")]
    [InlineData("Project", 1, 6, "Project 01/06")]
    [InlineData("Asset", 20, 24, "Asset 20/24")]
    public void Presentation_MakesEveryCounterScopeExplicit(
        string scope,
        int position,
        int total,
        string expected)
    {
        var presentation = new SpectreProgressPresentation(viewportWidth: 140, unicode: false);
        var item = new SpectreProgressLedgerItem
        {
            Title = "Work item",
            CounterLabel = scope,
            Position = position,
            Total = total
        };

        Assert.StartsWith(expected, presentation.BuildLabel(item, null), StringComparison.Ordinal);
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

    [Fact]
    public void Viewport_ProjectsAContiguousWindowAroundActiveTaskWithoutReordering()
    {
        var tasks = Enumerable.Range(1, 12)
            .Select(index => new ProgressTask(
                index,
                $"Task.{index:00}",
                maxValue: 1,
                autoStart: false,
                TimeProvider.System))
            .ToArray();
        tasks[8].StartTask();

        var rows = new Rows(tasks.Select(task => (IRenderable)new Text(task.Description)));
        var projected = SpectreProgressViewport.Project(rows, tasks, maximumRows: 5);

        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes
        });
        console.Write(projected);

        var output = writer.ToString();
        Assert.DoesNotContain("Task.04", output, StringComparison.Ordinal);
        AssertOrdered(output, "Task.05", "Task.06", "Task.07", "Task.08", "Task.09");
        Assert.DoesNotContain("Task.10", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewportState_DoesNotJumpBackBetweenAdjacentUploadRows()
    {
        var tasks = Enumerable.Range(1, 30)
            .Select(index => new ProgressTask(
                index,
                index <= 18 ? $"Module {index:00}/18" : $"Asset {index - 18:00}/12",
                maxValue: 1,
                autoStart: false,
                TimeProvider.System))
            .ToArray();
        var viewport = new SpectreProgressViewportState();

        tasks[16].StartTask();
        tasks[24].StartTask();
        var uploadFrame = RenderViewport(viewport, tasks, maximumRows: 6);

        tasks[24].Value = 1;
        tasks[24].StopTask();
        var betweenUploadsFrame = RenderViewport(viewport, tasks, maximumRows: 6);

        tasks[25].StartTask();
        var nextUploadFrame = RenderViewport(viewport, tasks, maximumRows: 6);

        Assert.Contains("Asset 07/12", uploadFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("Module 17/18", betweenUploadsFrame, StringComparison.Ordinal);
        Assert.Contains("Asset 07/12", betweenUploadsFrame, StringComparison.Ordinal);
        Assert.Contains("Asset 08/12", nextUploadFrame, StringComparison.Ordinal);
    }

    private static string RenderViewport(
        SpectreProgressViewportState viewport,
        IReadOnlyList<ProgressTask> tasks,
        int maximumRows)
    {
        var rows = new Rows(tasks.Select(task => (IRenderable)new Text(task.Description)));
        var projected = viewport.Project(rows, tasks, maximumRows);
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes
        });
        console.Write(projected);
        return writer.ToString();
    }

    private static void AssertOrdered(string output, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var current = output.IndexOf(value, StringComparison.Ordinal);
            Assert.True(current > previous, output);
            previous = current;
        }
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
