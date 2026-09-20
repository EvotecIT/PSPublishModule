using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class ReleasePreparationTests
{
    [Fact]
    public async Task NewBuildInvalidatesLatePreparationAndCancelledBuildCannotPrepare()
    {
        await TestAppBuilder.RunAsync(async () =>
        {
            var service = new WaitingHandoff();
            using var model = new ReleaseViewModel(service);
            var build = new ReleaseBuildExecutionResult(Path.GetTempPath(), true, "Built", 1, []);
            model.SetBuild(build, false, false);
            var pending = model.PrepareAsync();
            model.SetBuild(null, true, false);
            service.Completion.SetResult(new(ReleaseQueueSessionFactory.Create(Path.GetTempPath(), [], DateTimeOffset.UtcNow), []));
            await pending;
            Assert.False(model.HasHandoff); Assert.False(model.CanPrepare); Assert.Empty(model.Artifacts);
            model.SetBuild(build, false, true);
            Assert.False(model.CanPrepare); Assert.Contains("cancelled", model.Status);
            return true;
        });
    }

    private sealed class WaitingHandoff : IReleaseBuildHandoffService
    {
        public TaskCompletionSource<ReleaseBuildHandoff> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ReleaseBuildHandoff> PrepareAsync(ReleaseBuildExecutionResult build, CancellationToken token = default) => Completion.Task;
    }
}
