using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioStateDatabaseTests
{
    [Fact]
    public async Task PersistPortfolioSnapshotAsync_RoundTripsGitHubAndDriftSignals()
    {
        var stateDatabase = await CreateStateDatabaseAsync();

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
                    DefaultBranch: "main",
                    ProbedBranch: "main",
                    IsDefaultBranch: true,
                    BranchProtectionEnabled: true,
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
        Assert.Equal("main", reloaded.GitHubInbox.DefaultBranch);
        Assert.True(reloaded.GitHubInbox.BranchProtectionEnabled);
        Assert.Equal(RepositoryReleaseDriftStatus.Attention, reloaded.ReleaseDrift!.Status);
        Assert.Equal(RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow, reloaded.Git.PrimaryActionableDiagnostic?.Code);
        Assert.Single(reloaded.PlanResults!);
    }

    [Fact]
    public async Task PersistPortfolioViewStateAsync_RoundTripsFocusAndSearch()
    {
        var stateDatabase = await CreateStateDatabaseAsync();

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
        var stateDatabase = await CreateStateDatabaseAsync();

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
        var stateDatabase = await CreateStateDatabaseAsync();

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

    [Fact]
    public async Task PersistSigningReceiptsAsync_RoundTripsArtifactMetadata()
    {
        var stateDatabase = await CreateStateDatabaseAsync();

        await stateDatabase.PersistSigningReceiptsAsync(
            "session-1",
            [
                new ReleaseSigningReceipt(
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryName: "DbaClientX",
                    AdapterKind: "ModuleBuild",
                    ArtifactPath: @"C:\Temp\DbaClientX\DbaClientX.psd1",
                    ArtifactKind: "ModuleManifest",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Signed manifest.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var receipts = await stateDatabase.LoadSigningReceiptsAsync("session-1");

        var receipt = Assert.Single(receipts);
        Assert.Equal(@"C:\Temp\DbaClientX\DbaClientX.psd1", receipt.ArtifactPath);
        Assert.Equal("ModuleManifest", receipt.ArtifactKind);
        Assert.Equal(ReleaseSigningReceiptStatus.Signed, receipt.Status);
    }

    [Fact]
    public async Task PersistVerificationReceiptsAsync_RoundTripsDestinationAndStatus()
    {
        var stateDatabase = await CreateStateDatabaseAsync();

        await stateDatabase.PersistVerificationReceiptsAsync(
            "session-1",
            [
                new ReleaseVerificationReceipt(
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryName: "DbaClientX",
                    AdapterKind: "ModulePublish",
                    TargetName: "PSGallery",
                    TargetKind: "PowerShellRepository",
                    Destination: "https://www.powershellgallery.com/api/v2",
                    Status: ReleaseVerificationReceiptStatus.Verified,
                    Summary: "Verified published module.",
                    VerifiedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var receipts = await stateDatabase.LoadVerificationReceiptsAsync("session-1");

        var receipt = Assert.Single(receipts);
        Assert.Equal("https://www.powershellgallery.com/api/v2", receipt.Destination);
        Assert.Equal(ReleaseVerificationReceiptStatus.Verified, receipt.Status);
        Assert.Equal("PSGallery", receipt.TargetName);
    }

    [Fact]
    public async Task PersistGitQuickActionReceiptAsync_RoundTripsLatestAction()
    {
        var stateDatabase = await CreateStateDatabaseAsync();

        await stateDatabase.PersistGitQuickActionReceiptAsync(
            new RepositoryGitQuickActionReceipt(
                RootPath: @"C:\Support\GitHub\PSPublishModule",
                ActionTitle: "Create PR branch",
                ActionKind: RepositoryGitQuickActionKind.GitCommand,
                Payload: "git switch -c codex/pspublishmodule-release-flow",
                Succeeded: true,
                Summary: "Create PR branch completed successfully.",
                OutputTail: "Switched to a new branch 'codex/pspublishmodule-release-flow'",
                ErrorTail: null,
                ExecutedAtUtc: DateTimeOffset.UtcNow));

        var receipts = await stateDatabase.LoadGitQuickActionReceiptsAsync();

        var receipt = Assert.Single(receipts);
        Assert.Equal("Create PR branch", receipt.ActionTitle);
        Assert.True(receipt.Succeeded);
        Assert.Contains("Switched to a new branch", receipt.OutputTail);
    }

    [Fact]
    public async Task PersistPortfolioSnapshotAsync_RollsBackWhenSnapshotWriteFails()
    {
        var stateDatabase = await CreateStateDatabaseAsync();

        var originalPortfolio = new[] {
            CreatePortfolioItem(
                rootPath: @"C:\Support\GitHub\DbaClientX",
                repositoryName: "DbaClientX",
                readinessKind: RepositoryReadinessKind.Ready,
                readinessReason: "Original snapshot.")
        };

        await stateDatabase.PersistPortfolioSnapshotAsync(originalPortfolio);

        var duplicateRoot = @"C:\Support\GitHub\PSPublishModule";
        var conflictingPortfolio = new[] {
            CreatePortfolioItem(duplicateRoot, "PSPublishModule", RepositoryReadinessKind.Attention, "First duplicate row."),
            CreatePortfolioItem(duplicateRoot, "PSPublishModule-duplicate", RepositoryReadinessKind.Blocked, "Second duplicate row should fail.")
        };

        await Assert.ThrowsAsync<DBAClientX.DbaQueryExecutionException>(() => stateDatabase.PersistPortfolioSnapshotAsync(conflictingPortfolio));

        var reloaded = await stateDatabase.LoadPortfolioSnapshotAsync();

        var snapshot = Assert.Single(reloaded);
        Assert.Equal("DbaClientX", snapshot.Name);
        Assert.Equal(@"C:\Support\GitHub\DbaClientX", snapshot.RootPath);
        Assert.Equal("Original snapshot.", snapshot.ReadinessReason);
    }

    [Fact]
    public async Task PersistPlanSnapshotsAsync_ReplacesExistingRows()
    {
        var stateDatabase = await CreateStateDatabaseAsync();

        var originalPortfolio = new[] {
            CreatePortfolioItem(
                rootPath: @"C:\Support\GitHub\DbaClientX",
                repositoryName: "DbaClientX",
                readinessKind: RepositoryReadinessKind.Ready,
                readinessReason: "Original snapshot.") with {
                    PlanResults = [
                        new RepositoryPlanResult(
                            RepositoryPlanAdapterKind.ProjectPlan,
                            RepositoryPlanStatus.Succeeded,
                            "Original plan.",
                            PlanPath: @"C:\Temp\original.plan.json",
                            ExitCode: 0,
                            DurationSeconds: 1.0)
                    ]
                }
        };

        var replacementPortfolio = new[] {
            CreatePortfolioItem(
                rootPath: @"C:\Support\GitHub\PSPublishModule",
                repositoryName: "PSPublishModule",
                readinessKind: RepositoryReadinessKind.Attention,
                readinessReason: "Replacement snapshot.") with {
                    PlanResults = [
                        new RepositoryPlanResult(
                            RepositoryPlanAdapterKind.ModuleJsonExport,
                            RepositoryPlanStatus.Failed,
                            "Replacement plan.",
                            PlanPath: @"C:\Temp\replacement.plan.json",
                            ExitCode: 1,
                            DurationSeconds: 2.0)
                    ]
                }
        };

        await stateDatabase.PersistPortfolioSnapshotAsync(originalPortfolio);
        await stateDatabase.PersistPlanSnapshotsAsync(originalPortfolio);
        await stateDatabase.PersistPortfolioSnapshotAsync(replacementPortfolio);
        await stateDatabase.PersistPlanSnapshotsAsync(replacementPortfolio);

        var loaded = await stateDatabase.LoadPortfolioSnapshotAsync();

        var reloaded = Assert.Single(loaded);
        Assert.Equal("PSPublishModule", reloaded.Name);
        var plan = Assert.Single(reloaded.PlanResults!);
        Assert.Equal(RepositoryPlanAdapterKind.ModuleJsonExport, plan.AdapterKind);
        Assert.Equal("Replacement plan.", plan.Summary);
        Assert.Equal(@"C:\Temp\replacement.plan.json", plan.PlanPath);
    }

    private static async Task<ReleaseStateDatabase> CreateStateDatabaseAsync()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests");
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, $"{Guid.NewGuid():N}.db");
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        await stateDatabase.InitializeAsync();
        return stateDatabase;
    }

    private static RepositoryPortfolioItem CreatePortfolioItem(string rootPath, string repositoryName, RepositoryReadinessKind readinessKind, string readinessReason)
    {
        return new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: repositoryName,
                RootPath: rootPath,
                RepositoryKind: ReleaseRepositoryKind.Library,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: null,
                ProjectBuildScriptPath: Path.Combine(rootPath, "Build", "Build-Project.ps1"),
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(readinessKind, readinessReason));
    }
}

