using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Domain.Projects;
using PowerForgeStudio.Orchestrator.Projects;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class ProjectOverviewTests
{
    [Fact]
    public async Task FailedOverviewInspectionDoesNotShowRecognizedSecretArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-overview-error-" + Guid.NewGuid().ToString("N"));
        var repository = new RepositoryCatalogEntry("Sample", root, ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository, null, null, false, false);
        using var model = new ProjectOverviewViewModel(new FailingOverviewService());
        model.SetProject(repository, root, Git("main", 0));

        await model.RefreshAsync();

        Assert.Equal("Could not inspect the selected project.", model.Status);
        Assert.Contains("<redacted>", model.Output);
        Assert.DoesNotContain("fixture-secret", model.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewGitSnapshotDoesNotCancelMetadataForTheSameWorkingCopy()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "studio-overview-race-" + Guid.NewGuid().ToString("N")));
        var repository = new RepositoryCatalogEntry("Sample", root, ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository, null, Path.Combine(root, "Build", "project.build.json"), false, false);
        var service = new ControlledOverviewService(root);
        using var model = new ProjectOverviewViewModel(service);
        model.SetProject(repository, root, Git("main", 0));
        var refresh = model.RefreshAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        model.SetProject(repository, root, Git("feature/new-head", 2));
        service.Complete();
        await refresh;

        Assert.Equal("Observed purpose", model.Purpose);
        Assert.Equal("feature/new-head", model.Branch);
        Assert.Equal("+2 / -0", model.AheadBehind);
    }

    [Fact]
    public async Task SelectingProjectOpensBoundedOverviewAndKeepsFilesAsSeparateRoute()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-overview-ui-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(fixture, "Workspace");
        var root = Path.Combine(workspace, "PowerForge.Sample");
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Overview fixture\n\nBuild and release this project through PowerForge.\n");
            await File.WriteAllTextAsync(Path.Combine(root, "Build", "project.build.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Build", "Build-Project.ps1"),
                "Set-Content -LiteralPath ../overview-should-not-run.txt -Value executed");
            await File.WriteAllTextAsync(Path.Combine(root, "Overview.slnx"), "<Solution />");
            Assert.True((await new GitClient().RunRawAsync(root, ["init", "-b", "main"])).Succeeded);

            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(workspace);
                await model.RefreshAsync();
                var project = Assert.Single(model.Projects);
                await project.EnsureLoadedAsync();
                await model.SelectAsync(project);

                Assert.True(model.IsOverviewPage);
                Assert.True(model.IsProjectRoute);
                Assert.False(model.IsFilesPage);
                Assert.Equal(Path.GetFileName(root), model.ProjectName);
                Assert.Contains("Build and release", model.Overview.Purpose, StringComparison.Ordinal);
                Assert.Contains(model.Overview.EntryPoints, item => item.Name == "Project build");
                Assert.Contains(model.Overview.EntryPoints, item => item.Name == ".NET solution");
                Assert.False(File.Exists(Path.Combine(root, "overview-should-not-run.txt")));

                Assert.Equal("4 untracked", model.Overview.GitState);
                await File.WriteAllTextAsync(Path.Combine(root, "external-change.txt"), "observed after overview refresh");
                await model.RefreshOverviewCommand.ExecuteAsync(null);
                Assert.Equal("5 untracked", model.Overview.GitState);

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    Capture(window, "project-overview.png");
                    window.Width = 1050;
                    window.Height = 720;
                    Capture(window, "project-overview-compact.png");
                    var page = window.FindControl<ProjectOverviewView>("ProjectOverviewPage");
                    var scroller = page?.FindControl<ScrollViewer>("PageScroll");
                    Assert.NotNull(scroller);
                    scroller.Offset = new global::Avalonia.Vector(0, scroller.Extent.Height);
                    Capture(window, "project-overview-compact-bottom.png");
                    model.ShowFilesCommand.Execute(null);
                    Assert.True(model.IsFilesPage);
                    Assert.False(model.IsOverviewPage);

                    var gitDirectory = Path.Combine(root, ".git");
                    Directory.Move(gitDirectory, gitDirectory + ".saved");
                    await File.WriteAllTextAsync(gitDirectory, "gitdir: Z:/missing/studio-overview-git");
                    await model.RefreshOverviewCommand.ExecuteAsync(null);
                    Assert.Equal("Could not refresh Git state for this working copy.", model.Overview.Status);
                    Assert.DoesNotContain("Z:/missing", model.Overview.Output, StringComparison.OrdinalIgnoreCase);
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }

    private static void Capture(MainWindow window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
    }

    private static ProjectGitStatus Git(string branch, int ahead) => new(true, branch, "origin/main", ahead, 0,
        0, 0, 0, [], [], [], [branch, "main"], [new(Path.GetTempPath(), branch, false, false)]);

    private sealed class ControlledOverviewService(string root) : IProjectOverviewService
    {
        private readonly TaskCompletionSource _complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProjectOverviewSnapshot> InspectAsync(RepositoryCatalogEntry repository, string workingCopyRoot,
            ProjectGitStatus git, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await _complete.Task.WaitAsync(cancellationToken);
            return new ProjectOverviewSnapshot(DateTimeOffset.UtcNow, repository.Name, "Library", "Primary repository",
                root, root, "Observed purpose", null, git.BranchDisplay, git.StatusSummary, git.AheadBehindDisplay, 1,
                [new(".NET / package", "Detected")], [new("Project build", "JSON", Path.Combine(root, "Build", "project.build.json"))],
                [new("PowerForge", "Required")], []);
        }

        public void Complete() => _complete.TrySetResult();
    }

    private sealed class FailingOverviewService : IProjectOverviewService
    {
        public Task<ProjectOverviewSnapshot> InspectAsync(RepositoryCatalogEntry repository, string workingCopyRoot,
            ProjectGitStatus git, CancellationToken cancellationToken = default)
            => Task.FromException<ProjectOverviewSnapshot>(new InvalidOperationException("Inspection failed with --Token=fixture-secret"));
    }
}
