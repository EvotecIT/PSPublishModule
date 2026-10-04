using PowerForge.ConsoleShared;
using Spectre.Console;

namespace PowerForge.Tests;

public sealed class DotNetPublishConsoleTests
{
    [Theory]
    [InlineData(80)]
    [InlineData(72)]
    [InlineData(160)]
    public void Failure_FinalizesEveryPlannedStepAndPreservesLiteralDiagnostics(int width)
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, width);
        var steps = new[]
        {
            new DotNetPublishStep { Key = "restore", Kind = DotNetPublishStepKind.Restore, Title = "Restore [App]" },
            new DotNetPublishStep { Key = "build", Kind = DotNetPublishStepKind.Build, Title = "Build [App]" },
            new DotNetPublishStep { Key = "publish", Kind = DotNetPublishStepKind.Publish, TargetName = "App[dev]", Runtime = "win-x64", Framework = "net8.0", Style = DotNetPublishStyle.FrameworkDependent },
            new DotNetPublishStep { Key = "manifest", Kind = DotNetPublishStepKind.Manifest, Title = "Write manifest" }
        };
        var result = SpectreDotNetPublishConsoleUi.RunInteractive(console,
            new DotNetPublishPlan { ProjectRoot = "source[dev]", Steps = steps }, "publish.json", progress =>
            {
                progress.StepStarting(steps[0]);
                progress.StepCompleted(steps[0]);
                progress.StepStarting(steps[1]);
                progress.StepCompleted(steps[1]);
                progress.StepStarting(steps[2]);
                progress.StepFailed(steps[2], new InvalidOperationException("missing [reference]"));
                return new DotNetPublishResult { Succeeded = false, ErrorMessage = "missing [reference]" };
            });

        Assert.False(result.Succeeded);
        var output = writer.ToString();
        Assert.Contains("Restore [App]", output);
        Assert.Contains("Build [App]", output);
        Assert.Contains("missing [reference]", output);
        Assert.Contains("Write manifest", output);
        Assert.Contains("skipped after failure", output);
    }

    [Fact]
    public void UnexpectedFailure_PreservesExceptionAndFinalizesActiveAndPendingSteps()
    {
        using var writer = new StringWriter();
        var steps = new[]
        {
            new DotNetPublishStep { Key = "build", Kind = DotNetPublishStepKind.Build, Title = "Build App" },
            new DotNetPublishStep { Key = "manifest", Kind = DotNetPublishStepKind.Manifest, Title = "Write manifest" }
        };
        var error = new IOException("output unavailable");
        var actual = Assert.Throws<IOException>(() => SpectreDotNetPublishConsoleUi.RunInteractive(
            CreateConsole(writer, 140), new DotNetPublishPlan { Steps = steps }, null, progress =>
            {
                progress.StepStarting(steps[0]);
                throw error;
            }));
        Assert.Same(error, actual);
        Assert.Contains("Build App", writer.ToString());
        Assert.Contains("skipped after failure", writer.ToString());
    }

    [Fact]
    public void Success_LabelsEachPublishStepWithItsActualStyle()
    {
        using var writer = new StringWriter();
        var steps = new[]
        {
            new DotNetPublishStep { Key = "portable", Kind = DotNetPublishStepKind.Publish, TargetName = "App", Style = DotNetPublishStyle.PortableSize },
            new DotNetPublishStep { Key = "contained", Kind = DotNetPublishStepKind.Publish, TargetName = "App", Style = DotNetPublishStyle.SelfContained }
        };
        var result = SpectreDotNetPublishConsoleUi.RunInteractive(CreateConsole(writer, 140),
            new DotNetPublishPlan { Steps = steps }, null, progress =>
            {
                foreach (var step in steps) { progress.StepStarting(step); progress.StepCompleted(step); }
                return new DotNetPublishResult { Succeeded = true };
            });
        Assert.True(result.Succeeded);
        Assert.Contains("PortableSize", writer.ToString());
        Assert.Contains("SelfContained", writer.ToString());
    }

    [Fact]
    public void CommandFailure_ShowsHeadlineAndOutputTailWithoutRepeatingCommandDetails()
    {
        using var writer = new StringWriter();
        var step = new DotNetPublishStep { Key = "build", Kind = DotNetPublishStepKind.Build, Title = "Build App" };
        const string message = "Build failed\r\nCommand: detailed command retained in result";
        var result = SpectreDotNetPublishConsoleUi.RunInteractive(CreateConsole(writer, 80),
            new DotNetPublishPlan { Steps = [step] }, null, progress =>
            {
                progress.StepStarting(step);
                progress.StepFailed(step, new InvalidOperationException(message));
                return new DotNetPublishResult
                {
                    Succeeded = false, ErrorMessage = message,
                    Failure = new DotNetPublishFailure { StdOutTail = "Compiler diagnostic [App]", LogPath = "build.log" }
                };
            });
        Assert.Equal(message, result.ErrorMessage);
        Assert.Contains("Build failed", writer.ToString());
        Assert.Contains("Compiler diagnostic [App]", writer.ToString());
        Assert.Contains("build.log", writer.ToString());
        Assert.DoesNotContain("detailed command retained in result", writer.ToString());
    }

    private static IAnsiConsole CreateConsole(TextWriter writer, int width)
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalOutput(writer, width), Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors, Interactive = InteractionSupport.Yes
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
