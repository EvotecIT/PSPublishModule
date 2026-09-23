using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Net;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class ReleaseCatalogTests
{
    [Fact]
    public async Task ReleasesPageShowsAssetDownloadsAndDropsOldWorkingCopyResults()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-github-release-" + Guid.NewGuid().ToString("N"))).FullName;
        var first = Directory.CreateDirectory(Path.Combine(root, "First")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "Second")).FullName;
        File.WriteAllText(Path.Combine(first, ".git"), "gitdir: fixture");
        File.WriteAllText(Path.Combine(second, ".git"), "gitdir: fixture");
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var github = new FakeGitHub();
                using var release = new ReleaseViewModel(githubReleases: github);
                using var workspace = new WorkspaceViewModel(root, release: release);
                workspace.ShowReleaseCommand.Execute(null);
                release.SetHistoryScope(first);
                await release.RefreshGitHubReleasesAsync();
                Assert.Equal("First/v1", release.SelectedGitHubRelease?.TagName);
                Assert.Equal(32, release.SelectedGitHubRelease?.AssetDownloads);
                Assert.Contains("First", release.GithubReleaseStatus);

                var window = new MainWindow { DataContext = workspace, Width = 1600, Height = 1000 };
                window.Show();
                try
                {
                    Capture(window, "release-downloads-wide.png");
                    window.Width = 1050; window.Height = 720;
                    Capture(window, "release-downloads-compact.png");
                    window.GetVisualDescendants().OfType<ReleaseView>().Single()
                        .GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                    Capture(window, "release-downloads-compact-scrolled.png");
                }
                finally { window.Close(); }

                github.DelayNext = true;
                var stale = release.RefreshGitHubReleasesAsync();
                release.SetHistoryScope(second);
                Assert.Empty(release.GitHubReleases);
                await release.RefreshGitHubReleasesAsync();
                Assert.Equal("Second/v1", release.SelectedGitHubRelease?.TagName);
                github.CompleteDelayed();
                await stale;
                Assert.Equal("Second/v1", release.SelectedGitHubRelease?.TagName);
                github.ErrorNext = true;
                await release.RefreshGitHubReleasesAsync();
                Assert.Equal("Second/v1", release.SelectedGitHubRelease?.TagName);
                Assert.Contains("may be stale", release.GithubReleaseStatus);
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LocalProjectKeepsSavedReleaseHistoryWithoutOfferingGitHubMetrics()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-local-release-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await TestAppBuilder.RunAsync(async () =>
            {
                var github = new FakeGitHub();
                using var release = new ReleaseViewModel(githubReleases: github);
                using var workspace = new WorkspaceViewModel(root, release: release);
                workspace.ActiveWorkingCopyRoot = root;
                workspace.ShowReleaseCommand.Execute(null);
                Assert.False(release.CanRefreshGitHubReleases);
                Assert.True(release.CanBrowseHistory);
                Assert.Contains("local project folder", release.GithubReleaseStatus);
                await release.RefreshGitHubReleasesAsync();
                Assert.Empty(release.GitHubReleases);
                Assert.Equal(0, github.ResolveCalls);
                var window = new MainWindow { DataContext = workspace, Width = 1050, Height = 720 };
                window.Show();
                try
                {
                    window.UpdateLayout();
                    window.GetVisualDescendants().OfType<ReleaseView>().Single()
                        .GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
                    Capture(window, "local-release-github-unavailable.png");
                }
                finally { window.Close(); }
                return true;
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Capture(MainWindow window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("POWERFORGE_STUDIO_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class FakeGitHub : IGitHubReleaseCatalogService
    {
        private TaskCompletionSource<GitHubPage<GitHubReleaseMetric>>? _delayed;
        public int ResolveCalls { get; private set; }
        public bool DelayNext { get; set; }
        public bool ErrorNext { get; set; }
        public Task<string?> ResolveRepositoryAsync(string workingCopy, CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            return Task.FromResult<string?>("EvotecIT/" + Path.GetFileName(workingCopy));
        }
        public Task<GitHubPage<GitHubReleaseMetric>> FetchRecentReleasesAsync(string slug, CancellationToken cancellationToken = default)
        {
            if (ErrorNext)
            {
                ErrorNext = false;
                throw new GitHubAccessException(HttpStatusCode.Forbidden);
            }
            if (DelayNext)
            {
                DelayNext = false;
                _delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return _delayed.Task;
            }
            return Task.FromResult(Page(slug));
        }
        public void CompleteDelayed() => _delayed!.SetResult(Page("EvotecIT/First"));
        private static GitHubPage<GitHubReleaseMetric> Page(string slug) => new([
            new(1, slug.Split('/')[1] + "/v1", "Release 1", "https://github.com/" + slug + "/releases/tag/v1",
                DateTimeOffset.UtcNow, false, false,
                [new("app.zip", 25, 1024), new("symbols.zip", 7, 512)], true)
        ], false);
    }
}
