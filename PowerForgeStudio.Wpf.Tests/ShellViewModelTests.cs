using System.Reflection;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Workspace;
using PowerForgeStudio.Wpf.ViewModels;

namespace PowerForgeStudio.Wpf.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public async Task RefreshAsync_PopulatesReleaseSignalChildViewModel()
    {
        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(0, 0, 0, 1, 0, 0),
            Items: [
                new ReleaseQueueItem(
                    RootPath: @"C:\Support\GitHub\Repo.Attention",
                    RepositoryName: "Repo.Attention",
                    RepositoryKind: ReleaseRepositoryKind.Module,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Build,
                    Status: ReleaseQueueItemStatus.Failed,
                    Summary: "Build failed.",
                    CheckpointKey: "build.failed",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var snapshot = CreateSnapshot(
            portfolioItems: [
                CreateRepository(
                    name: "Repo.Attention",
                    rootPath: @"C:\Support\GitHub\Repo.Attention",
                    readinessKind: RepositoryReadinessKind.Attention,
                    gitHubInbox: new RepositoryGitHubInbox(
                        RepositoryGitHubInboxStatus.Attention,
                        "evotec/repo-attention",
                        2,
                        true,
                        "v1.2.3",
                        "main",
                        "main",
                        true,
                        true,
                        "2 PRs open.",
                        "GitHub needs attention."),
                    releaseDrift: new RepositoryReleaseDrift(
                        RepositoryReleaseDriftStatus.Attention,
                        "Ahead of latest release.",
                        "Local work appears beyond the latest tag.")),
                CreateRepository(
                    name: "Repo.Ready",
                    rootPath: @"C:\Support\GitHub\Repo.Ready",
                    readinessKind: RepositoryReadinessKind.Ready)
            ],
            releaseInboxItems: [
                new RepositoryReleaseInboxItem(
                    @"C:\Support\GitHub\Repo.Attention",
                    "Repo.Attention",
                    "Resolve queue failure",
                    "A failed queue item needs action.",
                    "Failed",
                    RepositoryPortfolioFocusMode.Failed,
                    string.Empty,
                    null,
                    0)
            ],
            queueSession: queueSession,
            summary: new RepositoryPortfolioSummary(2, 1, 1, 0, 0, 0, 0, 1, 2, 1));

        var viewModel = CreateViewModel(snapshot);

        await viewModel.RefreshAsync(forceRefresh: true);

        Assert.Single(viewModel.ReleaseSignals.ReleaseInboxItems);
        Assert.Single(viewModel.ReleaseSignals.GitHubInboxItems);
        Assert.Equal("1 action item(s), 1 queue failed, 0 git-action failed, 0 USB waiting", viewModel.ReleaseSignals.ReleaseInboxHeadline);
        Assert.Equal("1 repo(s) need GitHub attention, 2 open PR(s)", viewModel.ReleaseSignals.GitHubInboxHeadline);
        Assert.Equal("1 repo(s) show release drift, 1 repo(s) have a detected release tag", viewModel.ReleaseSignals.ReleaseDriftHeadline);
    }

    [Fact]
    public async Task SelectedFocus_UpdatesFilteredRepositories()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Attention", @"C:\Support\GitHub\Repo.Attention", RepositoryReadinessKind.Attention),
            CreateRepository("Repo.Ready", @"C:\Support\GitHub\Repo.Ready", RepositoryReadinessKind.Ready)
        ]);
        var viewModel = CreateViewModel(snapshot);

        await viewModel.RefreshAsync(forceRefresh: true);
        SetIsInitialized(viewModel, false);

        viewModel.PortfolioOverview.SelectedFocus = viewModel.PortfolioOverview.FocusModes.Single(mode => mode.Mode == RepositoryPortfolioFocusMode.Attention);

        var repository = Assert.Single(viewModel.Repositories);
        Assert.Equal("Repo.Attention", repository.Name);
        Assert.Contains("showing 1 of 2", viewModel.PortfolioOverview.FocusHeadline);
    }

    [Fact]
    public async Task WorkspaceRootChange_ClearsLoadedProjectionAndScopesDatabasePath()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Ready", @"C:\Support\GitHub\Repo.Ready", RepositoryReadinessKind.Ready)
        ]);
        var viewModel = CreateViewModel(snapshot);

        await viewModel.RefreshAsync(forceRefresh: true);

        viewModel.WorkspaceRoot = @"D:\Repos\Studio";

        Assert.Empty(viewModel.Repositories);
        Assert.Equal(PowerForgeStudioHostPaths.GetWorkspaceDatabasePath(@"D:\Repos\Studio"), viewModel.DatabasePath);
        Assert.Contains(@"Workspace root updated to D:\Repos\Studio", viewModel.StatusText);
        Assert.False(viewModel.RunNextQueueStepCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyRepositoryFamilyCommand_FiltersRepositoriesToSelectedFamily()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha", RepositoryReadinessKind.Ready, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Alpha.Worktree", @"C:\Support\GitHub\Repo.Alpha.Worktree", RepositoryReadinessKind.Ready, workspaceKind: ReleaseWorkspaceKind.Worktree, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta", RepositoryReadinessKind.Ready, familyKey: "beta", familyName: "Beta")
        ]);
        var viewModel = CreateViewModel(snapshot);

        await viewModel.RefreshAsync(forceRefresh: true);
        SetIsInitialized(viewModel, false);

        var selectedFamily = viewModel.RepositoryFamily.Families.Single(family =>
            !string.IsNullOrWhiteSpace(family.FamilyKey)
            && family.TotalMembers == 2);
        viewModel.ApplyRepositoryFamilyCommand.Execute(selectedFamily);

        Assert.Equal(2, viewModel.Repositories.Count);
        Assert.All(viewModel.Repositories, repository => Assert.Equal(selectedFamily.FamilyKey, repository.FamilyKey));
        Assert.Contains($"Repository family applied: {selectedFamily.DisplayName}.", viewModel.PortfolioOverview.ViewMemory);
    }

    [Fact]
    public async Task SelectedRepository_ReprojectsFamilyLaneAndDetail()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha", RepositoryReadinessKind.Ready),
            CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta", RepositoryReadinessKind.Attention)
        ]);
        var viewModel = CreateViewModel(snapshot);

        await viewModel.RefreshAsync(forceRefresh: true);

        viewModel.SelectedRepository = viewModel.Repositories.Single(repository => repository.Name == "Repo.Beta");

        Assert.StartsWith("Repo.Beta:", viewModel.RepositoryFamily.Headline);
        Assert.Equal("Repo.Beta", viewModel.RepositoryDetail.Headline);
        Assert.Contains("Attention", viewModel.RepositoryDetail.Readiness);
    }

    [Fact]
    public async Task QueueCommandCanExecute_TracksQueueStateFromStations()
    {
        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(1, 0, 1, 1, 0, 0),
            Items: [
                new ReleaseQueueItem(
                    RootPath: @"C:\Support\GitHub\Repo.Queue",
                    RepositoryName: "Repo.Queue",
                    RepositoryKind: ReleaseRepositoryKind.Module,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Build,
                    Status: ReleaseQueueItemStatus.ReadyToRun,
                    Summary: "Build can start.",
                    CheckpointKey: "build.ready",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: @"C:\Support\GitHub\Repo.Sign",
                    RepositoryName: "Repo.Sign",
                    RepositoryKind: ReleaseRepositoryKind.Module,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 2,
                    Stage: ReleaseQueueStage.Sign,
                    Status: ReleaseQueueItemStatus.WaitingApproval,
                    Summary: "USB approval required.",
                    CheckpointKey: "sign.waiting.usb",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: @"C:\Support\GitHub\Repo.Fail",
                    RepositoryName: "Repo.Fail",
                    RepositoryKind: ReleaseRepositoryKind.Module,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 3,
                    Stage: ReleaseQueueStage.Verify,
                    Status: ReleaseQueueItemStatus.Failed,
                    Summary: "Verification failed.",
                    CheckpointKey: "verify.failed",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var snapshot = CreateSnapshot(
            portfolioItems: [
                CreateRepository("Repo.Queue", @"C:\Support\GitHub\Repo.Queue", RepositoryReadinessKind.Ready),
                CreateRepository("Repo.Sign", @"C:\Support\GitHub\Repo.Sign", RepositoryReadinessKind.Ready),
                CreateRepository("Repo.Fail", @"C:\Support\GitHub\Repo.Fail", RepositoryReadinessKind.Ready)
            ],
            queueSession: queueSession);
        var viewModel = CreateViewModel(snapshot);

        await viewModel.RefreshAsync(forceRefresh: true);

        Assert.True(viewModel.RunNextQueueStepCommand.CanExecute(null));
        Assert.True(viewModel.ApproveUsbCommand.CanExecute(null));
        Assert.True(viewModel.RetryFailedCommand.CanExecute(null));
    }

    [Fact]
    public async Task PrepareSelectedFamilyQueueCommand_UsesFamilyQueueActionService()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha", RepositoryReadinessKind.Ready, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Alpha.Worktree", @"C:\Support\GitHub\Repo.Alpha.Worktree", RepositoryReadinessKind.Ready, workspaceKind: ReleaseWorkspaceKind.Worktree, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta", RepositoryReadinessKind.Ready, familyKey: "beta", familyName: "Beta")
        ]);
        var familyQueueActionService = new FakeFamilyQueueActionService(
            prepareResult: new FamilyQueueActionResult("Prepared the Alpha family queue."));
        var viewModel = CreateViewModel(snapshot, familyQueueActionService: familyQueueActionService);

        await viewModel.RefreshAsync(forceRefresh: true);

        var selectedFamily = viewModel.RepositoryFamily.Families.Single(family =>
            string.Equals(family.DisplayName, "Repo.Alpha", StringComparison.OrdinalIgnoreCase));
        viewModel.ApplyRepositoryFamilyCommand.Execute(selectedFamily);

        Assert.True(viewModel.PrepareSelectedFamilyQueueCommand.CanExecute(null));

        viewModel.PrepareSelectedFamilyQueueCommand.Execute(null);

        var invocation = await familyQueueActionService.WaitForPrepareAsync();
        Assert.Equal(viewModel.DatabasePath, invocation.DatabasePath);
        Assert.Equal(viewModel.WorkspaceRoot, invocation.WorkspaceRoot);
        Assert.Equal(selectedFamily.FamilyKey, invocation.Family?.FamilyKey);
        Assert.Equal(3, invocation.PortfolioCount);
        Assert.Equal("Prepared the Alpha family queue.", viewModel.StatusText);
    }

    [Fact]
    public async Task RetrySelectedFamilyFailedCommand_UsesFamilyQueueActionService()
    {
        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(0, 0, 0, 0, 0, 1),
            Items: [
                new ReleaseQueueItem(
                    RootPath: @"C:\Support\GitHub\Repo.Alpha",
                    RepositoryName: "Repo.Alpha",
                    RepositoryKind: ReleaseRepositoryKind.Module,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Build,
                    Status: ReleaseQueueItemStatus.Failed,
                    Summary: "Build failed.",
                    CheckpointKey: "build.failed",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var snapshot = CreateSnapshot(
            portfolioItems: [
                CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha", RepositoryReadinessKind.Attention, familyKey: "alpha", familyName: "Alpha"),
                CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta", RepositoryReadinessKind.Ready, familyKey: "beta", familyName: "Beta")
            ],
            queueSession: queueSession);
        var familyQueueActionService = new FakeFamilyQueueActionService(
            retryResult: new FamilyQueueActionResult("Retried failed items for Alpha."));
        var viewModel = CreateViewModel(snapshot, familyQueueActionService: familyQueueActionService);

        await viewModel.RefreshAsync(forceRefresh: true);

        var selectedFamily = viewModel.RepositoryFamily.Families.Single(family =>
            string.Equals(family.DisplayName, "Repo.Alpha", StringComparison.OrdinalIgnoreCase));
        viewModel.ApplyRepositoryFamilyCommand.Execute(selectedFamily);

        Assert.True(viewModel.RetrySelectedFamilyFailedCommand.CanExecute(null));

        viewModel.RetrySelectedFamilyFailedCommand.Execute(null);

        var invocation = await familyQueueActionService.WaitForRetryAsync();
        Assert.Equal(viewModel.DatabasePath, invocation.DatabasePath);
        Assert.Equal(selectedFamily.FamilyKey, invocation.Family?.FamilyKey);
        Assert.Equal(queueSession.SessionId, invocation.QueueSession?.SessionId);
        Assert.Equal("Retried failed items for Alpha.", viewModel.StatusText);
    }

    [Fact]
    public async Task ExecuteGitQuickActionCommand_UsesWorkflowServiceAndUpdatesDetail()
    {
        const string repositoryRoot = @"C:\Support\GitHub\Repo.Action";
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Action", repositoryRoot, RepositoryReadinessKind.Attention)
        ]);
        var action = new RepositoryGitQuickAction(
            Title: "Fetch origin",
            Summary: "Fetch the latest refs from origin.",
            Kind: RepositoryGitQuickActionKind.GitCommand,
            Payload: "git fetch origin",
            ExecuteLabel: "Fetch",
            IsPrimary: true,
            GitOperation: RepositoryGitOperationKind.StatusShortBranch);
        var receipt = new RepositoryGitQuickActionReceipt(
            RootPath: repositoryRoot,
            ActionTitle: action.Title,
            ActionKind: action.Kind,
            Payload: action.Payload,
            Succeeded: true,
            Summary: "Fetch completed.",
            OutputTail: "Already up to date.",
            ErrorTail: null,
            ExecutedAtUtc: DateTimeOffset.UtcNow);
        var workflowService = new FakeRepositoryGitQuickActionWorkflowService(
            new RepositoryGitQuickActionWorkflowResult(
                "Fetch completed. Already up to date.",
                receipt,
                ShouldRefresh: false));
        var viewModel = CreateViewModel(snapshot, gitQuickActionWorkflowService: workflowService);

        await viewModel.RefreshAsync(forceRefresh: true);

        viewModel.ExecuteGitQuickActionCommand.Execute(action);

        var invocation = await workflowService.WaitForExecuteAsync();
        Assert.Equal(viewModel.DatabasePath, invocation.DatabasePath);
        Assert.Equal(repositoryRoot, invocation.RepositoryRootPath);
        Assert.Equal(action.Title, invocation.Action?.Title);
        Assert.Equal("Fetch origin (Succeeded)", viewModel.RepositoryDetail.LastGitAction);
        Assert.Equal("Fetch completed.", viewModel.RepositoryDetail.LastGitActionSummary);
        Assert.Equal("Already up to date.", viewModel.RepositoryDetail.LastGitActionOutput);
        Assert.Equal("No error captured yet.", viewModel.RepositoryDetail.LastGitActionError);
        Assert.Equal("Fetch completed. Already up to date.", viewModel.StatusText);
    }

    [Fact]
    public async Task SearchText_PersistsPortfolioViewStateThroughPersistenceService()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.One", @"C:\Support\GitHub\Repo.One", RepositoryReadinessKind.Ready)
        ]);
        var persistence = new FakePortfolioViewStatePersistenceService();
        var viewModel = CreateViewModel(snapshot, persistence, TimeSpan.FromMilliseconds(1));

        await viewModel.RefreshAsync(forceRefresh: true);

        viewModel.PortfolioOverview.SearchText = "Repo";

        var persisted = await persistence.WaitForPersistAsync();
        Assert.Equal("Repo", persisted.SearchText);
        Assert.Equal(RepositoryPortfolioFocusMode.All, persisted.FocusMode);
    }

    [Fact]
    public async Task RefreshAsync_RestoresSavedPortfolioViewState()
    {
        var snapshot = CreateSnapshot(
            portfolioItems: [
                CreateRepository("Repo.Attention", @"C:\Support\GitHub\Repo.Attention", RepositoryReadinessKind.Attention, familyKey: "attention", familyName: "Attention"),
                CreateRepository("Repo.Ready", @"C:\Support\GitHub\Repo.Ready", RepositoryReadinessKind.Ready, familyKey: "ready", familyName: "Ready")
            ],
            savedPortfolioView: new RepositoryPortfolioViewState(
                PresetKey: null,
                FocusMode: RepositoryPortfolioFocusMode.Attention,
                SearchText: "Attention",
                FamilyKey: "attention",
                UpdatedAtUtc: DateTimeOffset.UtcNow));
        var viewModel = CreateViewModel(snapshot);

        await viewModel.RefreshAsync(forceRefresh: true);

        Assert.Equal(RepositoryPortfolioFocusMode.Attention, viewModel.PortfolioOverview.SelectedFocus.Mode);
        Assert.Equal("Attention", viewModel.PortfolioOverview.SearchText);
        Assert.Contains("Restored saved triage view", viewModel.PortfolioOverview.ViewMemory);
    }

    [Fact]
    public async Task RefreshAsync_RemembersActiveWorkspaceRoot()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Ready", @"C:\Support\GitHub\Repo.Ready", RepositoryReadinessKind.Ready)
        ]);
        var snapshotService = new FakeWorkspaceSnapshotService(snapshot);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: @"C:\Support\GitHub",
            RecentWorkspaceRoots: [@"C:\Support\GitHub"],
            ActiveProfileId: null,
            Profiles: []));
        var viewModel = CreateViewModel(
            snapshot,
            workspaceSnapshotService: snapshotService,
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        Assert.Equal(@"C:\Support\GitHub", snapshotService.LastWorkspaceRoot);
        Assert.Equal(PowerForgeStudioHostPaths.GetWorkspaceDatabasePath(@"C:\Support\GitHub"), snapshotService.LastDatabasePath);
        Assert.Equal(@"C:\Support\GitHub", Assert.Single(rootCatalogService.SavedRoots));
    }

    [Fact]
    public async Task InitializeAsync_ReopensActiveWorkspaceProfileOnStartup()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Attention", Path.Combine(workspaceRoot, "Repo.Attention"), RepositoryReadinessKind.Attention, familyKey: "repoattention", familyName: "Repo.Attention"),
            CreateRepository("Repo.Ready", Path.Combine(workspaceRoot, "Repo.Ready"), RepositoryReadinessKind.Ready, familyKey: "repoready", familyName: "Repo.Ready")
        ]);
        var persistence = new FakePortfolioViewStatePersistenceService([
            new RepositoryPortfolioSavedView(
                "attention-focus",
                "Attention Focus",
                new RepositoryPortfolioViewState(
                    PresetKey: "attention",
                    FocusMode: RepositoryPortfolioFocusMode.Attention,
                    SearchText: string.Empty,
                    FamilyKey: "repoattention",
                    UpdatedAtUtc: DateTimeOffset.UtcNow))
        ]);
        var snapshotService = new FakeWorkspaceSnapshotService(snapshot);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: workspaceRoot,
            RecentWorkspaceRoots: [workspaceRoot],
            ActiveProfileId: "modules",
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Daily module release desk",
                    TodayNote: "Start with attention repos, then prep the Alpha queue.",
                    LastLaunchResult: new WorkspaceProfileLaunchResult(
                        WorkspaceProfileLaunchActionKind.ApplySavedView,
                        "Apply Saved View",
                        true,
                        "Applied saved portfolio view 'Attention Focus'.",
                        DateTimeOffset.UtcNow),
                    LaunchHistory: [
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.ApplySavedView,
                            "Apply Saved View",
                            true,
                            "Applied saved portfolio view 'Attention Focus'.",
                            DateTimeOffset.UtcNow),
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                            "Refresh Workspace",
                            true,
                            "Portfolio ready.",
                            DateTimeOffset.UtcNow.AddMinutes(-25))
                    ],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: "attention-focus",
                    QueueScopeKey: "repoattention",
                    QueueScopeDisplayName: "Repo.Attention",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            persistenceService: persistence,
            workspaceSnapshotService: snapshotService,
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.InitializeAsync();

        Assert.Equal(workspaceRoot, snapshotService.LastWorkspaceRoot);
        Assert.Equal("Modules", viewModel.PortfolioOverview.SelectedWorkspaceProfile?.DisplayName);
        Assert.Contains("Applied workspace profile 'Modules'", viewModel.StatusText);
        Assert.Equal("Active profile: Modules", viewModel.PortfolioOverview.ActiveWorkspaceContextHeadline);
        Assert.Contains("Saved view: Attention Focus", viewModel.PortfolioOverview.ActiveWorkspaceContextDetails);
        Assert.Contains("Queue scope: Repo.Attention", viewModel.PortfolioOverview.ActiveWorkspaceContextDetails);
        Assert.Equal("Today's agenda for Modules", viewModel.PortfolioOverview.ActiveWorkspaceAgendaHeadline);
        Assert.Equal("Start with attention repos, then prep the Alpha queue.", viewModel.PortfolioOverview.ActiveWorkspaceAgendaDetails);
        var startupCard = Assert.Single(viewModel.PortfolioOverview.WorkspaceProfileCards);
        Assert.Equal("Daily module release desk", startupCard.Description);
        Assert.Equal("Start with attention repos, then prep the Alpha queue.", startupCard.TodayNote);
        Assert.Equal(3, viewModel.PortfolioOverview.ActiveWorkspaceLaunchActions.Count);
        Assert.Contains(viewModel.PortfolioOverview.ActiveWorkspaceLaunchActions, action => action.Kind == WorkspaceProfileLaunchActionKind.RefreshWorkspace);
        Assert.Contains(viewModel.PortfolioOverview.ActiveWorkspaceLaunchActions, action => action.Kind == WorkspaceProfileLaunchActionKind.ApplySavedView);
        Assert.Contains(viewModel.PortfolioOverview.ActiveWorkspaceLaunchActions, action => action.Kind == WorkspaceProfileLaunchActionKind.PrepareQueue);
        Assert.Contains("Last run:", viewModel.PortfolioOverview.ActiveWorkspaceLaunchBoardDetails);
        Assert.Contains("Apply Saved View", viewModel.PortfolioOverview.ActiveWorkspaceLaunchBoardDetails);
        Assert.Equal(2, viewModel.PortfolioOverview.ActiveWorkspaceLaunchTimeline.Count);
        Assert.Equal("Apply Saved View", viewModel.PortfolioOverview.ActiveWorkspaceLaunchTimeline[0].Title);
        Assert.Equal("Desk health: Green Today", viewModel.PortfolioOverview.ActiveWorkspaceHealthHeadline);
        Assert.Contains("Today's recorded actions are succeeding.", viewModel.PortfolioOverview.ActiveWorkspaceHealthDetails);
        Assert.StartsWith("View restored at ", viewModel.PortfolioOverview.ActiveWorkspaceReceiptHeadline);
        Assert.Contains("Applied saved portfolio view 'Attention Focus'.", viewModel.PortfolioOverview.ActiveWorkspaceReceiptDetails);
        Assert.Equal(WorkspaceProfileLaunchActionKind.PrepareQueue, viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.Kind);
        Assert.Equal("Prepare Queue", viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.Title);
        Assert.Equal("Pending", viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.LastRunLabel);
        Assert.True(viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.CanExecute);
        Assert.Equal(2, viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions.Count);
        Assert.Equal("Apply Saved View", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[0].Title);
        Assert.Equal("Prepare Queue", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[1].Title);
        Assert.Equal("Green Today", startupCard.HealthLabel);
        Assert.Equal(WorkspaceProfileHeroRouteKind.OpenDesk, startupCard.RouteKind);
        Assert.Equal("Open Desk", startupCard.RouteLabel);
    }

    [Fact]
    public async Task InitializeAsync_MarksReceiptFollowUpCompletedWhenHistoryAlreadySatisfiedIt()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var refreshedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30);
        var preparedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", Path.Combine(workspaceRoot, "Repo.Alpha"), RepositoryReadinessKind.Ready, familyKey: "repoalpha", familyName: "Repo.Alpha")
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: workspaceRoot,
            RecentWorkspaceRoots: [workspaceRoot],
            ActiveProfileId: "modules",
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Prepared desk",
                    TodayNote: "Queue prep already happened.",
                    LastLaunchResult: new WorkspaceProfileLaunchResult(
                        WorkspaceProfileLaunchActionKind.PrepareQueue,
                        "Prepare Queue",
                        true,
                        "Prepared the Alpha family queue.",
                        preparedAtUtc),
                    LaunchHistory: [
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.PrepareQueue,
                            "Prepare Queue",
                            true,
                            "Prepared the Alpha family queue.",
                            preparedAtUtc),
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                            "Refresh Workspace",
                            true,
                            "Portfolio ready.",
                            refreshedAtUtc)
                    ],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: null,
                    QueueScopeKey: "repoalpha",
                    QueueScopeDisplayName: "Repo.Alpha",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            workspaceSnapshotService: new FakeWorkspaceSnapshotService(snapshot),
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.InitializeAsync();

        Assert.Equal(WorkspaceProfileLaunchActionKind.PrepareQueue, viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.Kind);
        Assert.StartsWith("Completed ", viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.LastRunLabel);
        Assert.False(viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.CanExecute);
        Assert.Equal(2, viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions.Count);
        Assert.Equal("Refresh Workspace", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[0].Title);
        Assert.Equal("Prepare Queue", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[1].Title);
    }

    [Fact]
    public async Task InitializeAsync_BuildsOrderedReceiptActionChainForDeskProgress()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var refreshedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-45);
        var preparedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20);
        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: workspaceRoot,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(0, 0, 0, 0, 0, 1),
            Items: [
                new ReleaseQueueItem(
                    RootPath: Path.Combine(workspaceRoot, "Repo.Alpha"),
                    RepositoryName: "Repo.Alpha",
                    RepositoryKind: ReleaseRepositoryKind.Module,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Build,
                    Status: ReleaseQueueItemStatus.Failed,
                    Summary: "Build failed.",
                    CheckpointKey: "build.failed",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);
        var snapshot = CreateSnapshot(
            portfolioItems: [
                CreateRepository("Repo.Alpha", Path.Combine(workspaceRoot, "Repo.Alpha"), RepositoryReadinessKind.Attention, familyKey: "repoalpha", familyName: "Repo.Alpha")
            ],
            queueSession: queueSession);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: workspaceRoot,
            RecentWorkspaceRoots: [workspaceRoot],
            ActiveProfileId: "modules",
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Release desk",
                    TodayNote: "Retry the failed family if queue prep is already done.",
                    LastLaunchResult: new WorkspaceProfileLaunchResult(
                        WorkspaceProfileLaunchActionKind.PrepareQueue,
                        "Prepare Queue",
                        true,
                        "Prepared the Alpha family queue.",
                        preparedAtUtc),
                    LaunchHistory: [
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.PrepareQueue,
                            "Prepare Queue",
                            true,
                            "Prepared the Alpha family queue.",
                            preparedAtUtc),
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                            "Refresh Workspace",
                            true,
                            "Portfolio ready.",
                            refreshedAtUtc)
                    ],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: null,
                    QueueScopeKey: "repoalpha",
                    QueueScopeDisplayName: "Repo.Alpha",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            workspaceSnapshotService: new FakeWorkspaceSnapshotService(snapshot),
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.InitializeAsync();

        Assert.Equal(3, viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions.Count);
        Assert.Equal("Refresh Workspace", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[0].Title);
        Assert.StartsWith("Completed ", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[0].LastRunLabel);
        Assert.Equal("Prepare Queue", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[1].Title);
        Assert.StartsWith("Completed ", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[1].LastRunLabel);
        Assert.Equal("Retry Failed Family", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[2].Title);
        Assert.Equal("Pending", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[2].LastRunLabel);
        Assert.True(viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[2].CanExecute);
        Assert.Equal(WorkspaceProfileLaunchActionKind.RetryFailedFamily, viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.Kind);
    }

    [Fact]
    public async Task SaveWorkspaceProfileCommand_PersistsActiveProfileWithSavedViewContext()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Ready", @"C:\Support\GitHub\Repo.Ready", RepositoryReadinessKind.Ready, familyKey: "ready", familyName: "Ready")
        ]);
        var persistence = new FakePortfolioViewStatePersistenceService([
            new RepositoryPortfolioSavedView(
                "ready-candidates",
                "Ready Candidates",
                new RepositoryPortfolioViewState(
                    PresetKey: "ready-today",
                    FocusMode: RepositoryPortfolioFocusMode.Ready,
                    SearchText: string.Empty,
                    FamilyKey: "ready",
                    UpdatedAtUtc: DateTimeOffset.UtcNow))
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService();
        var viewModel = CreateViewModel(snapshot, persistenceService: persistence, workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);
        viewModel.PortfolioOverview.SelectedSavedView = Assert.Single(viewModel.PortfolioOverview.SavedViews);
        viewModel.PortfolioOverview.WorkspaceProfileDraftName = "Modules Daily";
        viewModel.PortfolioOverview.WorkspaceProfileDraftDescription = "Daily module release desk";
        viewModel.PortfolioOverview.WorkspaceProfileDraftTodayNote = "Ship the ready lane and verify receipts before lunch.";
        viewModel.PortfolioOverview.WorkspaceProfileDraftActionChain = "Refresh Workspace, Prepare Queue, Retry Failed Family";
        viewModel.PortfolioOverview.WorkspaceProfileDraftStartupFocus = "Attention";
        viewModel.PortfolioOverview.WorkspaceProfileDraftStartupSearch = "Ready";
        viewModel.PortfolioOverview.WorkspaceProfileDraftStartupFamily = "Ready";
        viewModel.PortfolioOverview.WorkspaceProfileDraftApplyStartupPreferenceAfterSavedView = true;
        viewModel.SaveWorkspaceProfileCommand.Execute(null);
        await WaitForConditionAsync(() => viewModel.PortfolioOverview.SelectedWorkspaceProfile is not null);

        var profile = Assert.Single(rootCatalogService.Catalog.Profiles);
        Assert.Equal("modules-daily", profile.ProfileId);
        Assert.Equal("Daily module release desk", profile.Description);
        Assert.Equal("Ship the ready lane and verify receipts before lunch.", profile.TodayNote);
        Assert.Equal(
            [
                WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                WorkspaceProfileLaunchActionKind.PrepareQueue,
                WorkspaceProfileLaunchActionKind.RetryFailedFamily
            ],
            profile.PreferredActionKinds);
        Assert.Equal(RepositoryPortfolioFocusMode.Attention, profile.PreferredStartupFocusMode);
        Assert.Equal("Ready", profile.PreferredStartupSearchText);
        Assert.Equal("Ready", profile.PreferredStartupFamilyKey);
        Assert.Equal("Ready", profile.PreferredStartupFamilyDisplayName);
        Assert.True(profile.ApplyStartupPreferenceAfterSavedView);
        Assert.Equal("ready-candidates", profile.SavedViewId);
        Assert.Equal("Modules Daily", viewModel.PortfolioOverview.SelectedWorkspaceProfile?.DisplayName);
        Assert.Equal("Saved workspace profile 'Modules Daily'.", viewModel.StatusText);
        var savedCard = Assert.Single(viewModel.PortfolioOverview.WorkspaceProfileCards);
        Assert.Equal("Daily module release desk", savedCard.Description);
        Assert.Equal("Ship the ready lane and verify receipts before lunch.", savedCard.TodayNote);
        Assert.Contains("saved view + startup emphasis", savedCard.StartupStrategyLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyWorkspaceProfileTemplateCommand_PrefillsDraftAndSavesFamilyScopedProfile()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha", RepositoryReadinessKind.Ready, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Alpha.Worktree", @"C:\Support\GitHub\Repo.Alpha.Worktree", RepositoryReadinessKind.Ready, workspaceKind: ReleaseWorkspaceKind.Worktree, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta", RepositoryReadinessKind.Attention, familyKey: "beta", familyName: "Beta")
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService();
        var viewModel = CreateViewModel(snapshot, workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        var alphaFamily = viewModel.RepositoryFamily.Families.Single(family =>
            string.Equals(family.DisplayName, "Alpha", StringComparison.OrdinalIgnoreCase)
            || string.Equals(family.FamilyKey, "alpha", StringComparison.OrdinalIgnoreCase)
            || family.TotalMembers == 2);
        viewModel.ApplyRepositoryFamilyCommand.Execute(alphaFamily);
        viewModel.PortfolioOverview.SelectedWorkspaceProfileTemplate = viewModel.PortfolioOverview.WorkspaceProfileTemplates.Single(template =>
            string.Equals(template.TemplateId, "daily-modules", StringComparison.OrdinalIgnoreCase));

        Assert.True(viewModel.ApplyWorkspaceProfileTemplateCommand.CanExecute(null));
        viewModel.ApplyWorkspaceProfileTemplateCommand.Execute(null);

        Assert.Equal("Daily Modules", viewModel.PortfolioOverview.WorkspaceProfileDraftName);
        Assert.Equal("Daily module release desk", viewModel.PortfolioOverview.WorkspaceProfileDraftDescription);
        Assert.Contains("prepare the current family queue", viewModel.PortfolioOverview.WorkspaceProfileDraftTodayNote, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Refresh Workspace, Prepare Queue, Retry Failed Family", viewModel.PortfolioOverview.WorkspaceProfileDraftActionChain);
        Assert.Equal("Ready Today", viewModel.PortfolioOverview.WorkspaceProfileDraftStartupFocus);
        Assert.Equal(alphaFamily.DisplayName, viewModel.PortfolioOverview.WorkspaceProfileDraftStartupFamily);
        Assert.Contains($"aligned the desk to family '{alphaFamily.DisplayName}'", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);

        viewModel.SaveWorkspaceProfileCommand.Execute(null);
        await WaitForConditionAsync(() => viewModel.PortfolioOverview.SelectedWorkspaceProfile is not null);

        var savedProfile = Assert.Single(rootCatalogService.Catalog.Profiles);
        Assert.Equal(alphaFamily.FamilyKey, savedProfile.QueueScopeKey);
        Assert.Equal(alphaFamily.DisplayName, savedProfile.QueueScopeDisplayName);
        Assert.Equal(RepositoryPortfolioFocusMode.Ready, savedProfile.PreferredStartupFocusMode);
        Assert.Equal(alphaFamily.DisplayName, savedProfile.PreferredStartupFamilyDisplayName);
        Assert.Equal(
            [
                WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                WorkspaceProfileLaunchActionKind.PrepareQueue,
                WorkspaceProfileLaunchActionKind.RetryFailedFamily
            ],
            savedProfile.PreferredActionKinds);
    }

    [Fact]
    public async Task SaveWorkspaceProfileTemplateCommand_PersistsAndDeletesCustomTemplate()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha", RepositoryReadinessKind.Ready, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta", RepositoryReadinessKind.Attention, familyKey: "beta", familyName: "Beta")
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService();
        var viewModel = CreateViewModel(snapshot, workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        var alphaFamily = viewModel.RepositoryFamily.Families.First();
        viewModel.ApplyRepositoryFamilyCommand.Execute(alphaFamily);
        viewModel.PortfolioOverview.WorkspaceProfileDraftName = "Modules Custom";
        viewModel.PortfolioOverview.WorkspaceProfileDraftDescription = "Custom module release desk";
        viewModel.PortfolioOverview.WorkspaceProfileDraftTodayNote = "Focus on the ready family and verify receipts.";
        viewModel.PortfolioOverview.WorkspaceProfileDraftActionChain = "Refresh Workspace, Prepare Queue";
        viewModel.PortfolioOverview.WorkspaceProfileDraftStartupFocus = "Ready Today";
        viewModel.PortfolioOverview.WorkspaceProfileDraftStartupFamily = alphaFamily.DisplayName;
        viewModel.PortfolioOverview.WorkspaceProfileDraftApplyStartupPreferenceAfterSavedView = true;

        Assert.True(viewModel.SaveWorkspaceProfileTemplateCommand.CanExecute(null));
        viewModel.SaveWorkspaceProfileTemplateCommand.Execute(null);
        await WaitForConditionAsync(() => rootCatalogService.Catalog.Templates?.Count == 1);

        var customTemplate = Assert.Single(rootCatalogService.Catalog.Templates!);
        Assert.Equal("Modules Custom", customTemplate.DisplayName);
        Assert.False(customTemplate.IsBuiltIn);
        Assert.Equal(alphaFamily.DisplayName, customTemplate.PreferredStartupFamily);
        Assert.Contains(viewModel.PortfolioOverview.WorkspaceProfileTemplates, template =>
            string.Equals(template.TemplateId, customTemplate.TemplateId, StringComparison.OrdinalIgnoreCase));

        viewModel.PortfolioOverview.SelectedWorkspaceProfileTemplate = viewModel.PortfolioOverview.WorkspaceProfileTemplates.Single(template =>
            string.Equals(template.TemplateId, customTemplate.TemplateId, StringComparison.OrdinalIgnoreCase));
        Assert.True(viewModel.DeleteWorkspaceProfileTemplateCommand.CanExecute(null));
        viewModel.DeleteWorkspaceProfileTemplateCommand.Execute(null);
        await WaitForConditionAsync(() => (rootCatalogService.Catalog.Templates?.Count ?? 0) == 0);

        Assert.DoesNotContain(viewModel.PortfolioOverview.WorkspaceProfileTemplates, template =>
            string.Equals(template.TemplateId, customTemplate.TemplateId, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Deleted custom template 'Modules Custom'.", viewModel.StatusText);
    }

    [Fact]
    public async Task CreateWorkspaceProfileFromTemplateCommand_CreatesSavedProfileInOneStep()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha", RepositoryReadinessKind.Ready, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Alpha.Worktree", @"C:\Support\GitHub\Repo.Alpha.Worktree", RepositoryReadinessKind.Ready, workspaceKind: ReleaseWorkspaceKind.Worktree, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta", RepositoryReadinessKind.Attention, familyKey: "beta", familyName: "Beta")
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService();
        var viewModel = CreateViewModel(snapshot, workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        var alphaFamily = viewModel.RepositoryFamily.Families.Single(family =>
            string.Equals(family.DisplayName, "Alpha", StringComparison.OrdinalIgnoreCase)
            || family.TotalMembers == 2);
        viewModel.ApplyRepositoryFamilyCommand.Execute(alphaFamily);
        viewModel.PortfolioOverview.SelectedWorkspaceProfileTemplate = viewModel.PortfolioOverview.WorkspaceProfileTemplates.Single(template =>
            string.Equals(template.TemplateId, "daily-modules", StringComparison.OrdinalIgnoreCase));

        Assert.True(viewModel.CreateWorkspaceProfileFromTemplateCommand.CanExecute(null));
        viewModel.CreateWorkspaceProfileFromTemplateCommand.Execute(null);
        await WaitForConditionAsync(() => viewModel.PortfolioOverview.SelectedWorkspaceProfile is not null);

        var savedProfile = Assert.Single(rootCatalogService.Catalog.Profiles);
        Assert.Equal("Daily Modules", savedProfile.DisplayName);
        Assert.Equal(alphaFamily.FamilyKey, savedProfile.QueueScopeKey);
        Assert.Equal(alphaFamily.DisplayName, savedProfile.QueueScopeDisplayName);
        Assert.Equal("Daily Modules", viewModel.PortfolioOverview.SelectedWorkspaceProfile?.DisplayName);
        Assert.Contains("Created workspace profile 'Daily Modules' from template 'Daily Modules'.", viewModel.StatusText);
    }

    [Fact]
    public async Task ApplyWorkspaceProfileCommand_SwitchesRootAndAppliesSavedView()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Attention", Path.Combine(workspaceRoot, "Repo.Attention"), RepositoryReadinessKind.Attention, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Ready", Path.Combine(workspaceRoot, "Repo.Ready"), RepositoryReadinessKind.Ready, familyKey: "beta", familyName: "Beta")
        ]);
        var persistence = new FakePortfolioViewStatePersistenceService([
            new RepositoryPortfolioSavedView(
                "attention-focus",
                "Attention Focus",
                new RepositoryPortfolioViewState(
                    PresetKey: "attention",
                    FocusMode: RepositoryPortfolioFocusMode.Attention,
                    SearchText: "Attention",
                    FamilyKey: "alpha",
                    UpdatedAtUtc: DateTimeOffset.UtcNow))
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: @"C:\Support\GitHub",
            RecentWorkspaceRoots: [@"C:\Support\GitHub"],
            ActiveProfileId: null,
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Module estate",
                    TodayNote: "Resolve attention items before building.",
                    LastLaunchResult: null,
                    LaunchHistory: [],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: "attention-focus",
                    QueueScopeKey: "alpha",
                    QueueScopeDisplayName: "Alpha",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var snapshotService = new FakeWorkspaceSnapshotService(snapshot);
        var viewModel = CreateViewModel(
            snapshot,
            persistenceService: persistence,
            workspaceSnapshotService: snapshotService,
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        viewModel.PortfolioOverview.SelectedWorkspaceProfile = Assert.Single(viewModel.PortfolioOverview.WorkspaceProfiles);
        viewModel.ApplyWorkspaceProfileCommand.Execute(null);
        await WaitForConditionAsync(() =>
            string.Equals(snapshotService.LastWorkspaceRoot, workspaceRoot, StringComparison.OrdinalIgnoreCase)
            && !viewModel.IsBusy
            && viewModel.PortfolioOverview.SelectedFocus.Mode == RepositoryPortfolioFocusMode.Attention
            && string.Equals(viewModel.PortfolioOverview.SelectedSavedView?.DisplayName, "Attention Focus", StringComparison.Ordinal));

        var repositories = viewModel.Repositories.ToArray();
        Assert.Equal(workspaceRoot, viewModel.WorkspaceRoot);
        Assert.Equal(RepositoryPortfolioFocusMode.Attention, viewModel.PortfolioOverview.SelectedFocus.Mode);
        Assert.Equal("Attention Focus", viewModel.PortfolioOverview.SelectedSavedView?.DisplayName);
        Assert.Equal("Repo.Attention", Assert.Single(repositories).Name);
        Assert.Contains("Applied workspace profile 'Modules'", viewModel.StatusText);
    }

    [Fact]
    public async Task InitializeAsync_UsesProfilePreferredActionSequenceForReceiptChain()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var refreshedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-15);
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", Path.Combine(workspaceRoot, "Repo.Alpha"), RepositoryReadinessKind.Ready, familyKey: "repoalpha", familyName: "Repo.Alpha")
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: workspaceRoot,
            RecentWorkspaceRoots: [workspaceRoot],
            ActiveProfileId: "modules",
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Custom sequence desk",
                    TodayNote: "Open attention before queue prep.",
                    LastLaunchResult: new WorkspaceProfileLaunchResult(
                        WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                        "Refresh Workspace",
                        true,
                        "Portfolio ready.",
                        refreshedAtUtc),
                    LaunchHistory: [
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                            "Refresh Workspace",
                            true,
                            "Portfolio ready.",
                            refreshedAtUtc)
                    ],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: null,
                    QueueScopeKey: "repoalpha",
                    QueueScopeDisplayName: "Repo.Alpha",
                    PreferredActionKinds: [
                        WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                        WorkspaceProfileLaunchActionKind.OpenAttentionView,
                        WorkspaceProfileLaunchActionKind.PrepareQueue
                    ],
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            workspaceSnapshotService: new FakeWorkspaceSnapshotService(snapshot),
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.InitializeAsync();

        Assert.Equal(3, viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions.Count);
        Assert.Equal("Refresh Workspace", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[0].Title);
        Assert.Equal("Open Attention View", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[1].Title);
        Assert.Equal("Prepare Queue", viewModel.PortfolioOverview.ActiveWorkspaceReceiptActions[2].Title);
        Assert.Equal(WorkspaceProfileLaunchActionKind.PrepareQueue, viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.Kind);
    }

    [Fact]
    public async Task InitializeAsync_UsesProfileStartupPreferenceWhenNoSavedViewIsPinned()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", Path.Combine(workspaceRoot, "Repo.Alpha"), RepositoryReadinessKind.Attention, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Beta", Path.Combine(workspaceRoot, "Repo.Beta"), RepositoryReadinessKind.Ready, familyKey: "beta", familyName: "Beta")
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: workspaceRoot,
            RecentWorkspaceRoots: [workspaceRoot],
            ActiveProfileId: "modules",
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Startup preference desk",
                    TodayNote: "Open Alpha attention view first.",
                    LastLaunchResult: null,
                    LaunchHistory: [],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: null,
                    QueueScopeKey: null,
                    QueueScopeDisplayName: null,
                    PreferredActionKinds: null,
                    PreferredStartupFocusMode: RepositoryPortfolioFocusMode.Attention,
                    PreferredStartupSearchText: "Repo.Alpha",
                    PreferredStartupFamilyKey: "alpha",
                    PreferredStartupFamilyDisplayName: "Alpha",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            workspaceSnapshotService: new FakeWorkspaceSnapshotService(snapshot),
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.InitializeAsync();

        Assert.Equal(RepositoryPortfolioFocusMode.Attention, viewModel.PortfolioOverview.SelectedFocus.Mode);
        Assert.Equal("Repo.Alpha", viewModel.PortfolioOverview.SearchText);
        Assert.Single(viewModel.Repositories);
        Assert.Equal("Repo.Alpha", viewModel.Repositories[0].Name);
        Assert.Contains("focus Attention, search 'Repo.Alpha', family 'Alpha'", viewModel.StatusText);
    }

    [Fact]
    public async Task InitializeAsync_LayersProfileStartupPreferenceOnSavedViewWhenConfigured()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", Path.Combine(workspaceRoot, "Repo.Alpha"), RepositoryReadinessKind.Attention, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Beta", Path.Combine(workspaceRoot, "Repo.Beta"), RepositoryReadinessKind.Ready, familyKey: "beta", familyName: "Beta")
        ]);
        var persistence = new FakePortfolioViewStatePersistenceService([
            new RepositoryPortfolioSavedView(
                "ready-focus",
                "Ready Focus",
                new RepositoryPortfolioViewState(
                    PresetKey: "ready-today",
                    FocusMode: RepositoryPortfolioFocusMode.Ready,
                    SearchText: string.Empty,
                    FamilyKey: "beta",
                    UpdatedAtUtc: DateTimeOffset.UtcNow))
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: workspaceRoot,
            RecentWorkspaceRoots: [workspaceRoot],
            ActiveProfileId: "modules",
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Saved view plus startup emphasis desk",
                    TodayNote: "Reopen Beta view, then pivot back to Alpha attention.",
                    LastLaunchResult: null,
                    LaunchHistory: [],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: "ready-focus",
                    QueueScopeKey: "alpha",
                    QueueScopeDisplayName: "Alpha",
                    PreferredStartupFocusMode: RepositoryPortfolioFocusMode.Attention,
                    PreferredStartupSearchText: "Repo.Alpha",
                    PreferredStartupFamilyKey: "alpha",
                    PreferredStartupFamilyDisplayName: "Alpha",
                    ApplyStartupPreferenceAfterSavedView: true,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            persistenceService: persistence,
            workspaceSnapshotService: new FakeWorkspaceSnapshotService(snapshot),
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.InitializeAsync();

        Assert.Equal("Ready Focus", viewModel.PortfolioOverview.SelectedSavedView?.DisplayName);
        Assert.Equal(RepositoryPortfolioFocusMode.Attention, viewModel.PortfolioOverview.SelectedFocus.Mode);
        Assert.Equal("Repo.Alpha", viewModel.PortfolioOverview.SearchText);
        Assert.Single(viewModel.Repositories);
        Assert.Equal("Repo.Alpha", viewModel.Repositories[0].Name);
        Assert.Contains("saved view 'Ready Focus' and startup emphasis", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("saved view + startup emphasis", viewModel.PortfolioOverview.ActiveWorkspaceContextDetails, StringComparison.OrdinalIgnoreCase);
        var heroCard = Assert.Single(viewModel.PortfolioOverview.WorkspaceProfileCards);
        Assert.Contains("saved view + startup emphasis", heroCard.StartupStrategyLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteWorkspaceProfileCommand_RemovesActiveProfile()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Ready", @"C:\Support\GitHub\Repo.Ready", RepositoryReadinessKind.Ready)
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: @"C:\Support\GitHub",
            RecentWorkspaceRoots: [@"C:\Support\GitHub"],
            ActiveProfileId: "modules",
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Module estate",
                    TodayNote: "Keep the lane tidy.",
                    LastLaunchResult: null,
                    LaunchHistory: [],
                    WorkspaceRoot: @"C:\Support\GitHub",
                    SavedViewId: null,
                    QueueScopeKey: null,
                    QueueScopeDisplayName: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(snapshot, workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        viewModel.PortfolioOverview.SelectedWorkspaceProfile = Assert.Single(viewModel.PortfolioOverview.WorkspaceProfiles);
        viewModel.DeleteWorkspaceProfileCommand.Execute(null);
        await WaitForConditionAsync(() => viewModel.PortfolioOverview.WorkspaceProfiles.Count == 0);

        Assert.Empty(viewModel.PortfolioOverview.WorkspaceProfiles);
        Assert.Null(rootCatalogService.Catalog.ActiveProfileId);
        Assert.Equal("Deleted workspace profile 'Modules'.", viewModel.StatusText);
    }

    [Fact]
    public async Task PrepareWorkspaceProfileQueueCommand_UsesProfileFamilyScope()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha", RepositoryReadinessKind.Ready, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Alpha.Worktree", @"C:\Support\GitHub\Repo.Alpha.Worktree", RepositoryReadinessKind.Ready, workspaceKind: ReleaseWorkspaceKind.Worktree, familyKey: "alpha", familyName: "Alpha"),
            CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta", RepositoryReadinessKind.Ready, familyKey: "beta", familyName: "Beta")
        ]);
        var familyQueueActionService = new FakeFamilyQueueActionService(
            prepareResult: new FamilyQueueActionResult("Prepared the Alpha family queue."));
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: @"C:\Support\GitHub",
            RecentWorkspaceRoots: [@"C:\Support\GitHub"],
            ActiveProfileId: null,
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Alpha release lane",
                    TodayNote: "Prepare Alpha first.",
                    LastLaunchResult: null,
                    LaunchHistory: [],
                    WorkspaceRoot: @"C:\Support\GitHub",
                    SavedViewId: null,
                    QueueScopeKey: "repoalpha",
                    QueueScopeDisplayName: "Repo.Alpha",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            familyQueueActionService: familyQueueActionService,
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        viewModel.PortfolioOverview.SelectedWorkspaceProfile = Assert.Single(viewModel.PortfolioOverview.WorkspaceProfiles);
        Assert.True(viewModel.PrepareWorkspaceProfileQueueCommand.CanExecute(null));

        viewModel.PrepareWorkspaceProfileQueueCommand.Execute(null);

        var invocation = await familyQueueActionService.WaitForPrepareAsync();
        Assert.Equal("repoalpha", invocation.Family?.FamilyKey);
        Assert.Equal(3, invocation.PortfolioCount);
        Assert.Equal("Prepared the Alpha family queue.", viewModel.StatusText);
    }

    [Fact]
    public async Task ApplyWorkspaceProfileCardCommand_ActivatesProfileFromHeroCard()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var executedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-15);
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Ready", Path.Combine(workspaceRoot, "Repo.Ready"), RepositoryReadinessKind.Ready, familyKey: "repoready", familyName: "Repo.Ready")
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: workspaceRoot,
            RecentWorkspaceRoots: [workspaceRoot],
            ActiveProfileId: null,
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Daily module lane",
                    TodayNote: "Start from the ready board.",
                    LastLaunchResult: new WorkspaceProfileLaunchResult(
                        WorkspaceProfileLaunchActionKind.ApplySavedView,
                        "Apply Saved View",
                        true,
                        "Applied saved portfolio view 'Ready Board'.",
                        executedAtUtc),
                    LaunchHistory: [
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.ApplySavedView,
                            "Apply Saved View",
                            true,
                            "Applied saved portfolio view 'Ready Board'.",
                            executedAtUtc)
                    ],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: null,
                    QueueScopeKey: null,
                    QueueScopeDisplayName: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var snapshotService = new FakeWorkspaceSnapshotService(snapshot);
        var viewModel = CreateViewModel(
            snapshot,
            workspaceSnapshotService: snapshotService,
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        var card = Assert.Single(viewModel.PortfolioOverview.WorkspaceProfileCards);
        viewModel.ApplyWorkspaceProfileCardCommand.Execute(card);
        await WaitForConditionAsync(() => string.Equals(viewModel.PortfolioOverview.SelectedWorkspaceProfile?.ProfileId, "modules", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("Modules", viewModel.PortfolioOverview.SelectedWorkspaceProfile?.DisplayName);
        Assert.Equal("Active profile: Modules", viewModel.PortfolioOverview.ActiveWorkspaceContextHeadline);
        Assert.Equal(WorkspaceProfileHeroRouteKind.OpenDesk, card.RouteKind);
        Assert.Equal("Open Desk", card.RouteLabel);
        Assert.Equal(1, snapshotService.RefreshCount);
    }

    [Fact]
    public async Task ApplyWorkspaceProfileCardCommand_RefreshRouteRescansWorkspaceForStaleDesk()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var staleAtUtc = DateTimeOffset.UtcNow.AddHours(-5);
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Stale", Path.Combine(workspaceRoot, "Repo.Stale"), RepositoryReadinessKind.Ready, familyKey: "repostale", familyName: "Repo.Stale")
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: workspaceRoot,
            RecentWorkspaceRoots: [workspaceRoot],
            ActiveProfileId: null,
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Stale desk",
                    TodayNote: "Refresh before releasing.",
                    LastLaunchResult: new WorkspaceProfileLaunchResult(
                        WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                        "Refresh Workspace",
                        true,
                        "Portfolio ready.",
                        staleAtUtc),
                    LaunchHistory: [
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                            "Refresh Workspace",
                            true,
                            "Portfolio ready.",
                            staleAtUtc)
                    ],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: null,
                    QueueScopeKey: "repostale",
                    QueueScopeDisplayName: "Repo.Stale",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var snapshotService = new FakeWorkspaceSnapshotService(snapshot);
        var viewModel = CreateViewModel(
            snapshot,
            workspaceSnapshotService: snapshotService,
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        var card = Assert.Single(viewModel.PortfolioOverview.WorkspaceProfileCards);
        Assert.Equal("Stale Today", card.HealthLabel);
        Assert.Equal(WorkspaceProfileHeroRouteKind.RefreshDesk, card.RouteKind);
        Assert.Equal("Refresh Desk", card.RouteLabel);

        viewModel.ApplyWorkspaceProfileCardCommand.Execute(card);
        await WaitForConditionAsync(() =>
            string.Equals(viewModel.PortfolioOverview.SelectedWorkspaceProfile?.ProfileId, "modules", StringComparison.OrdinalIgnoreCase)
            && snapshotService.RefreshCount >= 2
            && (viewModel.PortfolioOverview.ActiveWorkspaceReceiptDetails?.Contains(
                "Refreshed workspace profile 'Modules'",
                StringComparison.Ordinal) ?? false));

        Assert.Equal(2, snapshotService.RefreshCount);
        Assert.Equal("Modules", viewModel.PortfolioOverview.SelectedWorkspaceProfile?.DisplayName);
        Assert.Equal("Active profile: Modules", viewModel.PortfolioOverview.ActiveWorkspaceContextHeadline);
        Assert.StartsWith("Fresh as of ", viewModel.PortfolioOverview.ActiveWorkspaceReceiptHeadline);
        Assert.Contains("Refreshed workspace profile 'Modules'", viewModel.PortfolioOverview.ActiveWorkspaceReceiptDetails);
        Assert.Equal(WorkspaceProfileLaunchActionKind.RefreshWorkspace, rootCatalogService.Catalog.Profiles.Single().LastLaunchResult?.ActionKind);
        Assert.Equal(WorkspaceProfileLaunchActionKind.PrepareQueue, viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.Kind);
        Assert.Equal("Pending", viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.LastRunLabel);
    }

    [Fact]
    public async Task ApplyWorkspaceProfileCardCommand_ResumeRouteAppliesAttentionDeskContext()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var failedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-25);
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Attention", Path.Combine(workspaceRoot, "Repo.Attention"), RepositoryReadinessKind.Attention, familyKey: "repoattention", familyName: "Repo.Attention"),
            CreateRepository("Repo.Ready", Path.Combine(workspaceRoot, "Repo.Ready"), RepositoryReadinessKind.Ready, familyKey: "repoready", familyName: "Repo.Ready")
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: workspaceRoot,
            RecentWorkspaceRoots: [workspaceRoot],
            ActiveProfileId: null,
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Attention desk",
                    TodayNote: "Recover the failed queue first.",
                    LastLaunchResult: new WorkspaceProfileLaunchResult(
                        WorkspaceProfileLaunchActionKind.PrepareQueue,
                        "Prepare Queue",
                        false,
                        "No portfolio items were available for the selected family.",
                        failedAtUtc),
                    LaunchHistory: [
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.PrepareQueue,
                            "Prepare Queue",
                            false,
                            "No portfolio items were available for the selected family.",
                            failedAtUtc)
                    ],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: null,
                    QueueScopeKey: "repoattention",
                    QueueScopeDisplayName: "Repo.Attention",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            workspaceSnapshotService: new FakeWorkspaceSnapshotService(snapshot),
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        var card = Assert.Single(viewModel.PortfolioOverview.WorkspaceProfileCards);
        Assert.Equal(WorkspaceProfileHeroRouteKind.ResumeDesk, card.RouteKind);

        viewModel.ApplyWorkspaceProfileCardCommand.Execute(card);
        await WaitForConditionAsync(() =>
            string.Equals(viewModel.PortfolioOverview.SelectedWorkspaceProfile?.ProfileId, "modules", StringComparison.OrdinalIgnoreCase)
            && viewModel.PortfolioOverview.SelectedFocus.Mode == RepositoryPortfolioFocusMode.Attention);

        Assert.Single(viewModel.Repositories);
        Assert.Equal("Repo.Attention", viewModel.Repositories[0].Name);
        Assert.StartsWith("Repo.Attention:", viewModel.RepositoryFamily.Headline);
        Assert.Contains("attention focus", viewModel.StatusText);
        Assert.Contains("queue scope 'Repo.Attention'", viewModel.StatusText);
        Assert.Equal(WorkspaceProfileLaunchActionKind.OpenAttentionView, viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.Kind);
        Assert.Equal("Pending", viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.LastRunLabel);
    }

    [Fact]
    public async Task ApplyWorkspaceProfileLaunchActionCommand_PreparesQueueFromLaunchBoard()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha", RepositoryReadinessKind.Ready, familyKey: "repoalpha", familyName: "Repo.Alpha"),
            CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta", RepositoryReadinessKind.Ready, familyKey: "repobeta", familyName: "Repo.Beta")
        ]);
        var familyQueueActionService = new FakeFamilyQueueActionService(
            prepareResult: new FamilyQueueActionResult(
                "Prepared the Alpha family queue.",
                new ReleaseQueueCommandResult(
                    true,
                    "Prepared the Alpha family queue.",
                    new ReleaseQueueSession(
                        SessionId: Guid.NewGuid().ToString("N"),
                        WorkspaceRoot: @"C:\Support\GitHub",
                        CreatedAtUtc: DateTimeOffset.UtcNow,
                        Summary: new ReleaseQueueSummary(1, 0, 0, 0, 0, 0),
                        Items: []),
                    [],
                    [],
                    [])));
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: @"C:\Support\GitHub",
            RecentWorkspaceRoots: [@"C:\Support\GitHub"],
            ActiveProfileId: "modules",
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Alpha release lane",
                    TodayNote: "Prepare Alpha first.",
                    LastLaunchResult: null,
                    LaunchHistory: [],
                    WorkspaceRoot: @"C:\Support\GitHub",
                    SavedViewId: null,
                    QueueScopeKey: "repoalpha",
                    QueueScopeDisplayName: "Repo.Alpha",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            familyQueueActionService: familyQueueActionService,
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.RefreshAsync(forceRefresh: true);

        var launchAction = Assert.Single(
            viewModel.PortfolioOverview.ActiveWorkspaceLaunchActions,
            action => action.Kind == WorkspaceProfileLaunchActionKind.PrepareQueue);

        viewModel.ApplyWorkspaceProfileLaunchActionCommand.Execute(launchAction);

        var invocation = await familyQueueActionService.WaitForPrepareAsync();
        Assert.Equal("repoalpha", invocation.Family?.FamilyKey);
        Assert.Equal(2, invocation.PortfolioCount);
        Assert.Equal("Prepared the Alpha family queue.", viewModel.StatusText);
        Assert.Equal(WorkspaceProfileLaunchActionKind.PrepareQueue, rootCatalogService.Catalog.Profiles.Single().LastLaunchResult?.ActionKind);
        Assert.Equal("Prepared the Alpha family queue.", rootCatalogService.Catalog.Profiles.Single().LastLaunchResult?.Summary);
        Assert.Contains("Last run:", viewModel.PortfolioOverview.ActiveWorkspaceLaunchBoardDetails);
        Assert.Equal("Prepare Queue", viewModel.PortfolioOverview.ActiveWorkspaceLaunchTimeline[0].Title);
        Assert.Equal("Prepare Queue", Assert.Single(rootCatalogService.Catalog.Profiles.Single().LaunchHistory).ActionTitle);
        Assert.Contains(
            "Succeeded",
            Assert.Single(
                viewModel.PortfolioOverview.ActiveWorkspaceLaunchActions,
                candidate => candidate.Kind == WorkspaceProfileLaunchActionKind.PrepareQueue).LastRunLabel);
    }

    [Fact]
    public async Task ApplyWorkspaceProfileLaunchActionCommand_RetriesFailedFamilyFromReceiptAction()
    {
        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(0, 0, 0, 0, 0, 1),
            Items: [
                new ReleaseQueueItem(
                    RootPath: @"C:\Support\GitHub\Repo.Alpha",
                    RepositoryName: "Repo.Alpha",
                    RepositoryKind: ReleaseRepositoryKind.Module,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Build,
                    Status: ReleaseQueueItemStatus.Failed,
                    Summary: "Build failed.",
                    CheckpointKey: "build.failed",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);
        var snapshot = CreateSnapshot(
            portfolioItems: [
                CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha", RepositoryReadinessKind.Attention, familyKey: "repoalpha", familyName: "Repo.Alpha"),
                CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta", RepositoryReadinessKind.Ready, familyKey: "repobeta", familyName: "Repo.Beta")
            ],
            queueSession: queueSession);
        var familyQueueActionService = new FakeFamilyQueueActionService(
            retryResult: new FamilyQueueActionResult(
                "Retried failed items for Repo.Alpha.",
                new ReleaseQueueCommandResult(
                    true,
                    "Retried failed items for Repo.Alpha.",
                    queueSession,
                    [],
                    [],
                    [])));
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: @"C:\Support\GitHub",
            RecentWorkspaceRoots: [@"C:\Support\GitHub"],
            ActiveProfileId: "modules",
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Attention desk",
                    TodayNote: "Recover failed builds first.",
                    LastLaunchResult: new WorkspaceProfileLaunchResult(
                        WorkspaceProfileLaunchActionKind.PrepareQueue,
                        "Prepare Queue",
                        false,
                        "Preparation failed for Repo.Alpha.",
                        DateTimeOffset.UtcNow.AddMinutes(-10)),
                    LaunchHistory: [
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.PrepareQueue,
                            "Prepare Queue",
                            false,
                            "Preparation failed for Repo.Alpha.",
                            DateTimeOffset.UtcNow.AddMinutes(-10))
                    ],
                    WorkspaceRoot: @"C:\Support\GitHub",
                    SavedViewId: null,
                    QueueScopeKey: "repoalpha",
                    QueueScopeDisplayName: "Repo.Alpha",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            familyQueueActionService: familyQueueActionService,
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.InitializeAsync();

        var receiptAction = viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction;
        Assert.NotNull(receiptAction);
        Assert.Equal(WorkspaceProfileLaunchActionKind.RetryFailedFamily, receiptAction!.Kind);

        viewModel.ApplyWorkspaceProfileLaunchActionCommand.Execute(receiptAction);

        var invocation = await familyQueueActionService.WaitForRetryAsync();
        Assert.Equal("repoalpha", invocation.Family?.FamilyKey);
        Assert.Equal("Retried failed items for Repo.Alpha.", viewModel.StatusText);
        Assert.Equal(WorkspaceProfileLaunchActionKind.RetryFailedFamily, rootCatalogService.Catalog.Profiles.Single().LastLaunchResult?.ActionKind);
        Assert.Equal("Retried failed items for Repo.Alpha.", rootCatalogService.Catalog.Profiles.Single().LastLaunchResult?.Summary);
        Assert.StartsWith("Retry started at ", viewModel.PortfolioOverview.ActiveWorkspaceReceiptHeadline);
        Assert.Equal(WorkspaceProfileLaunchActionKind.RetryFailedFamily, viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.Kind);
        Assert.StartsWith("Completed ", viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.LastRunLabel);
        Assert.False(viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.CanExecute);
    }

    [Fact]
    public async Task InitializeAsync_ShowsNeedsAttentionHealthWhenTodayIncludesFailure()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var failedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20);
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Attention", Path.Combine(workspaceRoot, "Repo.Attention"), RepositoryReadinessKind.Attention, familyKey: "repoattention", familyName: "Repo.Attention")
        ]);
        var rootCatalogService = new FakeWorkspaceRootCatalogService(new WorkspaceRootCatalog(
            ActiveWorkspaceRoot: workspaceRoot,
            RecentWorkspaceRoots: [workspaceRoot],
            ActiveProfileId: "modules",
            Profiles: [
                new WorkspaceProfile(
                    ProfileId: "modules",
                    DisplayName: "Modules",
                    Description: "Attention desk",
                    TodayNote: "Recover the failed queue before publishing.",
                    LastLaunchResult: new WorkspaceProfileLaunchResult(
                        WorkspaceProfileLaunchActionKind.PrepareQueue,
                        "Prepare Queue",
                        false,
                        "No portfolio items were available for the selected family.",
                        failedAtUtc),
                    LaunchHistory: [
                        new WorkspaceProfileLaunchResult(
                            WorkspaceProfileLaunchActionKind.PrepareQueue,
                            "Prepare Queue",
                            false,
                            "No portfolio items were available for the selected family.",
                            failedAtUtc)
                    ],
                    WorkspaceRoot: workspaceRoot,
                    SavedViewId: null,
                    QueueScopeKey: "repoattention",
                    QueueScopeDisplayName: "Repo.Attention",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]));
        var viewModel = CreateViewModel(
            snapshot,
            workspaceSnapshotService: new FakeWorkspaceSnapshotService(snapshot),
            workspaceRootCatalogService: rootCatalogService);

        await viewModel.InitializeAsync();

        Assert.Equal("Desk health: Needs Attention", viewModel.PortfolioOverview.ActiveWorkspaceHealthHeadline);
        Assert.Contains("At least one launch action failed today.", viewModel.PortfolioOverview.ActiveWorkspaceHealthDetails);
        var attentionCard = Assert.Single(viewModel.PortfolioOverview.WorkspaceProfileCards);
        Assert.Equal("Needs Attention", attentionCard.HealthLabel);
        Assert.Equal(WorkspaceProfileHeroRouteKind.ResumeDesk, attentionCard.RouteKind);
        Assert.Equal("Resume Desk", attentionCard.RouteLabel);
        Assert.StartsWith("Needs attention since ", viewModel.PortfolioOverview.ActiveWorkspaceReceiptHeadline);
        Assert.Contains("No portfolio items were available for the selected family.", viewModel.PortfolioOverview.ActiveWorkspaceReceiptDetails);
        Assert.Equal(WorkspaceProfileLaunchActionKind.OpenAttentionView, viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.Kind);
        Assert.Equal("Pending", viewModel.PortfolioOverview.ActiveWorkspaceReceiptAction?.LastRunLabel);
    }

    [Fact]
    public async Task SavePortfolioViewCommand_PersistsNamedViewAndReloadsSavedViews()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Ready", @"C:\Support\GitHub\Repo.Ready", RepositoryReadinessKind.Ready, familyKey: "ready", familyName: "Ready")
        ]);
        var persistence = new FakePortfolioViewStatePersistenceService();
        var viewModel = CreateViewModel(snapshot, persistenceService: persistence);

        await viewModel.RefreshAsync(forceRefresh: true);

        viewModel.PortfolioOverview.SavedViewDraftName = "Ready Candidates";
        viewModel.SavePortfolioViewCommand.Execute(null);

        var persistedNamedView = await persistence.WaitForNamedPersistAsync();
        await WaitForConditionAsync(() => viewModel.PortfolioOverview.SelectedSavedView is not null);
        Assert.Equal("ready-candidates", persistedNamedView.ViewId);
        Assert.Equal("Ready Candidates", persistedNamedView.DisplayName);
        Assert.Equal("Ready Candidates", viewModel.PortfolioOverview.SelectedSavedView?.DisplayName);
        Assert.Equal("Saved portfolio view 'Ready Candidates'.", viewModel.StatusText);
    }

    [Fact]
    public async Task ApplySavedPortfolioViewCommand_RestoresSelectedNamedView()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Attention", @"C:\Support\GitHub\Repo.Attention", RepositoryReadinessKind.Attention, familyKey: "attention", familyName: "Attention"),
            CreateRepository("Repo.Ready", @"C:\Support\GitHub\Repo.Ready", RepositoryReadinessKind.Ready, familyKey: "ready", familyName: "Ready")
        ]);
        var persistence = new FakePortfolioViewStatePersistenceService([
            new RepositoryPortfolioSavedView(
                "attention-focus",
                "Attention Focus",
                new RepositoryPortfolioViewState(
                    PresetKey: "attention",
                    FocusMode: RepositoryPortfolioFocusMode.Attention,
                    SearchText: "Attention",
                    FamilyKey: "attention",
                    UpdatedAtUtc: DateTimeOffset.UtcNow))
        ]);
        var viewModel = CreateViewModel(snapshot, persistenceService: persistence);

        await viewModel.RefreshAsync(forceRefresh: true);

        viewModel.PortfolioOverview.SelectedSavedView = Assert.Single(viewModel.PortfolioOverview.SavedViews);
        viewModel.ApplySavedPortfolioViewCommand.Execute(null);

        Assert.Equal(RepositoryPortfolioFocusMode.Attention, viewModel.PortfolioOverview.SelectedFocus.Mode);
        Assert.Equal("Attention", viewModel.PortfolioOverview.SearchText);
        Assert.Single(viewModel.Repositories);
        Assert.Equal("Repo.Attention", viewModel.Repositories[0].Name);
        Assert.Equal("Applied saved portfolio view 'Attention Focus'.", viewModel.StatusText);
    }

    [Fact]
    public async Task DeleteSavedPortfolioViewCommand_RemovesSelectedView()
    {
        var snapshot = CreateSnapshot(portfolioItems: [
            CreateRepository("Repo.Ready", @"C:\Support\GitHub\Repo.Ready", RepositoryReadinessKind.Ready)
        ]);
        var persistence = new FakePortfolioViewStatePersistenceService([
            new RepositoryPortfolioSavedView(
                "ready-candidates",
                "Ready Candidates",
                new RepositoryPortfolioViewState(
                    PresetKey: "ready-today",
                    FocusMode: RepositoryPortfolioFocusMode.Ready,
                    SearchText: string.Empty,
                    FamilyKey: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow))
        ]);
        var viewModel = CreateViewModel(snapshot, persistenceService: persistence);

        await viewModel.RefreshAsync(forceRefresh: true);

        viewModel.PortfolioOverview.SelectedSavedView = Assert.Single(viewModel.PortfolioOverview.SavedViews);
        viewModel.DeleteSavedPortfolioViewCommand.Execute(null);
        await persistence.WaitForDeleteAsync();
        await WaitForConditionAsync(() => viewModel.PortfolioOverview.SavedViews.Count == 0);

        Assert.Empty(viewModel.PortfolioOverview.SavedViews);
        Assert.Null(viewModel.PortfolioOverview.SelectedSavedView);
        Assert.Equal("Deleted portfolio view 'Ready Candidates'.", viewModel.StatusText);
    }

    private static ShellViewModel CreateViewModel(
        WorkspaceSnapshot snapshot,
        IPortfolioViewStatePersistenceService? persistenceService = null,
        TimeSpan? saveDelay = null,
        IFamilyQueueActionService? familyQueueActionService = null,
        IRepositoryGitQuickActionWorkflowService? gitQuickActionWorkflowService = null,
        IWorkspaceSnapshotService? workspaceSnapshotService = null,
        IWorkspaceRootCatalogService? workspaceRootCatalogService = null)
    {
        var queueCommandService = new FakeReleaseQueueCommandService();
        return new(new ShellViewModelServices(
            PortfolioFocusService: new RepositoryPortfolioFocusService(),
            WorkspaceFamilyService: new RepositoryWorkspaceFamilyService(),
            ReleaseInboxService: new RepositoryReleaseInboxService(),
            RepositoryDetailService: new RepositoryDetailService(),
            WorkspaceSnapshotService: workspaceSnapshotService ?? new FakeWorkspaceSnapshotService(snapshot),
            StationProjectionService: new ReleaseStationProjectionService(),
            QueueCommandService: queueCommandService) {
                PortfolioViewStatePersistenceService = persistenceService ?? new FakePortfolioViewStatePersistenceService(),
                PortfolioViewStateSaveDelay = saveDelay ?? TimeSpan.FromMilliseconds(350),
                FamilyQueueActionService = familyQueueActionService ?? new FamilyQueueActionService(queueCommandService),
                GitQuickActionWorkflowService = gitQuickActionWorkflowService ?? new RepositoryGitQuickActionWorkflowService(),
                WorkspaceRootCatalogService = workspaceRootCatalogService ?? new FakeWorkspaceRootCatalogService()
            });
    }

    private static WorkspaceSnapshot CreateSnapshot(
        IReadOnlyList<RepositoryPortfolioItem>? portfolioItems = null,
        IReadOnlyList<RepositoryReleaseInboxItem>? releaseInboxItems = null,
        RepositoryPortfolioSummary? summary = null,
        ReleaseQueueSession? queueSession = null,
        RepositoryPortfolioViewState? savedPortfolioView = null)
    {
        var repositories = portfolioItems ?? [];
        var queue = queueSession ?? new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(0, 0, 0, 0, 0, 0),
            Items: []);
        var familyService = new RepositoryWorkspaceFamilyService();
        var annotated = familyService.AnnotateFamilies(repositories);
        var families = familyService.BuildFamilies(annotated, queue);
        var lanes = familyService.BuildFamilyLanes(annotated, queue);
        var selectedSummary = summary ?? new RepositoryPortfolioSummary(annotated.Count, annotated.Count(item => item.ReadinessKind == RepositoryReadinessKind.Ready), annotated.Count(item => item.ReadinessKind == RepositoryReadinessKind.Attention), 0, 0, 0, annotated.Count(item => item.WorkspaceKind == ReleaseWorkspaceKind.Worktree), annotated.Count(item => item.GitHubInbox?.Status == RepositoryGitHubInboxStatus.Attention), annotated.Sum(item => item.GitHubInbox?.OpenPullRequestCount ?? 0), annotated.Count(item => item.ReleaseDrift?.Status == RepositoryReleaseDriftStatus.Attention));

        return new WorkspaceSnapshot(
            WorkspaceRoot: @"C:\Support\GitHub",
            DatabasePath: @"C:\Support\GitHub\_state\powerforgestudio.db",
            BuildEngineResolution: new PSPublishModuleResolution(
                PSPublishModuleResolutionSource.RepositoryManifest,
                @"C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1",
                "1.0.0",
                true),
            Summary: selectedSummary,
            PortfolioItems: repositories,
            ReleaseInboxItems: releaseInboxItems ?? [],
            DashboardCards: [],
            RepositoryFamilies: families,
            RepositoryFamilyLanes: lanes,
            QueueSession: queue,
            SigningStation: new StationSnapshot<ReleaseSigningArtifact>("No signing batch waiting.", "None.", []),
            SigningReceipts: [],
            SigningReceiptBatch: new ReceiptBatchSnapshot<ReleaseSigningReceipt>("No signing receipts yet.", "None.", []),
            PublishStation: new StationSnapshot<ReleasePublishTarget>("No publish batch ready.", "None.", []),
            PublishReceipts: [],
            PublishReceiptBatch: new ReceiptBatchSnapshot<ReleasePublishReceipt>("No publish receipts yet.", "None.", []),
            VerificationStation: new StationSnapshot<ReleaseVerificationTarget>("No verification batch ready.", "None.", []),
            VerificationReceipts: [],
            VerificationReceiptBatch: new ReceiptBatchSnapshot<ReleaseVerificationReceipt>("No verification receipts yet.", "None.", []),
            GitQuickActionReceipts: [],
            SavedPortfolioView: savedPortfolioView);
    }

    private static RepositoryPortfolioItem CreateRepository(
        string name,
        string rootPath,
        RepositoryReadinessKind readinessKind,
        ReleaseWorkspaceKind workspaceKind = ReleaseWorkspaceKind.PrimaryRepository,
        string? familyKey = null,
        string? familyName = null,
        RepositoryGitHubInbox? gitHubInbox = null,
        RepositoryReleaseDrift? releaseDrift = null)
        => new(
            new RepositoryCatalogEntry(
                Name: name,
                RootPath: rootPath,
                RepositoryKind: ReleaseRepositoryKind.Module,
                WorkspaceKind: workspaceKind,
                ModuleBuildScriptPath: Path.Combine(rootPath, "Build", "Build-Module.ps1"),
                ProjectBuildScriptPath: null,
                IsWorktree: workspaceKind == ReleaseWorkspaceKind.Worktree,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: "main",
                UpstreamBranch: "origin/main",
                AheadCount: 0,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0),
            new RepositoryReadiness(readinessKind, readinessKind.ToString()),
            PlanResults: [],
            GitHubInbox: gitHubInbox,
            ReleaseDrift: releaseDrift,
            WorkspaceFamilyKey: familyKey,
            WorkspaceFamilyName: familyName);

    private static void SetIsInitialized(ShellViewModel viewModel, bool value)
    {
        var field = typeof(ShellViewModel).GetField("_isInitialized", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, value);
    }

    private static async Task WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollingInterval = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var effectivePollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(50);
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - startedAt > effectiveTimeout)
            {
                throw new TimeoutException("Condition was not met within the expected time.");
            }

            await Task.Delay(effectivePollingInterval).ConfigureAwait(false);
        }
    }

    private sealed class FakeWorkspaceSnapshotService(WorkspaceSnapshot snapshot) : IWorkspaceSnapshotService
    {
        public string? LastWorkspaceRoot { get; private set; }

        public string? LastDatabasePath { get; private set; }

        public int RefreshCount { get; private set; }

        public Task<WorkspaceSnapshot> RefreshAsync(string workspaceRoot, string databasePath, WorkspaceRefreshOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastWorkspaceRoot = workspaceRoot;
            LastDatabasePath = databasePath;
            RefreshCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeReleaseQueueCommandService : IReleaseQueueCommandService
    {
        public Task<ReleaseQueueCommandResult> RunNextReadyItemAsync(string databasePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReleaseQueueCommandResult(false, "run-next", null, [], [], []));

        public Task<ReleaseQueueCommandResult> ApproveUsbAsync(string databasePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReleaseQueueCommandResult(false, "approve-usb", null, [], [], []));

        public Task<ReleaseQueueCommandResult> RetryFailedAsync(string databasePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReleaseQueueCommandResult(false, "retry-all", null, [], [], []));

        public Task<ReleaseQueueCommandResult> RetryFailedAsync(string databasePath, Func<ReleaseQueueItem, bool> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReleaseQueueCommandResult(false, "retry-filtered", null, [], [], []));

        public Task<ReleaseQueueCommandResult> PrepareQueueAsync(string databasePath, string workspaceRoot, IReadOnlyList<RepositoryPortfolioItem> portfolioItems, string? scopeKey = null, string? scopeDisplayName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReleaseQueueCommandResult(false, "prepare", null, [], [], []));
    }

    private sealed class FakePortfolioViewStatePersistenceService : IPortfolioViewStatePersistenceService
    {
        private readonly TaskCompletionSource<RepositoryPortfolioViewState> _persistedState = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<(string ViewId, string DisplayName, RepositoryPortfolioViewState State)> _namedPersistedState = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _deletedView = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<RepositoryPortfolioSavedView> _savedViews;

        public FakePortfolioViewStatePersistenceService(IReadOnlyList<RepositoryPortfolioSavedView>? savedViews = null)
        {
            _savedViews = savedViews?.ToList() ?? [];
        }

        public Task PersistAsync(
            string databasePath,
            RepositoryPortfolioViewState state,
            string viewId = "default",
            string? displayName = null,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(viewId, PortfolioViewStateService.DefaultViewId, StringComparison.OrdinalIgnoreCase))
            {
                _persistedState.TrySetResult(state);
                return Task.CompletedTask;
            }

            var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? viewId : displayName;
            _savedViews.RemoveAll(savedView => string.Equals(savedView.ViewId, viewId, StringComparison.OrdinalIgnoreCase));
            _savedViews.Add(new RepositoryPortfolioSavedView(viewId, normalizedDisplayName, state));
            _namedPersistedState.TrySetResult((viewId, normalizedDisplayName, state));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RepositoryPortfolioSavedView>> ListSavedViewsAsync(string databasePath, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RepositoryPortfolioSavedView>>(_savedViews.OrderByDescending(view => view.State.UpdatedAtUtc).ToArray());

        public Task DeleteAsync(string databasePath, string viewId, CancellationToken cancellationToken = default)
        {
            _savedViews.RemoveAll(savedView => string.Equals(savedView.ViewId, viewId, StringComparison.OrdinalIgnoreCase));
            _deletedView.TrySetResult(viewId);
            return Task.CompletedTask;
        }

        public async Task<RepositoryPortfolioViewState> WaitForPersistAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var registration = timeout.Token.Register(() => _persistedState.TrySetCanceled(timeout.Token));
            return await _persistedState.Task.ConfigureAwait(false);
        }

        public async Task<(string ViewId, string DisplayName, RepositoryPortfolioViewState State)> WaitForNamedPersistAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var registration = timeout.Token.Register(() => _namedPersistedState.TrySetCanceled(timeout.Token));
            return await _namedPersistedState.Task.ConfigureAwait(false);
        }

        public async Task<string> WaitForDeleteAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var registration = timeout.Token.Register(() => _deletedView.TrySetCanceled(timeout.Token));
            return await _deletedView.Task.ConfigureAwait(false);
        }
    }

    private sealed class FakeWorkspaceRootCatalogService : IWorkspaceRootCatalogService
    {
        private WorkspaceRootCatalog _catalog;

        public FakeWorkspaceRootCatalogService(WorkspaceRootCatalog? catalog = null)
        {
            _catalog = catalog ?? new WorkspaceRootCatalog(
                ActiveWorkspaceRoot: @"C:\Support\GitHub",
                RecentWorkspaceRoots: [@"C:\Support\GitHub"],
                ActiveProfileId: null,
                Profiles: [],
                Templates: []);
        }

        public List<string> SavedRoots { get; } = [];

        public WorkspaceRootCatalog Catalog => _catalog;

        public WorkspaceRootCatalog Load(string fallbackWorkspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(_catalog.ActiveWorkspaceRoot))
            {
                _catalog = new WorkspaceRootCatalog(fallbackWorkspaceRoot, [fallbackWorkspaceRoot], null, [], []);
            }

            return _catalog;
        }

        public WorkspaceRootCatalog SaveActive(string workspaceRoot, string? activeProfileId = null)
        {
            SavedRoots.Add(workspaceRoot);
            var recentRoots = new List<string> {
                workspaceRoot
            };

            foreach (var recentRoot in _catalog.RecentWorkspaceRoots)
            {
                if (!recentRoots.Contains(recentRoot, StringComparer.OrdinalIgnoreCase))
                {
                    recentRoots.Add(recentRoot);
                }
            }

            _catalog = new WorkspaceRootCatalog(workspaceRoot, recentRoots, activeProfileId, _catalog.Profiles, _catalog.Templates);
            return _catalog;
        }

        public WorkspaceRootCatalog SaveProfile(WorkspaceProfile profile, string? activeProfileId = null)
        {
            var profiles = _catalog.Profiles
                .Where(existing => !string.Equals(existing.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            profiles.Add(profile);
            _catalog = new WorkspaceRootCatalog(
                ActiveWorkspaceRoot: profile.WorkspaceRoot,
                RecentWorkspaceRoots: SaveActive(profile.WorkspaceRoot, activeProfileId).RecentWorkspaceRoots,
                ActiveProfileId: activeProfileId,
                Profiles: profiles.OrderByDescending(existing => existing.UpdatedAtUtc).ToArray(),
                Templates: _catalog.Templates);
            return _catalog;
        }

        public WorkspaceRootCatalog DeleteProfile(string profileId, string fallbackWorkspaceRoot, string? activeProfileId = null)
        {
            var profiles = _catalog.Profiles
                .Where(existing => !string.Equals(existing.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            _catalog = new WorkspaceRootCatalog(
                ActiveWorkspaceRoot: string.IsNullOrWhiteSpace(_catalog.ActiveWorkspaceRoot) ? fallbackWorkspaceRoot : _catalog.ActiveWorkspaceRoot,
                RecentWorkspaceRoots: _catalog.RecentWorkspaceRoots,
                ActiveProfileId: activeProfileId,
                Profiles: profiles,
                Templates: _catalog.Templates);
            return _catalog;
        }

        public WorkspaceRootCatalog SaveTemplate(WorkspaceProfileTemplate template, string fallbackWorkspaceRoot, string? activeProfileId = null)
        {
            var templates = (_catalog.Templates ?? [])
                .Where(existing => !string.Equals(existing.TemplateId, template.TemplateId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            templates.Add(template);
            _catalog = new WorkspaceRootCatalog(
                ActiveWorkspaceRoot: string.IsNullOrWhiteSpace(_catalog.ActiveWorkspaceRoot) ? fallbackWorkspaceRoot : _catalog.ActiveWorkspaceRoot,
                RecentWorkspaceRoots: _catalog.RecentWorkspaceRoots,
                ActiveProfileId: activeProfileId,
                Profiles: _catalog.Profiles,
                Templates: templates.OrderBy(existing => existing.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray());
            return _catalog;
        }

        public WorkspaceRootCatalog DeleteTemplate(string templateId, string fallbackWorkspaceRoot, string? activeProfileId = null)
        {
            var templates = (_catalog.Templates ?? [])
                .Where(existing => !string.Equals(existing.TemplateId, templateId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            _catalog = new WorkspaceRootCatalog(
                ActiveWorkspaceRoot: string.IsNullOrWhiteSpace(_catalog.ActiveWorkspaceRoot) ? fallbackWorkspaceRoot : _catalog.ActiveWorkspaceRoot,
                RecentWorkspaceRoots: _catalog.RecentWorkspaceRoots,
                ActiveProfileId: activeProfileId,
                Profiles: _catalog.Profiles,
                Templates: templates);
            return _catalog;
        }
    }

    private sealed record FamilyQueueActionInvocation(
        string DatabasePath,
        string WorkspaceRoot,
        int PortfolioCount,
        RepositoryWorkspaceFamilySnapshot? Family,
        ReleaseQueueSession? QueueSession);

    private sealed class FakeFamilyQueueActionService : IFamilyQueueActionService
    {
        private readonly FamilyQueueActionResult _prepareResult;
        private readonly FamilyQueueActionResult _retryResult;
        private readonly TaskCompletionSource<FamilyQueueActionInvocation> _prepareInvocation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<FamilyQueueActionInvocation> _retryInvocation = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeFamilyQueueActionService(
            FamilyQueueActionResult? prepareResult = null,
            FamilyQueueActionResult? retryResult = null)
        {
            _prepareResult = prepareResult ?? new FamilyQueueActionResult("Prepare queue called.");
            _retryResult = retryResult ?? new FamilyQueueActionResult("Retry failed called.");
        }

        public bool CanPrepare(bool isBusy, RepositoryWorkspaceFamilySnapshot? family)
            => !isBusy && family is not null;

        public bool CanRetryFailed(
            bool isBusy,
            ReleaseQueueSession? queueSession,
            IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
            RepositoryWorkspaceFamilySnapshot? family)
            => !isBusy && queueSession is not null && family is not null;

        public Task<FamilyQueueActionResult> PrepareQueueAsync(
            string databasePath,
            string workspaceRoot,
            IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
            RepositoryWorkspaceFamilySnapshot? family,
            CancellationToken cancellationToken = default)
        {
            _prepareInvocation.TrySetResult(new FamilyQueueActionInvocation(
                databasePath,
                workspaceRoot,
                portfolioItems.Count,
                family,
                null));
            return Task.FromResult(_prepareResult);
        }

        public Task<FamilyQueueActionResult> RetryFailedAsync(
            string databasePath,
            ReleaseQueueSession? queueSession,
            IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
            RepositoryWorkspaceFamilySnapshot? family,
            CancellationToken cancellationToken = default)
        {
            _retryInvocation.TrySetResult(new FamilyQueueActionInvocation(
                databasePath,
                string.Empty,
                portfolioItems.Count,
                family,
                queueSession));
            return Task.FromResult(_retryResult);
        }

        public async Task<FamilyQueueActionInvocation> WaitForPrepareAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var registration = timeout.Token.Register(() => _prepareInvocation.TrySetCanceled(timeout.Token));
            return await _prepareInvocation.Task.ConfigureAwait(false);
        }

        public async Task<FamilyQueueActionInvocation> WaitForRetryAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var registration = timeout.Token.Register(() => _retryInvocation.TrySetCanceled(timeout.Token));
            return await _retryInvocation.Task.ConfigureAwait(false);
        }
    }

    private sealed record GitQuickActionWorkflowInvocation(
        string DatabasePath,
        string? RepositoryRootPath,
        RepositoryGitQuickAction? Action);

    private sealed class FakeRepositoryGitQuickActionWorkflowService : IRepositoryGitQuickActionWorkflowService
    {
        private readonly RepositoryGitQuickActionWorkflowResult _result;
        private readonly TaskCompletionSource<GitQuickActionWorkflowInvocation> _executeInvocation = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeRepositoryGitQuickActionWorkflowService(RepositoryGitQuickActionWorkflowResult result)
        {
            _result = result;
        }

        public Task<RepositoryGitQuickActionWorkflowResult> ExecuteAsync(
            string databasePath,
            RepositoryPortfolioItem? repository,
            RepositoryGitQuickAction? action,
            CancellationToken cancellationToken = default)
        {
            _executeInvocation.TrySetResult(new GitQuickActionWorkflowInvocation(
                databasePath,
                repository?.RootPath,
                action));
            return Task.FromResult(_result);
        }

        public async Task<GitQuickActionWorkflowInvocation> WaitForExecuteAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var registration = timeout.Token.Register(() => _executeInvocation.TrySetCanceled(timeout.Token));
            return await _executeInvocation.Task.ConfigureAwait(false);
        }
    }
}
