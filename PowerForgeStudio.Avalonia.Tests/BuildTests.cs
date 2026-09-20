using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class BuildTests
{
    [Fact]
    public async Task SwitchingWorkingCopyCancelsAndDiscardsAnInFlightResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-build-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        await File.WriteAllTextAsync(Path.Combine(root, "Build", "project.build.json"), "{}");
        var planner = new DelayedPlanner();
        try
        {
            using var model = new BuildViewModel(planner);
            model.SetWorkingCopy(root);
            var pending = model.PlanAsync();
            await planner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            model.SetWorkingCopy(Path.Combine(root, "other"));
            Assert.True(planner.Token.IsCancellationRequested);
            planner.Complete.SetResult([new RepositoryPlanResult(RepositoryPlanAdapterKind.ProjectPlan,
                RepositoryPlanStatus.Succeeded, "Old result", null, 0, 0)]);
            await pending;
            Assert.Empty(model.Results);
            Assert.Equal("Ready to inspect and plan this working copy.", model.Status);
            Assert.False(model.IsBusy);
        }
        finally
        {
            planner.Complete.TrySetResult([]);
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class DelayedPlanner : IRepositoryPlanPreviewService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<RepositoryPlanResult>> Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<IReadOnlyList<RepositoryPlanResult>> PlanRepositoryAsync(RepositoryCatalogEntry repository, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Started.SetResult();
            return Complete.Task;
        }
    }

    [Fact]
    public async Task ModuleJsonPlanningRendersAndChangingWorkingCopyClearsResults()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-build-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, "powerforge.json");
        await File.WriteAllTextAsync(config, """{"Build":{"Name":"SampleModule","SourcePath":".","Version":"1.0.0"}}""");
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                using var workspace = new WorkspaceViewModel(root) { ActiveWorkingCopyRoot = root, ProjectName = "SampleModule" };
                workspace.ShowBuildCommand.Execute(null);
                await workspace.Build.PlanAsync();
                var result = Assert.Single(workspace.Build.Results);
                Assert.Equal(RepositoryPlanStatus.Succeeded, result.Status);
                Assert.Equal(config, result.PlanPath);
                Assert.False(workspace.Build.IsBusy);
                var window = new MainWindow { DataContext = workspace, Width = 1600, Height = 1000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, "build-plan.png"), PngBitmapEncoderOptions.Default);
                    }
                    window.Width = 1050;
                    window.Height = 720;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var compact = window.CaptureRenderedFrame();
                    Assert.NotNull(compact);
                    if (!string.IsNullOrEmpty(output)) compact.Save(Path.Combine(output, "build-plan-compact.png"), PngBitmapEncoderOptions.Default);
                    workspace.ActiveWorkingCopyRoot = "";
                    Assert.Empty(workspace.Build.Results);
                    Assert.False(workspace.Build.CanPlan);
                }
                finally { window.Close(); }
                return true;
            });
            Assert.Equal(["powerforge.json"], Directory.GetFiles(root).Select(Path.GetFileName));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
