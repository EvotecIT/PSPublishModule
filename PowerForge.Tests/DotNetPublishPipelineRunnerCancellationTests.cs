namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerCancellationTests
{
    [Fact]
    public async Task Run_cancels_the_active_dotnet_process()
    {
        using var scope = new CancellationTokenSource();
        var processRunner = new WaitingProcessRunner();
        var runner = new DotNetPublishPipelineRunner(new NullLogger(), processRunner);
        var plan = new DotNetPublishPlan
        {
            ProjectRoot = Directory.GetCurrentDirectory(),
            SolutionPath = "Sample.sln",
            Steps =
            [
                new DotNetPublishStep
                {
                    Key = "restore",
                    Kind = DotNetPublishStepKind.Restore,
                    Title = "Restore"
                }
            ]
        };

        var execution = Task.Run(() => runner.Run(plan, progress: null, scope.Token));
        await processRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scope.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(processRunner.ObservedToken.CanBeCanceled);
    }

    private sealed class WaitingProcessRunner : IProcessRunner
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal CancellationToken ObservedToken { get; private set; }

        public async Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            ObservedToken = cancellationToken;
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The process runner was not cancelled.");
        }
    }
}
