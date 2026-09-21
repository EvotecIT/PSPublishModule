using System.Net;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.Tests;

public sealed class GitHubWorkspaceTests
{
    [Fact]
    public async Task ProjectSwitchAndFilterRejectLateListsAndDiscussions()
    {
        await TestAppBuilder.RunAsync(async () =>
        {
            var service = new FakeGitHub();
            using var model = new GitHubViewModel(service);
            model.SetWorkingCopy("first");
            service.PendingList = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var oldRead = model.RefreshAsync();
            model.SetWorkingCopy("second");
            var pending = service.PendingList; service.PendingList = null;
            await model.RefreshAsync();
            pending.SetResult(new([Pull(7, "Wrong project")], false));
            await oldRead;
            Assert.Equal("owner/second", model.Slug);
            Assert.DoesNotContain(model.Items, item => item.Title == "Wrong project");
            service.PendingDetail = new(TaskCreationOptions.RunContinuationsAsynchronously);
            model.Selected = model.Items[0]; var oldDetail = model.SelectionLoad;
            model.ShowPullRequests = false;
            await model.RefreshAsync();
            service.PendingDetail.SetResult(new(Pull(7, "Old detail"), [], []));
            await oldDetail;
            Assert.Empty(model.Discussion); Assert.Empty(model.Checks); Assert.Equal("Discussion", model.DetailTitle);
            model.Selected = Assert.Single(model.Items);
            await model.SelectionLoad;
            Assert.Contains(model.Discussion, entry => entry.Markdown == "Issue description");
            Assert.False(model.HasChecks);
            service.Error = true;
            await model.RefreshAsync();
            Assert.Empty(model.Items); Assert.Contains("denied", model.Status);
            return true;
        });
    }

