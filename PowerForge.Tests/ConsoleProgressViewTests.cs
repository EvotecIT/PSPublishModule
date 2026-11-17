using System;
using System.Threading;
using ConsoleMonster;
using ConsoleMonster.Capabilities;
using ConsoleMonster.Output;
using ConsoleMonster.Testing;
using PowerForge.ConsoleUi;

namespace PowerForge.Tests;

// The shared live build view (ProgressView) as each console route drives it. Live frames are drawn into an
// interactive recording target; the final ledger and messages are checked as the plain text a terminal keeps.
[Collection(ConsoleCapture.Collection)]
public sealed class ConsoleProgressViewTests
{
    private const string Detail = "Authorize the certificate provider";
    private const string Diagnostic = "Complete recovery instructions remain visible after the ledger.";

    [Theory]
    [InlineData("project")]
    [InlineData("executable-signing")]
    [InlineData("release-signing")]
    [InlineData("status")]
    public void Routes_ShowCurrentWorkWhileRunningAndPrintMessagesAfterTheLedger(string route)
    {
        var text = ConsoleCapture.Live(view =>
        {
            var logger = new ConsoleOutputLogger();
            void Running()
            {
                logger.Warn(Diagnostic);
                // Leave the render thread time to draw the running state.
                Thread.Sleep(400);
            }

            switch (route)
            {
                case "project":
                    ProjectBuildConsoleUi.RunInteractive(view, new ProjectBuildConsolePlan { SignPackages = true }, progress =>
                    {
                        progress.PhaseStarted(ProjectBuildProgressPhase.PackageSigning, 0, Detail);
                        Running();
                        progress.PhaseCompleted(ProjectBuildProgressPhase.PackageSigning);
                        return new ProjectBuildWorkflowResult { Result = new ProjectBuildResult { Success = true } };
                    });
                    break;
                case "executable-signing":
                    var step = new DotNetPublishStep { Key = "publish", Kind = DotNetPublishStepKind.Publish, TargetName = "Sample" };
                    ExecutablePublishConsoleUi.RunInteractive(view, new DotNetPublishPlan { Steps = [step] }, null, progress =>
                    {
                        progress.StepStarting(step);
                        ((IDotNetPublishProgressReporterV2)progress).StepProgress(step, "Signing", Detail);
                        Running();
                        progress.StepCompleted(step);
                        return new DotNetPublishResult { Succeeded = true };
                    });
                    break;
                case "release-signing":
                    var tool = new DotNetPublishStep { Key = "publish", Kind = DotNetPublishStepKind.Publish, TargetName = "Sample" };
                    PowerForgeReleaseConsoleUi.RunInteractive(view,
                        new PowerForgeReleaseSpec { Module = new PowerForgeModuleReleaseOptions { ModuleName = "Sample" }, Tools = new PowerForgeToolReleaseSpec() },
                        new PowerForgeReleaseRequest { ToolsOnly = true },
                        progress =>
                        {
                            progress.PhaseStarted(PowerForgeReleaseProgressPhase.Tools, 0);
                            var adapter = new DotNetPublishReleaseProgressAdapter((IPowerForgeReleaseProgressReporterV2)progress, new DotNetPublishPlan { Steps = [tool] });
                            adapter.StepStarting(tool);
                            adapter.StepProgress(tool, "Signing", Detail);
                            Running();
                            adapter.StepCompleted(tool);
                            progress.PhaseCompleted(PowerForgeReleaseProgressPhase.Tools);
                            return new PowerForgeReleaseResult { Success = true };
                        });
                    break;
                default:
                    Assert.Equal(42, StatusConsoleUi.Run(view, Detail, () => { Running(); return 42; }));
                    break;
            }
        });

        Assert.Contains("Current: ", text, StringComparison.Ordinal);
        Assert.Contains(Detail, text, StringComparison.Ordinal);
        if (route != "status") Assert.Contains("PIN, password, touch", text, StringComparison.Ordinal);
        var ledger = text.LastIndexOf("[01/", StringComparison.Ordinal);
        var message = text.LastIndexOf(Diagnostic, StringComparison.Ordinal);
        Assert.True(ledger >= 0 && message > ledger, text);
    }

    [Fact]
    public void Status_ShowsFailureForAnUnsuccessfulResultAndWhenTheOperationThrows()
    {
        var expected = new DotNetRepositoryReleaseResult { Success = false };
        DotNetRepositoryReleaseResult? actual = null;
        var unsuccessful = ConsoleCapture.Final(view =>
            actual = StatusConsoleUi.Run(view, "Publish packages", () => expected, value => value.Success));
        Assert.Same(expected, actual);
        Assert.Contains("Publish packages", unsuccessful, StringComparison.Ordinal);
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(unsuccessful, @"\[01/01\] \[FAIL\] Publish packages"), unsuccessful);

        var error = new InvalidOperationException("signer unavailable");
        var thrown = ConsoleCapture.Final(view =>
        {
            var logger = new ConsoleOutputLogger();
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
                StatusConsoleUi.Run<int>(view, "Publish packages", () =>
                {
                    logger.Error("Error: " + new string('x', 300) + " complete diagnostic suffix.");
                    throw error;
                })));
        });
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(thrown, @"\[01/01\] \[FAIL\] Publish packages"), thrown);
        Assert.Contains("complete diagnostic suffix.", thrown, StringComparison.Ordinal);
        Assert.True(thrown.LastIndexOf("complete diagnostic suffix.", StringComparison.Ordinal) > thrown.LastIndexOf("[01/01]", StringComparison.Ordinal), thrown);
    }
}

/// <summary>
/// Runs console UI against a fixed-size recording target and returns what a terminal would keep. A live session
/// owns the process console while it runs, so tests that start one share a non-parallel collection.
/// </summary>
internal static class ConsoleCapture
{
    internal const string Collection = "Console live session";

    /// <summary>Append-only output (no live frames): the header, the final ledger and the messages.</summary>
    internal static string Final(Action<ProgressView> run, int width = 100, GlyphTier glyphs = GlyphTier.Ascii)
    {
        var target = TestTerminal.Target(width, 40, glyphs);
        using (Terminal.Use(target))
            run(new ProgressView(() => TimeSpan.Zero));
        return target.Text;
    }

    /// <summary>The final ledger: from its first row to the end.</summary>
    internal static string Ledger(string output) => output.Substring(output.IndexOf("[01/", StringComparison.Ordinal));

    /// <summary>The header block (up to its blank line) and the final ledger, without the append-only event lines.</summary>
    internal static string HeaderAndLedger(string output)
        => output.Substring(0, output.IndexOf("\n\n", StringComparison.Ordinal) + 2) + Ledger(output);

    /// <summary>An interactive target: live frames are drawn too, so running rows and "Current:" appear.</summary>
    internal static string Live(Action<ProgressView> run, int width = 100)
    {
        var target = new RecordingTarget(TestTerminal.Profile(width, 40, GlyphTier.Ascii).WithMode(OutputMode.Interactive));
        using (Terminal.Use(target))
            run(new ProgressView());
        return target.Text;
    }
}

[CollectionDefinition(ConsoleCapture.Collection, DisableParallelization = true)]
public sealed class ConsoleLiveSessionCollection;
