using PowerForge.ConsoleUi;

namespace PowerForge.Tests;

[Collection(ConsoleCapture.Collection)]
public sealed class DotNetPublishConsoleTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(160)]
    public void Failure_FinalizesEveryPlannedStepAndPreservesLiteralDiagnostics(int width)
    {
        var steps = new[]
        {
            new DotNetPublishStep { Key = "restore", Kind = DotNetPublishStepKind.Restore, Title = "Restore [App]" },
            new DotNetPublishStep { Key = "build", Kind = DotNetPublishStepKind.Build, Title = "Build [App]" },
            new DotNetPublishStep { Key = "publish", Kind = DotNetPublishStepKind.Publish, TargetName = "App[dev]", Runtime = "win-x64", Framework = "net8.0", Style = DotNetPublishStyle.FrameworkDependent },
            new DotNetPublishStep { Key = "manifest", Kind = DotNetPublishStepKind.Manifest, Title = "Write manifest" }
        };
        DotNetPublishResult? result = null;
        var output = ConsoleCapture.Final(view => result = ExecutablePublishConsoleUi.RunInteractive(view,
            new DotNetPublishPlan { ProjectRoot = "source[dev]", Steps = steps }, "publish.json", progress =>
            {
                progress.StepStarting(steps[0]);
                progress.StepCompleted(steps[0]);
                progress.StepStarting(steps[1]);
                progress.StepCompleted(steps[1]);
                progress.StepStarting(steps[2]);
                progress.StepFailed(steps[2], new InvalidOperationException("missing [reference]"));
                return new DotNetPublishResult { Succeeded = false, ErrorMessage = "missing [reference]" };
            }), width);

        Assert.False(result!.Succeeded);
        Assert.Contains("Restore [App]", output);
        Assert.Contains("Build [App]", output);
        Assert.Contains("missing [reference]", output);
        Assert.Contains("Write manifest", output);
        Assert.Contains("skipped after failure", output);
    }

    [Fact]
    public void UnexpectedFailure_PreservesExceptionAndFinalizesActiveAndPendingSteps()
    {
        var steps = new[]
        {
            new DotNetPublishStep { Key = "build", Kind = DotNetPublishStepKind.Build, Title = "Build App" },
            new DotNetPublishStep { Key = "manifest", Kind = DotNetPublishStepKind.Manifest, Title = "Write manifest" }
        };
        var error = new IOException("output unavailable");
        Exception? actual = null;
        var output = ConsoleCapture.Final(view => actual = Record.Exception(() => ExecutablePublishConsoleUi.RunInteractive(
            view, new DotNetPublishPlan { Steps = steps }, null, progress =>
            {
                progress.StepStarting(steps[0]);
                throw error;
            })), 140);
        Assert.Same(error, actual);
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(output, @"\[FAIL\] Build App\s+stopped"), output);
        Assert.Contains("skipped after failure", output);
    }

    [Fact]
    public void Success_LabelsEachPublishStepWithItsActualStyle()
    {
        var steps = new[]
        {
            new DotNetPublishStep { Key = "portable", Kind = DotNetPublishStepKind.Publish, TargetName = "App", Style = DotNetPublishStyle.PortableSize },
            new DotNetPublishStep { Key = "contained", Kind = DotNetPublishStepKind.Publish, TargetName = "App", Style = DotNetPublishStyle.SelfContained }
        };
        DotNetPublishResult? result = null;
        var output = ConsoleCapture.Final(view => result = ExecutablePublishConsoleUi.RunInteractive(view,
            new DotNetPublishPlan { Steps = steps }, null, progress =>
            {
                foreach (var step in steps) { progress.StepStarting(step); progress.StepCompleted(step); }
                return new DotNetPublishResult { Succeeded = true };
            }), 140);
        Assert.True(result!.Succeeded);
        Assert.Contains("PortableSize", output);
        Assert.Contains("SelfContained", output);
    }

    [Fact]
    public void CommandFailure_ShowsHeadlineAndOutputTailWithoutRepeatingCommandDetails()
    {
        var step = new DotNetPublishStep { Key = "build", Kind = DotNetPublishStepKind.Build, Title = "Build App" };
        const string message = "Build failed\r\nCommand: detailed command retained in result";
        DotNetPublishResult? result = null;
        var output = ConsoleCapture.Final(view => result = ExecutablePublishConsoleUi.RunInteractive(view,
            new DotNetPublishPlan { Steps = [step] }, null, progress =>
            {
                progress.StepStarting(step);
                progress.StepFailed(step, new InvalidOperationException(message));
                return new DotNetPublishResult
                {
                    Succeeded = false, ErrorMessage = message,
                    Failure = new DotNetPublishFailure { StdOutTail = "Compiler diagnostic [App]", LogPath = "build.log" }
                };
            }), 80);
        Assert.Equal(message, result!.ErrorMessage);
        Assert.Contains("Build failed", output);
        Assert.Contains("Compiler diagnostic [App]", output);
        Assert.Contains("build.log", output);
        Assert.DoesNotContain("detailed command retained in result", output);
    }
}
