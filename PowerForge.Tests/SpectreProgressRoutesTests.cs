using PowerForge.ConsoleShared;
using Spectre.Console;
using System.Text.RegularExpressions;

namespace PowerForge.Tests;

public sealed class SpectreProgressRoutesTests
{
    [Theory]
    [InlineData("project", 60)]
    [InlineData("project", 120)]
    [InlineData("executable", 60)]
    [InlineData("release", 60)]
    [InlineData("status", 60)]
    public void Routes_ShowCurrentOperationAndPreserveDiagnosticsBelowProgress(string route, int width)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalOutput(writer, width), Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors, Interactive = InteractionSupport.Yes
        });
        var logger = new SpectreConsoleLogger(console);
        const string detail = "Authorize the certificate provider";
        void Diagnostic() => logger.Warn("Complete recovery instructions remain visible.");

        if (route == "project")
        {
            SpectreProjectBuildConsoleUi.RunInteractive(console,
                new ProjectBuildConsolePlan { SignPackages = true },
                progress =>
                {
                    progress.PhaseStarted(ProjectBuildProgressPhase.PackageSigning, 0, detail);
                    Diagnostic();
                    progress.PhaseCompleted(ProjectBuildProgressPhase.PackageSigning);
                    return new ProjectBuildWorkflowResult { Result = new ProjectBuildResult { Success = true } };
                });
        }
        else if (route == "executable")
        {
            var step = new DotNetPublishStep { Key = "sign", Kind = DotNetPublishStepKind.MsiSign, InstallerId = detail };
            SpectreDotNetPublishConsoleUi.RunInteractive(console, new DotNetPublishPlan { Steps = [step] }, null,
                progress =>
                {
                    progress.StepStarting(step);
                    Diagnostic();
                    progress.StepCompleted(step);
                    return new DotNetPublishResult { Succeeded = true };
                });
        }
        else if (route == "release")
        {
            var spec = new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions { ModuleName = "Sample" }
            };
            SpectrePowerForgeReleaseConsoleUi.RunInteractive(console, spec,
                new PowerForgeReleaseRequest { ModuleOnly = true },
                progress =>
                {
                    progress.PhaseStarted(PowerForgeReleaseProgressPhase.Module, 0, detail);
                    Diagnostic();
                    progress.PhaseCompleted(PowerForgeReleaseProgressPhase.Module);
                    return new PowerForgeReleaseResult { Success = true };
                });
        }
        else
        {
            Assert.Equal(42, SpectreStatusConsoleUi.Run(console, detail, () => { Diagnostic(); return 42; }));
        }

        var plain = Regex.Replace(writer.ToString(), @"\x1b\[[0-?]*[ -/]*[@-~]", string.Empty);
        var normalized = Regex.Replace(plain, @"\s+", " ");
        Assert.Contains("Current:", normalized);
        Assert.Contains(detail, normalized);
        Assert.Contains("running", normalized);
        Assert.DoesNotContain("5%", normalized);
        if (route is "project" or "executable")
            Assert.Contains("PIN, password, touch", normalized);
        var lastProgress = plain.LastIndexOf("done", StringComparison.Ordinal);
        var lastDiagnostic = plain.LastIndexOf("Complete recovery instructions", StringComparison.Ordinal);
        Assert.True(lastProgress >= 0 && lastDiagnostic > lastProgress, plain);
        Assert.Matches(@"done[^\r\n]*\r?\n[ \t]*\r?\n", plain[lastProgress..]);
    }

    [Fact]
    public void Status_PreservesUnsuccessfulReturnedResultAndShowsFailure()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalOutput(writer, 80), Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors, Interactive = InteractionSupport.Yes
        });
        var expected = new DotNetRepositoryReleaseResult { Success = false };

        var actual = SpectreStatusConsoleUi.Run(console, "Publish packages", () => expected,
            value => value.Success);

        Assert.Same(expected, actual);
        var plain = Regex.Replace(writer.ToString(), @"\x1b\[[0-?]*[ -/]*[@-~]", string.Empty);
        Assert.Contains("failed", plain);
        Assert.DoesNotContain("done", plain);
    }

    [Fact]
    public void Status_RetainsDiagnosticAndFailureStateWhenOperationThrows()
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalOutput(writer, 80), Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors, Interactive = InteractionSupport.Yes
        });
        var logger = new SpectreConsoleLogger(console);
        Assert.Throws<InvalidOperationException>(() =>
            SpectreStatusConsoleUi.Run<int>(console, "Publish packages", () =>
            {
                logger.Error("Publication could not finish.");
                throw new InvalidOperationException("failed");
            }));
        Assert.Contains("failed", writer.ToString());
        Assert.Contains("Publication could not finish.", writer.ToString());
    }

    private sealed class TerminalOutput(TextWriter writer, int width) : IAnsiConsoleOutput
    {
        public TextWriter Writer => writer;
        public bool IsTerminal => true;
        public int Width => width;
        public int Height => 22;
        public void SetEncoding(System.Text.Encoding encoding) { }
    }
}
