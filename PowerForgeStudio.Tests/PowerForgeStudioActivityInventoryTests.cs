using System.Net;
using System.Text;
using PowerForge;
using PowerForgeStudio.Domain.Activity;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Activity;
using PowerForgeStudio.Orchestrator.Automation;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Git;
using PowerForgeStudio.Orchestrator.Hub;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioActivityInventoryTests
{
    [Fact]
    public async Task InspectAsync_ProbesFavoriteBeforeAlphabeticallyEarlierRepository()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-activity-priority-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            foreach (var name in new[] { "Alpha", "Beta" })
            {
                var repository = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
                if (name == "Alpha")
                {
                    Directory.CreateDirectory(Path.Combine(repository, "Build"));
                    await File.WriteAllTextAsync(Path.Combine(repository, "Build", "Build-Project.ps1"), "# Inventory must not run this script");
                }
                Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);
            }

            var explorer = new WorkspaceRootCatalogService(Path.Combine(root, "workspace-roots.json"));
            explorer.SetFavorite(root, Path.Combine(root, "Beta"), true);
            var requests = new List<string>();
            using var http = new HttpClient(new StubHttpMessageHandler(request =>
            {
                var path = request.RequestUri?.PathAndQuery ?? "";
                requests.Add(path);
                if (path == "/repos/EvotecIT/Beta") return Json("""{ "default_branch": "main" }""");
                return CreateGitHubResponse(request);
            })) { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new NamedGitRemoteResolver()),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(),
                new ReleaseHistoryService(Path.Combine(root, "history.db")), explorerState: explorer);

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(1, 1, 50));

            Assert.NotEmpty(requests);
            Assert.All(requests, path => Assert.Contains("/repos/EvotecIT/Beta", path, StringComparison.Ordinal));
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "Issue" && entry.Project == "Beta");
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "Pull request" && entry.Project == "Beta");
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "CI" && entry.Project == "Beta");
            Assert.Equal(2, snapshot.RepositoryCount);
            Assert.Contains("1 release-managed", Assert.Single(snapshot.Sources, source => source.Provider == "Local workspace").Message);
            Assert.DoesNotContain(snapshot.Entries, entry => entry.Project == "Beta" && entry.Provider == "PowerForge portfolio");
            Assert.Equal("Partial", Assert.Single(snapshot.Sources, source => source.Provider == "GitHub").State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InspectAsync_NonGitFolderDoesNotConsumeGitHubProbeSlot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-activity-probe-slot-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var nonGit = Directory.CreateDirectory(Path.Combine(root, "AlphaFolder")).FullName;
            await File.WriteAllTextAsync(Path.Combine(nonGit, "README.md"), "ordinary folder");
            var repository = Directory.CreateDirectory(Path.Combine(root, "Beta")).FullName;
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);
            var requests = new List<string>();
            using var http = new HttpClient(new StubHttpMessageHandler(request =>
            {
                var path = request.RequestUri?.PathAndQuery ?? "";
                requests.Add(path);
                if (path == "/repos/EvotecIT/Beta") return Json("""{ "default_branch": "main" }""");
                return CreateGitHubResponse(request);
            })) { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new NamedGitRemoteResolver()),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(),
                new ReleaseHistoryService(Path.Combine(root, "history.db")));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(1, 1, 50));

            Assert.Equal(1, snapshot.RepositoryCount);
            Assert.NotEmpty(requests);
            Assert.All(requests, path => Assert.Contains("/repos/EvotecIT/Beta", path, StringComparison.Ordinal));
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "Issue" && entry.Project == "Beta");
            Assert.Equal("Available", Assert.Single(snapshot.Sources, source => source.Provider == "GitHub").State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InspectAsync_LocalBuildProjectDoesNotRaiseGitGuardWarning()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-activity-local-build-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root, "LocalBuild")).FullName;
            var build = Directory.CreateDirectory(Path.Combine(project, "Build")).FullName;
            await File.WriteAllTextAsync(Path.Combine(build, "project.build.json"), "{}");
            using var http = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP expected.")))
                { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new StubGitRemoteResolver(null)),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(),
                new ReleaseHistoryService(Path.Combine(root, "history.db")));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(0, 0, 50));
            var portfolio = Assert.Single(await new RepositoryPortfolioService().BuildPortfolioAsync(
                await new WorkspaceRepositorySource().DiscoverAsync(root)));

            Assert.Equal(1, snapshot.RepositoryCount);
            Assert.Equal(RepositoryReadinessKind.Ready, portfolio.ReadinessKind);
            Assert.Empty(portfolio.Git.GitDiagnostics);
            Assert.DoesNotContain(snapshot.Entries, entry => entry.Project == "LocalBuild" && entry.Provider == "PowerForge portfolio");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InspectAsync_OrdinaryRepositoryWithoutGitHubOriginIsAbsentNotDeferred()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-activity-ordinary-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "OrdinaryProject")).FullName;
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);
            using var http = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP expected.")))
                { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new StubGitRemoteResolver(null)),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(),
                new ReleaseHistoryService(Path.Combine(root, "history.db")));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(1, 1, 50));

            Assert.Equal(1, snapshot.RepositoryCount);
            Assert.DoesNotContain(snapshot.Entries, entry => entry.Provider == "PowerForge portfolio");
            Assert.Equal("Absent", Assert.Single(snapshot.Sources, source => source.Provider == "GitHub").State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InspectAsync_TracksSavedReleaseStatesInsideSelectedWorkspace()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-activity-journal-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "SampleProject")).FullName;
            Directory.CreateDirectory(Path.Combine(repository, "Build"));
            await File.WriteAllTextAsync(Path.Combine(repository, "Build", "Build-Project.ps1"), "throw 'must not run'");
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);
            var databasePath = Path.Combine(root, "history.db");
            var database = new ReleaseStateDatabase(databasePath);
            await database.InitializeAsync();
            var now = DateTimeOffset.UtcNow;
            await database.PersistQueueSessionAsync(ReleaseQueueSessionFactory.Create(root,
                [QueueItem(repository, "SampleProject", ReleaseQueueStage.Completed, ReleaseQueueItemStatus.Succeeded, now.AddMinutes(-2))], now.AddMinutes(-2)));
            await database.PersistQueueSessionAsync(ReleaseQueueSessionFactory.Create(root,
                [QueueItem(repository, "SampleProject", ReleaseQueueStage.Publish, ReleaseQueueItemStatus.Failed, now.AddMinutes(-1))], now.AddMinutes(-1)));
            var outside = root + "-outside";
            await database.PersistQueueSessionAsync(ReleaseQueueSessionFactory.Create(outside,
                [QueueItem(outside, "Outside", ReleaseQueueStage.Completed, ReleaseQueueItemStatus.Succeeded, now)], now));

            using var http = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP expected.")))
                { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new StubGitRemoteResolver(null)),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(), new ReleaseHistoryService(databasePath));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(0, 0, 100));
            var journal = snapshot.Entries.Where(entry => entry.Provider == "Release journal").ToArray();
            Assert.Equal(2, journal.Length);
            Assert.Contains(journal, entry => entry.State == "Failed" && entry.IsActionable && entry.Severity == "Critical");
            Assert.Contains(journal, entry => entry.State == "Succeeded" && !entry.IsActionable && entry.Severity == "Information");
            Assert.All(journal, entry => Assert.Equal(repository, entry.Source));
            Assert.Equal("Available", Assert.Single(snapshot.Sources, source => source.Provider == "Local workspace").State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ReleaseQueueItem QueueItem(string root, string name, ReleaseQueueStage stage,
        ReleaseQueueItemStatus status, DateTimeOffset observed)
        => new(root, name, default, default, 1, stage, status,
            status == ReleaseQueueItemStatus.Failed ? "Publication failed." : "Release completed.",
            "release.test", "{}", observed);

    [Fact]
    public async Task InspectAsync_UnreadableJournalKeepsWorkspaceSignalsAndMarksPartialEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-activity-journal-error-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "SampleProject")).FullName;
            Directory.CreateDirectory(Path.Combine(repository, "Build"));
            await File.WriteAllTextAsync(Path.Combine(repository, "Build", "Build-Project.ps1"), "throw 'must not run'");
            await File.WriteAllTextAsync(Path.Combine(root, "history.db"), "invalid database");
            using var http = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP expected.")))
                { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new StubGitRemoteResolver(null)),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(),
                new ReleaseHistoryService(Path.Combine(root, "history.db")));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(0, 0, 100));
            Assert.Equal("Partial", Assert.Single(snapshot.Sources, source => source.Provider == "Local workspace").State);
            Assert.DoesNotContain(snapshot.Entries, entry => entry.Provider == "Release journal");
            Assert.Equal(1, snapshot.RepositoryCount);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InspectAsync_ProjectsOwnerEvidenceWithoutRunningBuildScript()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "SampleProject")).FullName;
            var build = Directory.CreateDirectory(Path.Combine(repository, "Build")).FullName;
            var marker = Path.Combine(repository, "build-ran.txt");
            await File.WriteAllTextAsync(Path.Combine(build, "Build-Project.ps1"), $"Set-Content -LiteralPath '{marker.Replace("'", "''")}' -Value ran");
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);

            using var http = new HttpClient(new StubHttpMessageHandler(CreateGitHubResponse)) { BaseAddress = new Uri("https://api.github.com") };
            var inbox = new GitHubInboxService(http, new StubGitRemoteResolver("https://github.com/EvotecIT/SampleProject.git"));
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(), inbox,
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(), new ReleaseHistoryService(Path.Combine(root, "history.db")));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(4, 3, 50));

            Assert.Equal(1, snapshot.RepositoryCount);
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "CI" && entry.Severity == "Critical");
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "Pull request");
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "Issue" && entry.Title.Contains("#42", StringComparison.Ordinal));
            Assert.Contains(snapshot.Entries, entry => entry.Kind == "Schedule" && entry.State == "Failed");
            var gitHubSource = Assert.Single(snapshot.Sources, source => source.Provider == "GitHub");
            Assert.Equal("Available", gitHubSource.State);
            Assert.Equal(snapshot.Entries.Count(entry => entry.Provider == "GitHub"), gitHubSource.EntryCount);
            Assert.False(File.Exists(marker));

            var limited = await service.InspectAsync(root, new WorkspaceActivityOptions(4, 3, 2));
            Assert.True(limited.IsTruncated);
            Assert.Equal(2, limited.Entries.Count);
            Assert.True(limited.TotalEntryCount > limited.Entries.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_AutomationFailureRemainsVisibleWithoutDiscardingLocalEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-failure-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "SampleProject")).FullName;
            Directory.CreateDirectory(Path.Combine(repository, "Build"));
            await File.WriteAllTextAsync(Path.Combine(repository, "Build", "Build-Project.ps1"), "throw 'must not run'");
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);

            using var http = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP expected."))) { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new StubGitRemoteResolver(null)),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FailingAutomationInventory(), new FakeGitHubProjectService(), new ReleaseHistoryService(Path.Combine(root, "history.db")));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(0, 0, 50));

            Assert.Contains(snapshot.Sources, source => source.Provider == "Local workspace" && source.State == "Available");
            Assert.Contains(snapshot.Sources, source => source.Provider == "Automations" && source.State == "Unavailable");
            Assert.NotEmpty(snapshot.Entries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_GitHubDeadlineRetainsLocalEvidenceAndReportsUnavailableSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-timeout-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "SampleProject")).FullName;
            Directory.CreateDirectory(Path.Combine(repository, "Build"));
            await File.WriteAllTextAsync(Path.Combine(repository, "Build", "Build-Project.ps1"), "throw 'must not run'");
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);

            using var http = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP expected."))) { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new BlockingGitRemoteResolver()),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(), new ReleaseHistoryService(Path.Combine(root, "history.db")));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(1, 1, 50, 1));

            Assert.Equal(1, snapshot.RepositoryCount);
            Assert.Contains(snapshot.Sources, source => source.Provider == "Local workspace" && source.State == "Available");
            Assert.Contains(snapshot.Sources, source => source.Provider == "GitHub" && source.State == "Unavailable"
                && source.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(snapshot.Entries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_GitHubDeadlineBoundsSelectedOrdinaryGitStatus()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-activity-git-timeout-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "OrdinaryProject")).FullName;
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);
            var portfolio = new RepositoryPortfolioService(
                new GitRepositoryInspector(new GitClient(new BlockingProcessRunner())),
                new RepositoryGitPreflightService());
            using var http = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP expected.")))
                { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), portfolio,
                new GitHubInboxService(http, new StubGitRemoteResolver(null)),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(),
                new ReleaseHistoryService(Path.Combine(root, "history.db")));
            using var userDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(1, 1, 50, 1),
                userDeadline.Token);

            Assert.Equal(1, snapshot.RepositoryCount);
            Assert.Contains(snapshot.Sources, source => source.Provider == "GitHub" && source.State == "Unavailable"
                && source.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Access denied")]
    [InlineData(HttpStatusCode.TooManyRequests, "Rate limited")]
    public async Task InspectAsync_GitHubAccessStatesRemainDistinct(HttpStatusCode statusCode, string expectedState)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-activity-access-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(root, "SampleProject")).FullName;
            Directory.CreateDirectory(Path.Combine(repository, "Build"));
            await File.WriteAllTextAsync(Path.Combine(repository, "Build", "Build-Project.ps1"), "throw 'must not run'");
            Assert.True((await new GitClient().RunRawAsync(repository, ["init", "-b", "main"])).Succeeded);

            using var http = new HttpClient(new StubHttpMessageHandler(CreateGitHubResponse)) { BaseAddress = new Uri("https://api.github.com") };
            using var service = new WorkspaceActivityInventoryService(
                new RepositoryCatalogScanner(), new RepositoryPortfolioService(),
                new GitHubInboxService(http, new StubGitRemoteResolver("https://github.com/EvotecIT/SampleProject.git")),
                new RepositoryReleaseDriftService(), new RepositoryReleaseInboxService(),
                new FakeAutomationInventory(), new FakeGitHubProjectService(statusCode), new ReleaseHistoryService(Path.Combine(root, "history.db")));

            var snapshot = await service.InspectAsync(root, new WorkspaceActivityOptions(1, 1, 50, 5));

            Assert.Equal(expectedState, Assert.Single(snapshot.Sources, source => source.Provider == "GitHub").State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpResponseMessage CreateGitHubResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri?.PathAndQuery ?? "";
        if (path == "/repos/EvotecIT/SampleProject") return Json("""{ "default_branch": "main" }""");
        if (path.Contains("/pulls?", StringComparison.OrdinalIgnoreCase)) return Json("""[{ "number": 7 }]""");
        if (path.Contains("/releases/latest", StringComparison.OrdinalIgnoreCase)) return Json("""{ "tag_name": "v1.0.0" }""");
        if (path.Contains("/actions/runs?", StringComparison.OrdinalIgnoreCase)) return Json("""{ "workflow_runs": [ { "conclusion": "failure" } ] }""");
        if (path.Contains("/branches/main", StringComparison.OrdinalIgnoreCase)) return Json("""{ "protected": true }""");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }

    private sealed class StubGitRemoteResolver(string? origin) : IGitRemoteResolver
    {
        public Task<string?> ResolveOriginUrlAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(origin);
    }

    private sealed class NamedGitRemoteResolver : IGitRemoteResolver
    {
        public Task<string?> ResolveOriginUrlAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>($"https://github.com/EvotecIT/{Path.GetFileName(repositoryRoot)}.git");
    }

    private sealed class BlockingGitRemoteResolver : IGitRemoteResolver
    {
        public async Task<string?> ResolveOriginUrlAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class BlockingProcessRunner : IProcessRunner
    {
        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Git status must have been canceled.");
        }
    }

    private sealed class FakeAutomationInventory : IWorkspaceAutomationInventoryService
    {
        public Task<WorkspaceAutomationSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            WorkspaceAutomationEntry entry = new("nightly", "Nightly release", "Windows Task Scheduler", "Local", "SampleProject",
                "Daily", "Failed", now.AddHours(1), now.AddMinutes(-10), "Exit 1", true, true, true, "Task Scheduler", "Provider runtime evidence.");
            return Task.FromResult(new WorkspaceAutomationSnapshot(now, [entry],
                [new WorkspaceAutomationSourceState("Windows Task Scheduler", "Available", 1, "Runtime evidence read locally.")]));
        }
    }

    private sealed class FailingAutomationInventory : IWorkspaceAutomationInventoryService
    {
        public Task<WorkspaceAutomationSnapshot> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("provider failed");
    }

    private sealed class FakeGitHubProjectService(HttpStatusCode? failure = null) : IGitHubProjectService
    {
        public Task<GitHubPullRequest?> FindMergedPullRequestByHeadAsync(string slug, string headSha, string expectedBaseBranch, CancellationToken cancellationToken = default)
            => Task.FromResult<GitHubPullRequest?>(null);
        public Task<string?> ResolveRepositoryAsync(string workingCopy, CancellationToken cancellationToken = default) => Task.FromResult<string?>("EvotecIT/SampleProject");
        public Task<GitHubPage<GitHubIssue>> FetchIssuesAsync(string slug, string state = "open", CancellationToken cancellationToken = default)
            => failure is { } status
                ? Task.FromException<GitHubPage<GitHubIssue>>(new GitHubAccessException(status))
                : Task.FromResult(new GitHubPage<GitHubIssue>(
                    [new GitHubIssue(42, "Release tracking is stale", "open", "maintainer", ["release"], [], DateTimeOffset.UtcNow.AddDays(-1), null, $"https://github.com/{slug}/issues/42")], false));
        public Task<GitHubPage<GitHubPullRequest>> FetchPullRequestsAsync(string slug, string state = "open", CancellationToken cancellationToken = default) => Task.FromResult(new GitHubPage<GitHubPullRequest>([], false));
        public Task<GitHubIssueDetail?> FetchIssueDetailAsync(string slug, int issueNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubIssueDetail?>(null);
        public Task<GitHubPullRequestDetail?> FetchPullRequestDetailAsync(string slug, int pullRequestNumber, CancellationToken cancellationToken = default) => Task.FromResult<GitHubPullRequestDetail?>(null);
        public Task<GitHubPullRequestFiles> FetchPullRequestFilesAsync(string slug, int number, string expectedHeadSha, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitHubPage<GitHubCheck>> FetchChecksAsync(string slug, string headSha, CancellationToken cancellationToken = default) => Task.FromResult(new GitHubPage<GitHubCheck>([], false));
    }
}
