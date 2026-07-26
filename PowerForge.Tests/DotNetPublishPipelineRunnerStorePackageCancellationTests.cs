namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerStorePackageTests
{
    [Fact]
    public async Task Run_StorePackage_ForwardsCancellationToBuildProcess()
    {
        var root = CreateTempRoot();
        try
        {
            var app = CreateProject(root, "App/App.csproj");
            var packaging = CreateProject(root, "Store/Package.csproj");
            var spec = CreateBaseSpec(root, app);
            spec.StorePackages =
            [
                new DotNetPublishStorePackage
                {
                    Id = "app.store",
                    PrepareFromTarget = "app",
                    PackagingProjectPath = packaging
                }
            ];
            var processRunner = new CancellationProbeProcessRunner();
            var runner = new DotNetPublishPipelineRunner(new NullLogger(), processRunner);
            var plan = runner.Plan(spec, null);
            plan.Steps = plan.Steps
                .Where(step => step.Kind == DotNetPublishStepKind.StorePackage)
                .ToArray();
            using var cancellation = new CancellationTokenSource();

            var execution = Task.Run(() => runner.Run(plan, progress: null, cancellationToken: cancellation.Token));
            await processRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
            Assert.True(processRunner.ObservedCancellation);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private sealed class CancellationProbeProcessRunner : IProcessRunner
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool ObservedCancellation { get; private set; }

        public async Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The process runner was expected to be canceled.");
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = cancellationToken.IsCancellationRequested;
                throw;
            }
        }
    }
}
