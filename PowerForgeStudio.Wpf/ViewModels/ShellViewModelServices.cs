using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record ShellViewModelServices(
    RepositoryPortfolioFocusService PortfolioFocusService,
    RepositoryWorkspaceFamilyService WorkspaceFamilyService,
    RepositoryReleaseInboxService ReleaseInboxService,
    RepositoryDetailService RepositoryDetailService,
    IWorkspaceSnapshotService WorkspaceSnapshotService,
    ReleaseStationProjectionService StationProjectionService,
    IReleaseQueueCommandService QueueCommandService)
{
    public IShellWorkspaceProjectionService WorkspaceProjectionService { get; init; } = new ShellWorkspaceProjectionService(
        PortfolioFocusService,
        WorkspaceFamilyService,
        ReleaseInboxService);

    public IPortfolioInteractionService PortfolioInteractionService { get; init; } = new PortfolioInteractionService();

    public IFamilyQueueActionService FamilyQueueActionService { get; init; } = new FamilyQueueActionService(QueueCommandService);

    public IRepositoryGitQuickActionWorkflowService GitQuickActionWorkflowService { get; init; } = new RepositoryGitQuickActionWorkflowService();

    public PortfolioViewStateService PortfolioViewStateService { get; init; } = new();

    public IPortfolioViewStatePersistenceService PortfolioViewStatePersistenceService { get; init; } = new PortfolioViewStatePersistenceService();

    public IWorkspaceRootCatalogService WorkspaceRootCatalogService { get; init; } = new WorkspaceRootCatalogService();

    public TimeSpan PortfolioViewStateSaveDelay { get; init; } = TimeSpan.FromMilliseconds(350);

    public static ShellViewModelServices CreateDefault()
        => new(
            PortfolioFocusService: new RepositoryPortfolioFocusService(),
            WorkspaceFamilyService: new RepositoryWorkspaceFamilyService(),
            ReleaseInboxService: new RepositoryReleaseInboxService(),
            RepositoryDetailService: new RepositoryDetailService(),
            WorkspaceSnapshotService: new WorkspaceSnapshotService(),
            StationProjectionService: new ReleaseStationProjectionService(),
            QueueCommandService: new ReleaseQueueCommandService());
}
