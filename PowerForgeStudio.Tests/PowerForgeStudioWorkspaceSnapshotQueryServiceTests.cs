using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioWorkspaceSnapshotQueryServiceTests
{
    private readonly WorkspaceSnapshotQueryService _service = new();

    [Fact]
    public void FindFamilyLane_MatchesByDisplayNameAndMemberName()
    {
        var lane = new RepositoryWorkspaceFamilyLaneSnapshot(
            FamilyKey: "pswritehtml",
            DisplayName: "PSWriteHTML",
            Headline: "PSWriteHTML family board",
            Details: "1 ready, 0 usb, 0 publish, 0 verify, 0 failed, 0 completed.",
            ReadyCount: 1,
            UsbWaitingCount: 0,
            PublishReadyCount: 0,
            VerifyReadyCount: 0,
            FailedCount: 0,
            CompletedCount: 0,
            Members: [
                new RepositoryWorkspaceFamilyLaneItem(
                    RootPath: @"C:\Support\GitHub\PSWriteHTML",
                    RepositoryName: "PSWriteHTML",
                    WorkspaceKind: Domain.Catalog.ReleaseWorkspaceKind.PrimaryRepository,
                    LaneKey: "ready",
                    LaneDisplay: "Ready",
                    Detail: "Ready to build.",
                    ReadinessDisplay: "Ready",
                    SortOrder: 0)
            ]);

        var snapshot = CreateSnapshot(
            portfolioItems: [],
            releaseInboxItems: [],
            dashboardCards: [],
            families: [
                new RepositoryWorkspaceFamilySnapshot(
                    FamilyKey: "pswritehtml",
                    DisplayName: "PSWriteHTML",
                    PrimaryRootPath: @"C:\Support\GitHub\PSWriteHTML",
                    TotalMembers: 1,
                    WorktreeMembers: 0,
                    AttentionMembers: 0,
                    ReadyMembers: 1,
                    QueueActiveMembers: 0,
                    MemberSummary: "1 primary | 0 worktree | 0 review | 0 temp")
            ],
            familyLanes: [lane]);

        Assert.Same(lane, _service.FindFamilyLane(snapshot, "PSWriteHTML"));
        Assert.Same(lane, _service.FindFamilyLane(snapshot, "pswritehtml"));
        Assert.Same(lane, _service.FindFamilyLane(snapshot, @"C:\Support\GitHub\PSWriteHTML"));
    }

    [Fact]
    public void GetReleaseInbox_And_GetFamilies_RespectTopLimit()
    {
        var snapshot = CreateSnapshot(
            portfolioItems: [],
            releaseInboxItems: [
                new RepositoryReleaseInboxItem(@"C:\Support\GitHub\Repo1", "Repo1", "Repo1", "First", "Failed", RepositoryPortfolioFocusMode.Failed, string.Empty, null, 0),
                new RepositoryReleaseInboxItem(@"C:\Support\GitHub\Repo2", "Repo2", "Repo2", "Second", "USB Waiting", RepositoryPortfolioFocusMode.WaitingUsb, string.Empty, "usb-waiting", 1)
            ],
            dashboardCards: [],
            families: [
                new RepositoryWorkspaceFamilySnapshot("repo1", "Repo1", @"C:\Support\GitHub\Repo1", 1, 0, 1, 0, 0, "1 primary | 0 worktree | 0 review | 0 temp"),
                new RepositoryWorkspaceFamilySnapshot("repo2", "Repo2", @"C:\Support\GitHub\Repo2", 1, 0, 0, 1, 0, "1 primary | 0 worktree | 0 review | 0 temp")
            ],
            familyLanes: []);

        Assert.Single(_service.GetReleaseInbox(snapshot, 1));
        Assert.Single(_service.GetFamilies(snapshot, 1));
    }

    [Fact]
    public void FindRepository_MatchesByName_LeafName_AndPath()
    {
        var repo = new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: "PSPublishModule-private-gallery",
                RootPath: @"C:\Support\GitHub\PSPublishModule-private-gallery",
                RepositoryKind: ReleaseRepositoryKind.Module,
                WorkspaceKind: ReleaseWorkspaceKind.ReviewClone,
                ModuleBuildScriptPath: @"C:\Support\GitHub\PSPublishModule-private-gallery\Build\Build-Module.ps1",
                ProjectBuildScriptPath: null,
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Ready"));

        var snapshot = CreateSnapshot(
            portfolioItems: [repo],
            releaseInboxItems: [],
            dashboardCards: [],
            families: [],
            familyLanes: []);

        Assert.Same(repo, _service.FindRepository(snapshot, "PSPublishModule-private-gallery"));
        Assert.Same(repo, _service.FindRepository(snapshot, @"C:\Support\GitHub\PSPublishModule-private-gallery"));
        Assert.Same(repo, _service.FindRepository(snapshot, "pspublishmoduleprivategallery"));
    }

    private static WorkspaceSnapshot CreateSnapshot(
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        IReadOnlyList<RepositoryReleaseInboxItem> releaseInboxItems,
        IReadOnlyList<PortfolioDashboardSnapshot> dashboardCards,
        IReadOnlyList<RepositoryWorkspaceFamilySnapshot> families,
        IReadOnlyList<RepositoryWorkspaceFamilyLaneSnapshot> familyLanes)
    {
        return new WorkspaceSnapshot(
            WorkspaceRoot: @"C:\Support\GitHub",
            DatabasePath: @"C:\Support\GitHub\_state\powerforgestudio.db",
            BuildEngineResolution: new PSPublishModuleResolution(
                Source: PSPublishModuleResolutionSource.RepositoryManifest,
                ManifestPath: @"C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1",
                ModuleVersion: "1.0.0",
                IsUsable: true,
                Warning: null),
            Summary: new RepositoryPortfolioSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            PortfolioItems: portfolioItems,
            ReleaseInboxItems: releaseInboxItems,
            DashboardCards: dashboardCards,
            RepositoryFamilies: families,
            RepositoryFamilyLanes: familyLanes,
            QueueSession: new ReleaseQueueSession(
                SessionId: Guid.NewGuid().ToString("N"),
                WorkspaceRoot: @"C:\Support\GitHub",
                CreatedAtUtc: DateTimeOffset.UtcNow,
                Summary: new ReleaseQueueSummary(0, 0, 0, 0, 0, 0),
                Items: []),
            SigningStation: new StationSnapshot<Domain.Signing.ReleaseSigningArtifact>("No signing batch waiting.", "None.", []),
            SigningReceipts: [],
            SigningReceiptBatch: new ReceiptBatchSnapshot<Domain.Signing.ReleaseSigningReceipt>("No signing receipts yet.", "None.", []),
            PublishStation: new StationSnapshot<Domain.Publish.ReleasePublishTarget>("No publish batch ready.", "None.", []),
            PublishReceipts: [],
            PublishReceiptBatch: new ReceiptBatchSnapshot<Domain.Publish.ReleasePublishReceipt>("No publish receipts yet.", "None.", []),
            VerificationStation: new StationSnapshot<Domain.Verification.ReleaseVerificationTarget>("No verification batch ready.", "None.", []),
            VerificationReceipts: [],
            VerificationReceiptBatch: new ReceiptBatchSnapshot<Domain.Verification.ReleaseVerificationReceipt>("No verification receipts yet.", "None.", []),
            GitQuickActionReceipts: [],
            SavedPortfolioView: null);
    }
}
