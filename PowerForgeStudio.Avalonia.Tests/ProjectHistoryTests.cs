using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForge;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class ProjectHistoryTests
{
    [Fact]
    public async Task SwitchingWorkingCopiesCancelsOldHistoryAndKeepsNewEvidence()
    {
        var first = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "history-first-" + Guid.NewGuid().ToString("N")));
        var second = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "history-second-" + Guid.NewGuid().ToString("N")));
        var service = new DelayedHistoryService(first, second);
        using var model = new ProjectHistoryViewModel(service);

        model.SetWorkingCopy(first);
        var firstRefresh = model.RefreshAsync();
        await service.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        model.SetWorkingCopy(second);
        Assert.False(model.IsLoading);
        await model.RefreshAsync();
        await service.FirstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await firstRefresh;

        Assert.Equal(second, model.Root);
        Assert.Equal("second", Assert.Single(model.Commits).Message);
        Assert.Equal("feature/history", model.Branch);
    }

    [Fact]
    public async Task SwitchingToAnUnbornRepositoryClearsAnActiveDetailProgressState()
    {
        var first = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "history-detail-first-" + Guid.NewGuid().ToString("N")));
        var second = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "history-detail-second-" + Guid.NewGuid().ToString("N")));
        var service = new DelayedDetailHistoryService(first, second);
        using var model = new ProjectHistoryViewModel(service);

        model.SetWorkingCopy(first);
        await model.RefreshAsync();
        await service.DetailStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsDetailLoading);

        model.SetWorkingCopy(second);
        Assert.False(model.IsDetailLoading);
        await model.RefreshAsync();
        await service.DetailCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(model.Commits);
        Assert.Null(model.SelectedCommit);
        Assert.False(model.IsLoading);
        Assert.False(model.IsDetailLoading);
    }

    [Fact]
    public async Task HistoryRouteShowsCommitsChangedFilesAndDiff()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "studio-history-ui-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(fixture, "Workspace");
        var root = Path.Combine(workspace, "History.Sample");
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        var git = new GitClient();
        try
        {
            async Task Run(params string[] arguments)
            {
                var result = await git.RunRawAsync(root, arguments);
                Assert.True(result.Succeeded, result.StdErr);
            }
            await Run("init", "-b", "main");
            await Run("config", "user.name", "Studio validation");
            await Run("config", "user.email", "studio-validation@example.invalid");
            await Run("config", "commit.gpgSign", "false");
            await Run("config", "core.hooksPath", Path.Combine(root, "empty-hooks"));
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# History fixture\n");
            await File.WriteAllTextAsync(Path.Combine(root, "Build", "project.build.json"), "{}");
            await Run("add", ".");
            await Run("commit", "-m", "Initial project");
            Directory.CreateDirectory(Path.Combine(root, "src"));
            await File.WriteAllTextAsync(Path.Combine(root, "src", "feature.txt"), "history detail\n");
            await Run("add", "src/feature.txt");
            await Run("commit", "-m", "Add history detail");

            await TestAppBuilder.RunAsync(async () =>
            {
                using var model = new WorkspaceViewModel(workspace);
                await model.RefreshAsync();
                var project = Assert.Single(model.Projects);
                await project.EnsureLoadedAsync();
                await model.SelectAsync(project);
                await model.ShowHistoryCommand.ExecuteAsync(null);
                for (var attempt = 0; attempt < 100 && model.History.IsDetailLoading; attempt++) await Task.Delay(20);

                Assert.True(model.IsHistoryPage);
                Assert.True(model.IsProjectRoute);
                Assert.Equal(2, model.History.Commits.Count);
                Assert.Equal("Add history detail", model.History.SelectedCommit?.Message);
                Assert.Contains("src/feature.txt", model.History.ChangedFiles);
                Assert.Contains("+history detail", model.History.Diff, StringComparison.Ordinal);

                var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    Capture(window, "project-history.png");
                    window.Width = 1050;
                    window.Height = 720;
                    Capture(window, "project-history-compact.png");
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally
        {
            if (Directory.Exists(fixture))
            {
                foreach (var file in new DirectoryInfo(fixture).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                    if (file.IsReadOnly) file.IsReadOnly = false;
                Directory.Delete(fixture, recursive: true);
            }
        }
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

    private sealed class DelayedHistoryService(string first, string second) : IProjectHistoryService
    {
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProjectHistoryContext> GetHistoryContextAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            if (repositoryRoot == first)
            {
                FirstStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { FirstCancelled.TrySetResult(); throw; }
            }
            Assert.Equal(second, repositoryRoot);
            return new ProjectHistoryContext(true, "feature/history");
        }

        public Task<IReadOnlyList<GitLogEntry>> GetHistoryLogAsync(string repositoryRoot, int count = 15, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GitLogEntry>>([new(new string('a', 40), "aaaaaaa", "Studio", "second", DateTimeOffset.UtcNow)]);

        public Task<GitCommitDetail> GetCommitDetailAsync(string repositoryRoot, string commitHash, CancellationToken cancellationToken = default)
            => Task.FromResult(new GitCommitDetail(commitHash, ["file.txt"], "diff", false, false));
    }


    private sealed class DelayedDetailHistoryService(string first, string second) : IProjectHistoryService
    {
        public TaskCompletionSource DetailStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DetailCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProjectHistoryContext> GetHistoryContextAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            Assert.True(repositoryRoot == first || repositoryRoot == second);
            return Task.FromResult(new ProjectHistoryContext(true, "main"));
        }

        public Task<IReadOnlyList<GitLogEntry>> GetHistoryLogAsync(string repositoryRoot, int count = 15, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GitLogEntry>>(repositoryRoot == first
                ? [new(new string('b', 40), "bbbbbbb", "Studio", "detail", DateTimeOffset.UtcNow)]
                : []);

        public async Task<GitCommitDetail> GetCommitDetailAsync(string repositoryRoot, string commitHash, CancellationToken cancellationToken = default)
        {
            Assert.Equal(first, repositoryRoot);
            DetailStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The delayed detail should have been cancelled.");
            }
            catch (OperationCanceledException)
            {
                DetailCancelled.TrySetResult();
                throw;
            }
        }
    }
}
