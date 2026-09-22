using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class BuildExecutionTests
{
    [Fact]
    public async Task JsonBuildProducesARealNuGetPackageAndRenderedReceipt()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-execution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        Directory.CreateDirectory(Path.Combine(root, "feed"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "StudioFixture.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework><Version>1.0.0</Version>
                  <PackageId>StudioFixture</PackageId><Authors>Validation</Authors>
                  <Description>Studio build validation fixture</Description>
                </PropertyGroup></Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "Fixture.cs"), "public sealed class Fixture { public int Value => 42; }");
            await File.WriteAllTextAsync(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            await File.WriteAllTextAsync(Path.Combine(root, "Build", "project.build.json"), """
                { "RootPath": "..", "ExpectedVersion": "1.0.0", "NugetSource": ["../feed"],
                  "OutputPath": "artifacts/packages", "CreateReleaseZip": false,
                  "SignAssemblies": false, "SignPackages": false, "PublishNuget": false, "PublishGitHub": false }
                """);
            await TestAppBuilder.RunAsync(async () =>
            {
                using var workspace = new WorkspaceViewModel(root) { ActiveWorkingCopyRoot = root, ProjectName = "StudioFixture" };
                workspace.ShowBuildCommand.Execute(null);
                await workspace.Build.PlanAsync();
                Assert.True(workspace.Build.CanBuild, workspace.Build.Status);
                await workspace.Build.BuildAsync();
                Assert.True(workspace.Build.BuildResult?.Succeeded, workspace.Build.BuildStatus + "\n" + workspace.Build.BuildOutput);
                var package = Assert.Single(workspace.Build.BuildResult!.AdapterResults.SelectMany(x => x.ArtifactFiles), x => x.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
                using (var archive = ZipFile.OpenRead(package))
                    Assert.Contains(archive.Entries, x => x.FullName == "lib/net10.0/StudioFixture.dll");
                var window = new MainWindow { DataContext = workspace, Width = 1600, Height = 1000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var view = Assert.Single(window.GetVisualDescendants().OfType<BuildView>());
                    view.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, "build-result.png"), PngBitmapEncoderOptions.Default);
                    }
                    window.Width = 1050;
                    window.Height = 720;
                    window.UpdateLayout();
                    var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
                    scroll.ScrollToEnd();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var compact = window.CaptureRenderedFrame();
                    Assert.NotNull(compact);
                    if (!string.IsNullOrEmpty(output)) compact.Save(Path.Combine(output, "build-result-compact.png"), PngBitmapEncoderOptions.Default);

                    workspace.ShowReleaseCommand.Execute(null);
                    Assert.True(workspace.Release.CanPrepare);
                    await workspace.Release.PrepareAsync();
                    Assert.True(workspace.Release.HasHandoff, workspace.Release.Status);
                    Assert.False(workspace.Release.ShowPublicationDetails);
                    Assert.Contains(workspace.Release.Artifacts, artifact => artifact.ArtifactPath == package);
                    Assert.Equal(root, workspace.Release.Handoff!.Session.Items.Single().RootPath);
                    Assert.False(workspace.IsFilesPage);
                    foreach (var small in new[] { false, true })
                    {
                        window.Width = small ? 1050 : 1600; window.Height = small ? 720 : 1000;
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var releaseFrame = window.CaptureRenderedFrame(); Assert.NotNull(releaseFrame);
                        if (!string.IsNullOrEmpty(output)) releaseFrame.Save(Path.Combine(output, small ? "release-prepare-compact.png" : "release-prepare.png"), PngBitmapEncoderOptions.Default);
                    }
                    File.Delete(package);
                    await workspace.Release.PrepareAsync();
                    Assert.False(workspace.Release.HasHandoff);
                    Assert.Equal("Preparation failed", workspace.Release.Stage);
                    workspace.ShowBuildCommand.Execute(null);

                    // A real compiler failure must return actionable diagnostics and re-enable the build action.
                    await File.WriteAllTextAsync(Path.Combine(root, "Fixture.cs"), "public class {");
                    await workspace.Build.BuildAsync();
                    Assert.NotNull(workspace.Build.BuildResult);
                    Assert.False(workspace.Build.BuildResult.Succeeded);
                    Assert.Equal("Build failed. Review diagnostics below; output from earlier runs may remain on disk.", workspace.Build.BuildStatus);
                    Assert.False(workspace.Release.HasHandoff);
                    Assert.False(workspace.Release.CanPrepare);
                    Assert.Contains(workspace.Build.BuildResult.AdapterResults, x => !string.IsNullOrWhiteSpace(x.ErrorTail));
                    Assert.All(workspace.Build.BuildResult.AdapterResults, adapter =>
                    {
                        Assert.Empty(adapter.ArtifactFiles);
                        Assert.Empty(adapter.ArtifactDirectories);
                    });
                    Assert.True(workspace.Build.CanBuild);
                    window.UpdateLayout();
                    scroll.ScrollToEnd();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var failure = window.CaptureRenderedFrame();
                    Assert.NotNull(failure);
                    if (!string.IsNullOrEmpty(output)) failure.Save(Path.Combine(output, "build-failure.png"), PngBitmapEncoderOptions.Default);
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            var plans = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerForgeStudio", "plans", Path.GetFileName(root));
            if (Directory.Exists(plans)) Directory.Delete(plans, recursive: true);
        }
    }

    [Fact]
    public async Task RunningBuildKeepsItsRootWhenBrowsingAnotherProjectAndCanBeCancelled()
    {
        var executor = new WaitingExecutor();
        using var model = new BuildViewModel(builds: executor) { HasSuccessfulInspection = true };
        model.SetWorkingCopy(Path.GetTempPath());
        model.HasSuccessfulInspection = true;
        var pending = model.BuildAsync();
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var root = model.BuildRoot;
        model.SetWorkingCopy(Path.Combine(Path.GetTempPath(), "other-project"));
        Assert.Equal(root, model.BuildRoot);
        Assert.False(executor.Token.IsCancellationRequested);
        Assert.False(model.CanBuild);
        model.CancelBuildCommand.Execute(null);
        await pending;
        Assert.True(executor.Token.IsCancellationRequested);
        Assert.Contains("cancelled", model.BuildStatus);
        Assert.False(model.IsBuilding);
    }

    [Fact]
    public async Task BuildViewModelShowsOutputWhileExecutorIsStillRunning()
    {
        await TestAppBuilder.RunAsync(async () =>
        {
            var executor = new ReportingExecutor();
            using var model = new BuildViewModel(builds: executor);
            model.SetWorkingCopy(Path.GetTempPath());
            model.HasSuccessfulInspection = true;
            var observed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var outputNotifications = 0;
            model.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName != nameof(BuildViewModel.BuildOutput)) return;
                outputNotifications++;
                if (model.BuildOutput.Contains("restoring packages", StringComparison.Ordinal))
                    observed.TrySetResult(model.BuildOutput);
            };

            var pending = model.BuildAsync();
            try
            {
                var output = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(pending.IsCompleted);
                Assert.Contains("ProjectBuild · Output · restoring packages", output, StringComparison.Ordinal);
                Assert.True(output.Length <= 128 * 1024, $"Build output grew to {output.Length} characters.");
                Assert.InRange(outputNotifications, 1, 25);
            }
            finally { executor.AllowCompletion.TrySetResult(); }
            await pending;
            return true;
        });
    }

    [Fact]
    public async Task CompletedBuildScrollsItsReceiptIntoView()
    {
        await TestAppBuilder.RunAsync(async () =>
        {
            using var model = new BuildViewModel(builds: new CompletedExecutor());
            model.SetWorkingCopy(Path.GetTempPath());
            model.HasSuccessfulInspection = true;
            var view = new BuildView { DataContext = model };
            var window = new Window { Content = view, Width = 900, Height = 360 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.Equal(0, scroll.Offset.Y);

                await model.BuildAsync();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.NotNull(model.BuildResult);
                Assert.True(scroll.Offset.Y > 0, "The completed build receipt remained below the visible viewport.");
                var heading = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Build result");
                var position = heading.TranslatePoint(default, scroll);
                Assert.NotNull(position);
                Assert.InRange(position.Value.Y, 0, scroll.Viewport.Height - heading.Bounds.Height);
            }
            finally { window.Close(); }
            return true;
        });
    }

    private sealed class CompletedExecutor : IReleaseBuildExecutionService
    {
        public Task<ReleaseBuildExecutionResult> ExecuteAsync(string rootPath, CancellationToken cancellationToken = default,
            IProgress<ReleaseBuildProgress>? progress = null)
            => Task.FromResult(new ReleaseBuildExecutionResult(rootPath, true, "Build completed.", 0, []));
    }

    private sealed class WaitingExecutor : IReleaseBuildExecutionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public async Task<ReleaseBuildExecutionResult> ExecuteAsync(string rootPath, CancellationToken cancellationToken = default, IProgress<ReleaseBuildProgress>? progress = null)
        {
            Token = cancellationToken;
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Expected cancellation.");
        }
    }

    private sealed class ReportingExecutor : IReleaseBuildExecutionService
    {
        public TaskCompletionSource AllowCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ReleaseBuildExecutionResult> ExecuteAsync(string rootPath, CancellationToken cancellationToken = default,
            IProgress<ReleaseBuildProgress>? progress = null)
        {
            for (var index = 0; index < 1_000; index++)
                progress?.Report(new ReleaseBuildProgress("ProjectBuild", "Output", new string('x', 256)));
            progress?.Report(new ReleaseBuildProgress("ProjectBuild", "Output", "restoring packages"));
            await AllowCompletion.Task.WaitAsync(cancellationToken);
            return new ReleaseBuildExecutionResult(rootPath, true, "Build completed.", 0, []);
        }
    }
}
