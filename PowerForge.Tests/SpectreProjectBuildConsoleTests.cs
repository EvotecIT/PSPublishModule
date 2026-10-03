using PowerForge.ConsoleShared;
using Spectre.Console;

namespace PowerForge.Tests;

public sealed class SpectreProjectBuildConsoleTests
{
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
        Assert.Contains("100%", writer.ToString());
        Assert.DoesNotContain("stale metadata", writer.ToString());
    }

    private sealed class TerminalOutput(TextWriter writer) : IAnsiConsoleOutput
    {
        public TextWriter Writer => writer;
        public bool IsTerminal => true;
        public int Width => 100;
        public int Height => 18;
        public void SetEncoding(System.Text.Encoding encoding) { }
    }
}
