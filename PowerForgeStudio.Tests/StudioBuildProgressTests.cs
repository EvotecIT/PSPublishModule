using PowerForge;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class StudioBuildProgressTests
{
    [Fact]
    public void ProgressPreservesPhaseAndRedactsRecognizedSecretArguments()
    {
        var values = new List<ReleaseBuildProgress>();
        var reporter = new ReleaseBuildProgressAdapter(new InlineProgress(values.Add));
        reporter.PhaseStarted(ProjectBuildProgressPhase.PackageBuild, 1, "packing --ApiKey secret-value");
        var value = Assert.Single(values);
        Assert.Equal("PackageBuild", value.Phase);
        Assert.Equal("Started", value.State);
        Assert.DoesNotContain("secret-value", value.Detail);
        Assert.Contains("<redacted>", value.Detail);
        Assert.Equal(4096, StudioOutputSanitizer.Sanitize(new string('x', 5000)).Length);
    }

    private sealed class InlineProgress(Action<ReleaseBuildProgress> report) : IProgress<ReleaseBuildProgress>
    {
        public void Report(ReleaseBuildProgress value) => report(value);
    }
}
