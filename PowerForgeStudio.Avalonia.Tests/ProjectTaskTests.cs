using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class ProjectTaskTests
{
    [Fact]
    public async Task InvalidTaskConfigurationRemainsVisibleAfterInspection()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-task-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        await File.WriteAllTextAsync(Path.Combine(root, "Build", "powerforge.tasks.json"),
            """{ "SchemaVersion": 1, "Tasks": [{ "Id": "broken", "Name": "Broken", "Executable": "dotnet", "TimeoutSecounds": 30 }] }""");
        try
        {
            using var model = new BuildViewModel();
            model.SetWorkingCopy(root);
            Assert.False(model.ShowTaskSection);
            await model.PlanAsync();
            Assert.True(model.HasTaskConfigurationError);
            Assert.True(model.ShowTaskSection);
            Assert.Contains("unsupported property", model.TaskCatalogStatus);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ProjectTaskAndReleasePreparationBlockEachOther()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-task-interlock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        await File.WriteAllTextAsync(Path.Combine(root, "Build", "powerforge.tasks.json"),
            """{ "SchemaVersion": 1, "Tasks": [{ "Id": "sdk", "Name": "Check SDK", "Executable": "dotnet", "Arguments": ["--version"] }] }""");
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var handoff = new WaitingHandoff();
                using var workspace = new WorkspaceViewModel(root, release: new ReleaseViewModel(handoff));
                workspace.Build.SetWorkingCopy(root);
                await workspace.Build.PlanAsync();
                workspace.Build.SelectedTask = Assert.Single(workspace.Build.Tasks);
                workspace.Release.SetBuild(new ReleaseBuildExecutionResult(root, true, "Built", 1, []), false, false);
                Assert.True(workspace.Build.CanRunTask);
                Assert.True(workspace.Release.CanPrepare);

                workspace.Build.IsTaskRunning = true;
                Assert.True(workspace.Release.IsProjectTaskRunning);
                Assert.False(workspace.Release.CanPrepare);
                workspace.Build.IsTaskRunning = false;

                var preparing = workspace.Release.PrepareAsync();
                Assert.True(workspace.Release.IsPreparing);
                Assert.True(workspace.Build.IsReleaseRunning);
                Assert.False(workspace.Build.CanRunTask);
                handoff.Completion.SetResult(new(ReleaseQueueSessionFactory.Create(root, [], DateTimeOffset.UtcNow), []));
                await preparing;
                Assert.False(workspace.Build.IsReleaseRunning);
                Assert.True(workspace.Release.CanSign);
                workspace.Build.IsTaskRunning = true;
                Assert.False(workspace.Release.CanSign);
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task JsonOnlyTaskCanBeReviewedRunAndInvalidatedFromBuildPage()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-task-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        await File.WriteAllTextAsync(Path.Combine(root, "Build", "powerforge.tasks.json"),
            """
            { "SchemaVersion": 1, "Tasks": [
              { "Id": "sdk", "Name": "Check SDK", "Description": "Confirm the selected .NET SDK.", "Executable": "dotnet",
                "Arguments": ["--version"], "WorkingDirectory": ".", "TimeoutSeconds": 30 }
            ] }
            """);
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new BuildViewModel();
                model.SetWorkingCopy(root);
                await model.PlanAsync();
                Assert.False(model.CanBuild);
                var task = Assert.Single(model.Tasks);
                Assert.True(model.ShowTaskSection);
                Assert.Equal("sdk", task.Id);
                Assert.Contains("Project tasks are ready", model.Status);

                var view = new BuildView { DataContext = model };
                var window = new Window { Content = view, Width = 900, Height = 720 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    model.SelectedTask = task;
                    Assert.True(model.CanRunTask);
                    Assert.Equal("dotnet", model.SelectedTaskExecutable);
                    Assert.Equal("Confirm the selected .NET SDK.", model.SelectedTaskDescription);
                    Assert.Contains("--version", model.SelectedTaskArguments);
                    await model.RunTaskAsync();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Assert.Contains("completed", model.TaskStatus);
                    Assert.Contains("Task · Output", model.TaskOutput);
                    Assert.False(model.IsTaskRunning);
                    var heading = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Task run");
                    var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
                    Assert.True(scroll.Offset.Y > 0);
                    Assert.True(heading.IsEffectivelyVisible);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, "project-task-result.png"), PngBitmapEncoderOptions.Default);
                    }

                    model.HasUnsavedChanges = true;
                    Assert.Empty(model.Tasks);
                    Assert.False(model.CanRunTask);
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class WaitingHandoff : IReleaseBuildHandoffService
    {
        public TaskCompletionSource<ReleaseBuildHandoff> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ReleaseBuildHandoff> PrepareAsync(ReleaseBuildExecutionResult build, CancellationToken token = default) => Completion.Task;
    }
}
