using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class BuildTests
{
    [Fact]
    public async Task EditingAndSavingDuringInspectionInvalidatesItsLateResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-build-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        await File.WriteAllTextAsync(Path.Combine(root, "Build", "project.build.json"), "{}");
        var planner = new DelayedPlanner();
        try
        {
            using var model = new BuildViewModel(planner);
            model.SetWorkingCopy(root);
            var pending = model.PlanAsync();
            await planner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            model.HasUnsavedChanges = true;
            Assert.True(planner.Token.IsCancellationRequested);
            model.HasUnsavedChanges = false;
            planner.Complete.SetResult([new RepositoryPlanResult(RepositoryPlanAdapterKind.ProjectPlan,
                RepositoryPlanStatus.Succeeded, "Before edit", null, 0, 0)]);
            await pending;
            Assert.Empty(model.Results);
            Assert.False(model.HasSuccessfulInspection);
            Assert.False(model.CanBuild);
            Assert.True(model.CanPlan);
        }
        finally { planner.Complete.TrySetResult([]); Directory.Delete(root, recursive: true); }
    }

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
        Assert.Equal("Ready to inspect and plan this project.", model.Status);
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
    public async Task ScriptInspectionKeepsTheExecutionWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-script-notice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        await File.WriteAllTextAsync(Path.Combine(root, "Build", "Build-Project.ps1"), "# Planning fixture only");
        var planner = new DelayedPlanner();
        try
        {
            using var model = new BuildViewModel(planner);
            model.SetWorkingCopy(root);
            var pending = model.PlanAsync();
            await planner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            planner.Complete.SetResult([]);
            await pending;
            Assert.True(model.PlanningUsesScript);
            Assert.Contains("side effects", model.PlanningNotice, StringComparison.Ordinal);
            Assert.Contains("Project scripts", model.ContextSafetyNotice, StringComparison.Ordinal);
        }
        finally { planner.Complete.TrySetResult([]); Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PlannerFailureRedactsRecognizedSecretArgumentsAndRestoresInspectionAction()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-build-error-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        await File.WriteAllTextAsync(Path.Combine(root, "Build", "project.build.json"), "{}");
        var planner = new DelayedPlanner();
        try
        {
            using var model = new BuildViewModel(planner);
            model.SetWorkingCopy(root);
            Assert.True(model.EmphasizePlan);
            var pending = model.PlanAsync();
            await planner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            planner.Complete.SetException(new InvalidOperationException("Planner failed with --Token=fixture-secret"));
            await pending;
            Assert.Contains("<redacted>", model.Status);
            Assert.DoesNotContain("fixture-secret", model.Status, StringComparison.Ordinal);
            Assert.True(model.EmphasizePlan);
            Assert.False(model.CanBuild);
        }
        finally { planner.Complete.TrySetResult([]); Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PlanResultDiagnosticsAreRedactedBeforeDisplay()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-plan-diagnostic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        await File.WriteAllTextAsync(Path.Combine(root, "Build", "project.build.json"), "{}");
        var planner = new DelayedPlanner();
        try
        {
            using var model = new BuildViewModel(planner);
            model.SetWorkingCopy(root);
            var pending = model.PlanAsync();
            await planner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            planner.Complete.SetResult([new RepositoryPlanResult(RepositoryPlanAdapterKind.ProjectPlan,
                RepositoryPlanStatus.Failed, "Failed with --Token=fixture-secret", null, 1, 0,
                "stdout --ApiKey=fixture-secret", "stderr --Password=fixture-secret")]);
            await pending;

            var result = Assert.Single(model.Results);
            Assert.Contains("<redacted>", result.Summary);
            Assert.Contains("<redacted>", result.OutputTail);
            Assert.Contains("<redacted>", result.ErrorTail);
            Assert.DoesNotContain("fixture-secret", string.Join(" ", result.Summary, result.OutputTail, result.ErrorTail), StringComparison.Ordinal);
        }
        finally { planner.Complete.TrySetResult([]); Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ModuleJsonPlanningRendersAndChangingWorkingCopyClearsResults()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-build-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, "powerforge.json");
        await File.WriteAllTextAsync(config,
            """
            {
              "Build": { "Name": "SampleModule", "SourcePath": ".", "Version": "1.0.0" },
              "Install": { "Enabled": false },
              "Segments": [
                { "Type": "Packed", "Configuration": { "Enabled": true, "Path": "Artifacts/SampleModule.zip" } },
                { "Type": "Execute", "Configuration": { "Name": "Generate release metadata", "At": "AfterBuild", "InlineScript": "Write-Output 'hidden-value'", "Environment": { "TOKEN": "hidden-value" } } },
                { "Type": "GalleryNuget", "Configuration": { "Enabled": true, "Destination": "PowerShellGallery", "RepositoryName": "PSGallery", "ApiKey": "hidden-value" } }
              ]
            }
            """);
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                using var workspace = new WorkspaceViewModel(root) { ActiveWorkingCopyRoot = root, ProjectName = "SampleModule" };
                workspace.ShowBuildCommand.Execute(null);
                Assert.False(workspace.ShowGenericProjectContext);
                await workspace.Build.PlanAsync();
                var result = Assert.Single(workspace.Build.Results);
                Assert.Equal(RepositoryPlanStatus.Succeeded, result.Status);
                Assert.Equal(config, result.PlanPath);
                Assert.Equal("powerforge.json", workspace.Build.ContractDisplay);
                Assert.False(workspace.Build.PlanningUsesScript);
                Assert.Contains("JSON inspection", workspace.Build.PlanningNotice, StringComparison.Ordinal);
                Assert.Contains(result.Actions, action => action.Action == "Stage to staging");
                Assert.Contains(result.Actions, action => action.Action == "Run action (Generate release metadata)");
                Assert.Contains(result.Actions, action => action.Action == "Pack Packed");
                Assert.DoesNotContain(result.Actions, action => action.Lane is "Release" or "Install" or "Sign");
                Assert.DoesNotContain("hidden-value", string.Join(" ", result.Actions.SelectMany(action => new[] { action.Action, action.Target, action.Detail })), StringComparison.Ordinal);
                Assert.False(workspace.Build.IsBusy);
                var window = new MainWindow { DataContext = workspace, Width = 1600, Height = 1000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var context = Assert.Single(window.GetVisualDescendants().OfType<Border>(), border => border.Name == "ContextPanel");
                    Assert.Contains(context.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Build context" && block.IsEffectivelyVisible);
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
                    var buildView = Assert.Single(window.GetVisualDescendants().OfType<BuildView>());
                    var pageScroll = buildView.GetVisualDescendants().OfType<ScrollViewer>().First();
                    pageScroll.Offset = new global::Avalonia.Vector(0, 360);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var actionsFrame = window.CaptureRenderedFrame();
                    Assert.NotNull(actionsFrame);
                    if (!string.IsNullOrEmpty(output)) actionsFrame.Save(Path.Combine(output, "build-plan-actions.png"), PngBitmapEncoderOptions.Default);
                    window.Width = 1050;
                    window.Height = 720;
                    pageScroll.Offset = default;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var compact = window.CaptureRenderedFrame();
                    Assert.NotNull(compact);
                    if (!string.IsNullOrEmpty(output)) compact.Save(Path.Combine(output, "build-plan-compact.png"), PngBitmapEncoderOptions.Default);
                    pageScroll.Offset = new global::Avalonia.Vector(0, 400);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var compactActions = window.CaptureRenderedFrame();
                    Assert.NotNull(compactActions);
                    if (!string.IsNullOrEmpty(output)) compactActions.Save(Path.Combine(output, "build-plan-actions-compact.png"), PngBitmapEncoderOptions.Default);
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
