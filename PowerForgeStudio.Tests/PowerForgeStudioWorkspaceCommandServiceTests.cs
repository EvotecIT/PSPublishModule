using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioWorkspaceCommandServiceTests
{
    [Fact]
    public async Task PrepareQueueAsync_FamilySelector_UsesMatchingFamilyScope()
    {
        var queueCommandService = new FakeReleaseQueueCommandService();
        var service = new WorkspaceCommandService(queueCommandService, new WorkspaceSnapshotQueryService());
        var snapshot = CreateSnapshot([
            CreateRepository("PSPublishModule", @"C:\Support\GitHub\PSPublishModule", "pspublishmodule", "PSPublishModule"),
            CreateRepository("PSPublishModule-private-gallery", @"C:\Support\GitHub\PSPublishModule-private-gallery", "pspublishmodule", "PSPublishModule"),
            CreateRepository("DbaClientX", @"C:\Support\GitHub\DbaClientX", "dbaclientx", "DbaClientX")
        ]);

        await service.PrepareQueueAsync(snapshot, @"C:\Temp\powerforgestudio.db", "PSPublishModule");

        Assert.Equal("pspublishmodule", queueCommandService.LastScopeKey);
        Assert.Equal("PSPublishModule", queueCommandService.LastScopeDisplayName);
        Assert.Equal(2, queueCommandService.LastPortfolioItems.Count);
        Assert.All(queueCommandService.LastPortfolioItems, item => Assert.Equal("pspublishmodule", item.FamilyKey));
    }

    [Fact]
    public async Task RetryFailedAsync_FamilySelector_RestrictsPredicateToMatchingFamily()
    {
        var queueCommandService = new FakeReleaseQueueCommandService();
        var service = new WorkspaceCommandService(queueCommandService, new WorkspaceSnapshotQueryService());
        var snapshot = CreateSnapshot([
            CreateRepository("PSPublishModule", @"C:\Support\GitHub\PSPublishModule", "pspublishmodule", "PSPublishModule"),
            CreateRepository("PSPublishModule-private-gallery", @"C:\Support\GitHub\PSPublishModule-private-gallery", "pspublishmodule", "PSPublishModule"),
            CreateRepository("DbaClientX", @"C:\Support\GitHub\DbaClientX", "dbaclientx", "DbaClientX")
        ]);

        await service.RetryFailedAsync(snapshot, @"C:\Temp\powerforgestudio.db", "PSPublishModule");

        Assert.NotNull(queueCommandService.LastRetryPredicate);
        Assert.True(queueCommandService.LastRetryPredicate!(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\PSPublishModule",
            RepositoryName: "PSPublishModule",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Build,
            Status: ReleaseQueueItemStatus.Failed,
            Summary: "Failed",
            CheckpointKey: "build.failed",
            CheckpointStateJson: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow)));
        Assert.False(queueCommandService.LastRetryPredicate!(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Library,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 2,
            Stage: ReleaseQueueStage.Build,
            Status: ReleaseQueueItemStatus.Failed,
            Summary: "Failed",
            CheckpointKey: "build.failed",
            CheckpointStateJson: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow)));
    }

    private static WorkspaceSnapshot CreateSnapshot(IReadOnlyList<RepositoryPortfolioItem> portfolioItems)
    {
        var families = new RepositoryWorkspaceFamilyService().BuildFamilies(portfolioItems, queueSession: null);
        var lanes = new RepositoryWorkspaceFamilyService().BuildFamilyLanes(portfolioItems, queueSession: null);

        return new WorkspaceSnapshot(
            WorkspaceRoot: @"C:\Support\GitHub",
            DatabasePath: @"C:\Support\GitHub\_state\powerforgestudio.db",
            BuildEngineResolution: new PSPublishModuleResolution(
                Source: PSPublishModuleResolutionSource.RepositoryManifest,
                ManifestPath: @"C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1",
                ModuleVersion: "1.0.0",
                IsUsable: true),
            Summary: new RepositoryPortfolioSummary(portfolioItems.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            PortfolioItems: portfolioItems,
            ReleaseInboxItems: [],
            DashboardCards: [],
            RepositoryFamilies: families,
            RepositoryFamilyLanes: lanes,
            QueueSession: new ReleaseQueueSession(Guid.NewGuid().ToString("N"), @"C:\Support\GitHub", DateTimeOffset.UtcNow, new ReleaseQueueSummary(0, 0, 0, 0, 0, 0), []),
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
            SavedPortfolioView: null);
    }

    private static RepositoryPortfolioItem CreateRepository(string name, string rootPath, string familyKey, string familyName)
        => new(
            new RepositoryCatalogEntry(
                Name: name,
                RootPath: rootPath,
                RepositoryKind: ReleaseRepositoryKind.Module,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: Path.Combine(rootPath, "Build", "Build-Module.ps1"),
                ProjectBuildScriptPath: null,
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Ready"),
            PlanResults: [],
            WorkspaceFamilyKey: familyKey,
            WorkspaceFamilyName: familyName);

    private sealed class FakeReleaseQueueCommandService : IReleaseQueueCommandService
    {
        public IReadOnlyList<RepositoryPortfolioItem> LastPortfolioItems { get; private set; } = [];

        public string? LastScopeKey { get; private set; }

        public string? LastScopeDisplayName { get; private set; }

        public Func<ReleaseQueueItem, bool>? LastRetryPredicate { get; private set; }

        public Task<ReleaseQueueCommandResult> RunNextReadyItemAsync(string databasePath, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateResult("run-next"));

        public Task<ReleaseQueueCommandResult> ApproveUsbAsync(string databasePath, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateResult("approve-usb"));

        public Task<ReleaseQueueCommandResult> RetryFailedAsync(string databasePath, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateResult("retry-all"));

        public Task<ReleaseQueueCommandResult> RetryFailedAsync(string databasePath, Func<ReleaseQueueItem, bool> predicate, CancellationToken cancellationToken = default)
        {
            LastRetryPredicate = predicate;
            return Task.FromResult(CreateResult("retry-filtered"));
        }

        public Task<ReleaseQueueCommandResult> PrepareQueueAsync(string databasePath, string workspaceRoot, IReadOnlyList<RepositoryPortfolioItem> portfolioItems, string? scopeKey = null, string? scopeDisplayName = null, CancellationToken cancellationToken = default)
        {
            LastPortfolioItems = portfolioItems;
            LastScopeKey = scopeKey;
            LastScopeDisplayName = scopeDisplayName;
            return Task.FromResult(CreateResult("prepare"));
        }

        private static ReleaseQueueCommandResult CreateResult(string message)
            => new(false, message, null, [], [], []);
    }
}
