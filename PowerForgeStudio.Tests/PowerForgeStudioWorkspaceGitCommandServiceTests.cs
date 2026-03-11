using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Storage;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioWorkspaceGitCommandServiceTests
{
    [Fact]
    public async Task GetActionCatalogAsync_ReturnsSafeGitActionsForSelectedRepository()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "PowerForgeStudio", Guid.NewGuid().ToString("N"), "git-actions.db");

        try
        {
            var repository = CreateRepository();
            var snapshot = CreateSnapshot([repository]);
            var service = new WorkspaceGitCommandService(
                new WorkspaceSnapshotQueryService(),
                new RepositoryGitRemediationService(),
                new RepositoryGitQuickActionService(),
                new StubGitQuickActionExecutionService());

            var catalog = await service.GetActionCatalogAsync(snapshot, databasePath, "PSPublishModule");

            Assert.Equal("PSPublishModule", catalog.RepositoryName);
            Assert.Contains(catalog.Actions, action => action.Payload == "git status --short --branch");
            Assert.Contains(catalog.Actions, action => action.Payload.StartsWith("git switch -c ", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteParentDirectory(databasePath);
        }
    }

    [Fact]
    public async Task ExecuteActionAsync_PersistsReceiptForResolvedRepositoryAction()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "PowerForgeStudio", Guid.NewGuid().ToString("N"), "git-actions.db");

        try
        {
            var repository = CreateRepository();
            var snapshot = CreateSnapshot([repository]);
            var service = new WorkspaceGitCommandService(
                new WorkspaceSnapshotQueryService(),
                new RepositoryGitRemediationService(),
                new RepositoryGitQuickActionService(),
                new StubGitQuickActionExecutionService());

            var result = await service.ExecuteActionAsync(snapshot, databasePath, "PSPublishModule", "Inspect current git state");

            Assert.True(result.Changed);
            Assert.NotNull(result.Receipt);
            Assert.Equal("Inspect current git state", result.Receipt!.ActionTitle);
            Assert.True(result.Receipt.Succeeded);

            var stateDatabase = new ReleaseStateDatabase(databasePath);
            await stateDatabase.InitializeAsync();
            var persisted = await stateDatabase.LoadGitQuickActionReceiptsAsync();
            var receipt = Assert.Single(persisted);
            Assert.Equal(repository.RootPath, receipt.RootPath);
            Assert.Equal("Inspect current git state", receipt.ActionTitle);
        }
        finally
        {
            DeleteParentDirectory(databasePath);
        }
    }

    private static WorkspaceSnapshot CreateSnapshot(IReadOnlyList<RepositoryPortfolioItem> portfolioItems)
    {
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
            RepositoryFamilies: [],
            RepositoryFamilyLanes: [],
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

    private static RepositoryPortfolioItem CreateRepository()
        => new(
            new RepositoryCatalogEntry(
                Name: "PSPublishModule",
                RootPath: @"C:\Support\GitHub\PSPublishModule",
                RepositoryKind: ReleaseRepositoryKind.Mixed,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: @"C:\Support\GitHub\PSPublishModule\Build\Build-Module.ps1",
                ProjectBuildScriptPath: @"C:\Support\GitHub\PSPublishModule\Build\Build-Project.ps1",
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: "main",
                UpstreamBranch: "origin/main",
                AheadCount: 1,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0,
                Diagnostics: [
                    new RepositoryGitDiagnostic(
                        RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow,
                        RepositoryGitDiagnosticSeverity.Attention,
                        "PR branch required",
                        "Local commits are sitting on main; direct push is likely blocked.",
                        "Move the work onto a feature branch.")
                ]),
            new RepositoryReadiness(RepositoryReadinessKind.Attention, "Git action needed."));

    private static void DeleteParentDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubGitQuickActionExecutionService : IRepositoryGitQuickActionExecutionService
    {
        public Task<RepositoryGitQuickActionExecutionResult> ExecuteAsync(
            string repositoryRoot,
            RepositoryGitQuickAction action,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new RepositoryGitQuickActionExecutionResult(
                Succeeded: true,
                Summary: $"{action.Title} completed successfully.",
                OutputTail: "git status --short --branch",
                ErrorTail: null));
    }
}
