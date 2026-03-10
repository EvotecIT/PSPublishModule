using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioStateDatabaseTests
{
    [Fact]
    public async Task PersistPortfolioSnapshotAsync_RoundTripsGitHubAndDriftSignals()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests");
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, $"{Guid.NewGuid():N}.db");
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        await stateDatabase.InitializeAsync();

        var portfolio = new[] {
            new RepositoryPortfolioItem(
                new RepositoryCatalogEntry(
                    Name: "DbaClientX",
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryKind: ReleaseRepositoryKind.Mixed,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    ModuleBuildScriptPath: @"C:\Support\GitHub\DbaClientX\Build\Build-Module.ps1",
                    ProjectBuildScriptPath: @"C:\Support\GitHub\DbaClientX\Build\Build-Project.ps1",
                    IsWorktree: false,
                    HasWebsiteSignals: false),
                new RepositoryGitSnapshot(true, "main", "origin/main", 1, 0, 0, 0),
                new RepositoryReadiness(RepositoryReadinessKind.Attention, "Ahead of upstream."),
                PlanResults: [
                    new RepositoryPlanResult(
                        RepositoryPlanAdapterKind.ProjectPlan,
                        RepositoryPlanStatus.Succeeded,
                        "Project build plan generated.",
                        PlanPath: @"C:\Temp\project.plan.json",
                        ExitCode: 0,
                        DurationSeconds: 1.5)
                ],
                GitHubInbox: new RepositoryGitHubInbox(
                    RepositoryGitHubInboxStatus.Attention,
                    RepositorySlug: "EvotecIT/DbaClientX",
                    OpenPullRequestCount: 2,
                    LatestWorkflowFailed: true,
                    LatestReleaseTag: "v0.2.0",
                    Summary: "2 open PR(s), latest workflow failed, latest release v0.2.0, 1 local commit(s) ahead",
                    Detail: "Current branch is ahead."),
                ReleaseDrift: new RepositoryReleaseDrift(
                    RepositoryReleaseDriftStatus.Attention,
                    "Branch is ahead of the latest release signal by 1 commit(s).",
                    "Current branch is ahead of upstream by 1 commit."))
        };

        await stateDatabase.PersistPortfolioSnapshotAsync(portfolio);
        await stateDatabase.PersistPlanSnapshotsAsync(portfolio);
        var loaded = await stateDatabase.LoadPortfolioSnapshotAsync();

        var reloaded = Assert.Single(loaded);
        Assert.NotNull(reloaded.GitHubInbox);
        Assert.NotNull(reloaded.ReleaseDrift);
        Assert.Equal("EvotecIT/DbaClientX", reloaded.GitHubInbox!.RepositorySlug);
        Assert.Equal(2, reloaded.GitHubInbox.OpenPullRequestCount);
        Assert.True(reloaded.GitHubInbox.LatestWorkflowFailed);
        Assert.Equal("v0.2.0", reloaded.GitHubInbox.LatestReleaseTag);
        Assert.Equal(RepositoryReleaseDriftStatus.Attention, reloaded.ReleaseDrift!.Status);
        Assert.Single(reloaded.PlanResults!);
    }

    [Fact]
    public async Task PersistPortfolioViewStateAsync_RoundTripsFocusAndSearch()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests");
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, $"{Guid.NewGuid():N}.db");
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        await stateDatabase.InitializeAsync();

        var viewState = new RepositoryPortfolioViewState(
            PresetKey: "ready-today",
            FocusMode: RepositoryPortfolioFocusMode.Ready,
            SearchText: "dba",
            FamilyKey: "dbaclientx",
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        await stateDatabase.PersistPortfolioViewStateAsync(viewState);
        var reloaded = await stateDatabase.LoadPortfolioViewStateAsync();

        Assert.NotNull(reloaded);
        Assert.Equal("ready-today", reloaded!.PresetKey);
        Assert.Equal(RepositoryPortfolioFocusMode.Ready, reloaded!.FocusMode);
        Assert.Equal("dba", reloaded.SearchText);
        Assert.Equal("dbaclientx", reloaded.FamilyKey);
    }

    [Fact]
    public async Task PersistQueueSessionAsync_RoundTripsScopeMetadata()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests");
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, $"{Guid.NewGuid():N}.db");
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        await stateDatabase.InitializeAsync();

        var session = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(1, 1, 0, 0, 0, 0),
            Items: [
                new ReleaseQueueItem(
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryName: "DbaClientX",
                    RepositoryKind: ReleaseRepositoryKind.Library,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Build,
                    Status: ReleaseQueueItemStatus.ReadyToRun,
                    Summary: "Ready to build.",
                    CheckpointKey: "build.ready",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ],
            ScopeKey: "dbaclientx",
            ScopeDisplayName: "DbaClientX");

        await stateDatabase.PersistQueueSessionAsync(session);
        var reloaded = await stateDatabase.LoadLatestQueueSessionAsync();

        Assert.NotNull(reloaded);
        Assert.Equal("dbaclientx", reloaded!.ScopeKey);
        Assert.Equal("DbaClientX", reloaded.ScopeDisplayName);
        Assert.Single(reloaded.Items);
    }

    [Fact]
    public async Task PersistPublishReceiptsAsync_RoundTripsSourcePath()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests");
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, $"{Guid.NewGuid():N}.db");
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        await stateDatabase.InitializeAsync();

        await stateDatabase.PersistPublishReceiptsAsync(
            "session-1",
            [
                new ReleasePublishReceipt(
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryName: "DbaClientX",
                    AdapterKind: "ProjectBuild",
                    TargetName: "NuGet",
                    TargetKind: "NuGet",
                    Destination: "https://api.nuget.org/v3/index.json",
                    SourcePath: @"C:\Temp\DbaClientX.1.2.3.nupkg",
                    Status: ReleasePublishReceiptStatus.Published,
                    Summary: "Published package.",
                    PublishedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var receipts = await stateDatabase.LoadPublishReceiptsAsync("session-1");

        var receipt = Assert.Single(receipts);
        Assert.Equal(@"C:\Temp\DbaClientX.1.2.3.nupkg", receipt.SourcePath);
        Assert.Equal("https://api.nuget.org/v3/index.json", receipt.Destination);
    }
}

