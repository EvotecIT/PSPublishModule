using PowerForge.ConsoleShared;
using Spectre.Console;

namespace PowerForge.Tests;

public sealed class SpectreProjectBuildConsoleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminalPhaseRows_KeepStatusAndElapsedTimeReadableWithLongDetails(bool failed)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalOutput(writer, 60), Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors, Interactive = InteractionSupport.Yes
        });
        SpectreProjectBuildConsoleUi.RunInteractive(console,
            new ProjectBuildConsolePlan { RootPath = "source", PlanOnly = true }, progress =>
            {
                progress.PhaseStarted(ProjectBuildProgressPhase.Plan, 2);
                var detail = new string('P', 100);
                if (failed) progress.PhaseFailed(ProjectBuildProgressPhase.Plan, detail);
                else progress.PhaseCompleted(ProjectBuildProgressPhase.Plan, detail);
                return new ProjectBuildWorkflowResult { Result = new ProjectBuildResult { Success = !failed } };
            });
        var plain = System.Text.RegularExpressions.Regex.Replace(writer.ToString(), @"\x1b\[[0-?]*[ -/]*[@-~]", string.Empty);
        var finalRow = plain.TrimEnd().Split('\n').Last();
        Assert.Contains(failed ? "failed" : "done", finalRow, StringComparison.Ordinal);
        Assert.Matches(@"\b\d{2}:\d{2}\b", finalRow);
        Assert.True(finalRow.TrimEnd('\r').GetCellWidth() <= 60, finalRow);
    }

    [Fact]
    public void CompletedPlan_IsNotReopenedByMetadataFromPackageExecution()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalOutput(writer), Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors, Interactive = InteractionSupport.Yes
        });
        var result = SpectreProjectBuildConsoleUi.RunInteractive(console,
            new ProjectBuildConsolePlan { RootPath = "source", PlanOnly = true }, progress =>
            {
                progress.PhaseStarted(ProjectBuildProgressPhase.Plan, 2);
                progress.PhaseCompleted(ProjectBuildProgressPhase.Plan, "plan prepared");
                progress.PhaseStarted(ProjectBuildProgressPhase.Plan, 2, "stale metadata start");
                progress.PhaseUpdated(ProjectBuildProgressPhase.Plan, 1, 2, "stale metadata update");
                return new ProjectBuildWorkflowResult { Result = new ProjectBuildResult { Success = true } };
            });
        Assert.True(result.Result.Success);
        Assert.Contains("plan prepared", writer.ToString());
        Assert.Contains("done", writer.ToString());
        Assert.DoesNotContain("stale metadata", writer.ToString());
    }

    private sealed class TerminalOutput(TextWriter writer, int width = 100) : IAnsiConsoleOutput
    {
        public TextWriter Writer => writer;
        public bool IsTerminal => true;
        public int Width => width;
        public int Height => 18;
        public void SetEncoding(System.Text.Encoding encoding) { }
    }
}