    [Fact]
    public async Task ShellRendersDiscussionAndFailedChecksAtWideAndCompactSizes()
    {
        await TestAppBuilder.RunAsync(async () =>
        {
            var service = new FakeGitHub();
            using var workspace = new WorkspaceViewModel(Path.GetTempPath(), gitHub: service);
            workspace.ProjectName = "PowerForge";
            workspace.ActiveWorkingCopyRoot = Path.GetTempPath();
            workspace.ShowGitHubCommand.Execute(null);
            await workspace.GitHub.RefreshAsync();
            workspace.GitHub.Selected = workspace.GitHub.Items[0];
            await workspace.GitHub.SelectionLoad;
            Assert.False(workspace.IsFilesPage); Assert.True(workspace.IsGitHubPage);
            Assert.Equal(40, workspace.GitHub.HeadSha.Length);
            Assert.True(Assert.Single(workspace.GitHub.Checks).IsFailure);
            Assert.Contains("1 need attention", workspace.GitHub.ChecksStatus);
            var window = new MainWindow { DataContext = workspace, Width = 1600, Height = 1000 };
            window.Show();
            try
            {
                foreach (var compact in new[] { false, true })
                {
                    window.Width = compact ? 1050 : 1600; window.Height = compact ? 700 : 1000;
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("STUDIO_SCREENSHOT_DIR");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, compact ? "workspace-github-compact.png" : "workspace-github.png"), PngBitmapEncoderOptions.Default);
                    }
                }
                await workspace.GitHub.ReviewFilesAsync();
                Assert.True(workspace.GitHub.IsFilesReview);
                Assert.Equal(2, workspace.GitHub.ChangedFiles.Count);
                Assert.Contains("Release", workspace.GitHub.PatchPreview);
                foreach (var compact in new[] { false, true })
                {
                    window.Width = compact ? 1050 : 1600; window.Height = compact ? 700 : 1000;
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("STUDIO_SCREENSHOT_DIR");
                    if (!string.IsNullOrWhiteSpace(output)) frame.Save(Path.Combine(output, compact ? "workspace-pr-files-compact.png" : "workspace-pr-files.png"), PngBitmapEncoderOptions.Default);
                }
                workspace.GitHub.SelectedChangedFile = workspace.GitHub.ChangedFiles[1];
                Assert.Contains("binary", workspace.GitHub.PatchNotice);
                workspace.GitHub.ShowDiscussionCommand.Execute(null);
                Assert.False(workspace.GitHub.IsFilesReview);
                service.CheckError = true;
                await workspace.GitHub.LoadSelectionAsync();
                Assert.NotEmpty(workspace.GitHub.Discussion);
                Assert.Empty(workspace.GitHub.Checks);
                Assert.Contains("denied", workspace.GitHub.ChecksStatus);
                workspace.ShowFilesCommand.Execute(null);
                Assert.True(workspace.IsFilesPage); Assert.False(workspace.IsGitHubPage);
            }
            finally { window.Close(); }
            return true;
        });
    }

    [Fact]
    public async Task LateFilesCannotReopenReviewAfterNavigationOrReplaceAnotherProject()
    {
        await TestAppBuilder.RunAsync(async () =>
        {
            var service = new FakeGitHub();
            using var model = new GitHubViewModel(service);
            model.SetWorkingCopy("first"); await model.RefreshAsync();
            model.Selected = model.Items[0]; await model.SelectionLoad;
            service.PendingFiles = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading = model.ReviewFilesAsync();
            Assert.True(model.IsFilesLoading);
            model.ShowDiscussionCommand.Execute(null);
            service.PendingFiles.SetResult(new(new string('a', 40), new string('b', 40), new([], false)));
            await loading;
            Assert.False(model.IsFilesReview); Assert.Empty(model.ChangedFiles);
            service.PendingFiles = new(TaskCreationOptions.RunContinuationsAsynchronously);
            loading = model.ReviewFilesAsync();
            model.SetWorkingCopy("second");
            service.PendingFiles.SetResult(new(new string('a', 40), new string('b', 40), new([new("wrong.cs", null, "added", 1, 0, "+wrong", false)], false)));
            await loading;
            Assert.Empty(model.ChangedFiles); Assert.False(model.IsFilesReview); Assert.Equal("", model.FilesRevision);
            return true;
        });
    }

    private static GitHubPullRequest Pull(int number = 42, string title = "Preserve build configuration across working copies") => new(number, title, "open", "maintainer",
        "feature/build-context", "main", GitHubPrReviewStatus.Unknown, GitHubPrMergeStatus.Unknown, [], 12, 3, 2, DateTimeOffset.UtcNow, null,
        BodyMarkdown: "Build plans must follow the selected working copy. Keep the captured configuration stable during execution.", HeadSha: new string('a', 40));

    private sealed class FakeGitHub : IGitHubProjectService
    {
        public Task<GitHubPullRequest?> FindMergedPullRequestByHeadAsync(string slug, string headSha, string expectedBaseBranch, CancellationToken cancellationToken = default)
            => Task.FromResult<GitHubPullRequest?>(null);
        public TaskCompletionSource<GitHubPage<GitHubPullRequest>>? PendingList;
        public TaskCompletionSource<GitHubPullRequestDetail?>? PendingDetail;
        public bool Error, CheckError;
        public TaskCompletionSource<GitHubPullRequestFiles>? PendingFiles;
        public Task<GitHubPullRequestFiles> FetchPullRequestFilesAsync(string slug, int number, string expectedHeadSha, CancellationToken cancellationToken = default)
            => PendingFiles?.Task ?? Task.FromResult(new GitHubPullRequestFiles(expectedHeadSha, new string('b', 40), new([
                new("Build/project.build.json", null, "modified", 2, 1, "@@ -1,3 +1,4 @@\n {\n-  \"configuration\": \"Debug\"\n+  \"configuration\": \"Release\",\n+  \"sign\": true\n }", false),
                new("Assets/icon.png", null, "modified", 0, 0, null, false)], false)));
        public Task<string?> ResolveRepositoryAsync(string root, CancellationToken cancellationToken = default) => Task.FromResult<string?>("owner/" + (root == "first" ? "first" : "second"));
        public Task<GitHubPage<GitHubPullRequest>> FetchPullRequestsAsync(string slug, string state = "open", CancellationToken cancellationToken = default)
        {
            if (Error) throw new GitHubAccessException(HttpStatusCode.Forbidden);
            return PendingList?.Task ?? Task.FromResult(new GitHubPage<GitHubPullRequest>([Pull()], false));
        }
        public Task<GitHubPage<GitHubIssue>> FetchIssuesAsync(string slug, string state = "open", CancellationToken cancellationToken = default)
        {
            if (Error) throw new GitHubAccessException(HttpStatusCode.Forbidden);
            return Task.FromResult(new GitHubPage<GitHubIssue>([Issue()], false));
        }
        private static GitHubIssue Issue() => new(17, "Keep project context after refresh", "open", "reporter", [], [], DateTimeOffset.UtcNow, null, BodyMarkdown: "Issue description");
        public Task<GitHubIssueDetail?> FetchIssueDetailAsync(string slug, int number, CancellationToken cancellationToken = default)
            => Task.FromResult<GitHubIssueDetail?>(new(Issue(), [], []));
        public Task<GitHubPullRequestDetail?> FetchPullRequestDetailAsync(string slug, int number, CancellationToken cancellationToken = default)
            => PendingDetail?.Task ?? Task.FromResult<GitHubPullRequestDetail?>(new(Pull(), [new(1, GitHubDiscussionCommentKind.IssueComment, "reviewer", "The selected configuration is now captured before launch. Please verify the cancellation path too.", DateTimeOffset.UtcNow)], []));
        public Task<GitHubPage<GitHubCheck>> FetchChecksAsync(string slug, string headSha, CancellationToken cancellationToken = default)
        {
            if (CheckError) throw new GitHubAccessException(HttpStatusCode.Forbidden);
            return Task.FromResult(new GitHubPage<GitHubCheck>([new("Windows · build and tests", "failure", "check run", null)], false));
        }
    }
}
