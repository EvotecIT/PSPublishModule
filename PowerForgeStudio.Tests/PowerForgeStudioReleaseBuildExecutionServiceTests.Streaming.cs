using System.Collections.Concurrent;
using PowerForge;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public async Task PowerShellProjectBuildReportsSanitizedLinesBeforeItCompletes()
    {
        using var scope = new TemporaryDirectoryScope();
        var root = scope.CreateDirectory("ScriptProject");
        var build = scope.CreateDirectory(Path.Combine("ScriptProject", "Build"));
        File.WriteAllText(Path.Combine(build, "Build-Project.ps1"), "# fixture");
        var runner = new PausedPowerShellRunner();
        var progress = new BuildProgressSink();
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(
                new NullLogger(),
                executeRelease: _ => throw new InvalidOperationException("JSON build was not selected."),
                publishGitHub: null,
                validateGitHubPreflight: null),
            new ProjectBuildCommandHostService(runner),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()));

        var pending = service.ExecuteAsync(root, progress: progress);
        await runner.LinesEmitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.False(pending.IsCompleted);
            var live = progress.Updates.ToArray();
            Assert.Contains(live, update => update.Phase == "ProjectBuild" && update.State == "Output" &&
                                            update.Detail.Contains("restoring packages", StringComparison.Ordinal));
            Assert.Contains(live, update => update.Phase == "ProjectBuild" && update.State == "Error output" &&
                                            update.Detail.Contains("warning from compiler", StringComparison.Ordinal));
            Assert.DoesNotContain(live, update => update.Detail.Contains("private-secret", StringComparison.Ordinal));
        }
        finally { runner.AllowCompletion.TrySetResult(); }
        var result = await pending;
        Assert.True(result.Succeeded, result.Summary);
    }

    private sealed class BuildProgressSink : IProgress<ReleaseBuildProgress>
    {
        public ConcurrentQueue<ReleaseBuildProgress> Updates { get; } = new();
        public void Report(ReleaseBuildProgress value) => Updates.Enqueue(value);
    }

    private sealed class PausedPowerShellRunner : IPowerShellRunner, ICancellablePowerShellRunner
    {
        public TaskCompletionSource LinesEmitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("The cancellable PowerShell path was expected.");

        public async Task<PowerShellRunResult> RunAsync(PowerShellRunRequest request, CancellationToken cancellationToken)
        {
            request.OutputLineReceived?.Invoke("restoring packages from https://feed.example.invalid/index.json?token=private-secret");
            request.ErrorLineReceived?.Invoke("warning from compiler");
            LinesEmitted.TrySetResult();
            await AllowCompletion.Task.WaitAsync(cancellationToken);
            return new PowerShellRunResult(0, "restoring packages", "warning from compiler", "pwsh");
        }
    }
}
