using System.Text.Json;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.PowerShell;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Wpf.ViewModels;

namespace PowerForgeStudio.Wpf.Tests;

public sealed class ShellWorkspaceSessionStateTests
{
    [Fact]
    public void ApplyWorkspaceSnapshot_StoresPortfolioQueueAndSelectionInputs()
    {
        var state = new ShellWorkspaceSessionState(PSPublishModuleLocator.Resolve());
        var repository = CreateRepository("Repo.Alpha", @"C:\Support\GitHub\Repo.Alpha");
        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(1, 0, 0, 0, 0, 0),
            Items: [
                new ReleaseQueueItem(
                    RootPath: repository.RootPath,
                    RepositoryName: repository.Name,
                    RepositoryKind: ReleaseRepositoryKind.Module,
                    WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Build,
                    Status: ReleaseQueueItemStatus.ReadyToRun,
                    Summary: "Build ready.",
                    CheckpointKey: "build.ready",
                    CheckpointStateJson: JsonSerializer.Serialize(new { step = "build" }),
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);
        var receipt = new RepositoryGitQuickActionReceipt(
            RootPath: repository.RootPath,
            ActionTitle: "Fetch origin",
            ActionKind: RepositoryGitQuickActionKind.GitCommand,
            Payload: "git fetch origin",
            Succeeded: true,
            Summary: "Fetch completed.",
            OutputTail: null,
            ErrorTail: null,
            ExecutedAtUtc: DateTimeOffset.UtcNow);
        var snapshot = new WorkspaceSnapshot(
            WorkspaceRoot: @"C:\Support\GitHub",
            DatabasePath: @"C:\Support\GitHub\_state\powerforgestudio.db",
            BuildEngineResolution: new PSPublishModuleResolution(
                PSPublishModuleResolutionSource.RepositoryManifest,
                @"C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1",
                "1.0.0",
                true),
            Summary: new RepositoryPortfolioSummary(1, 1, 0, 0, 0, 0, 0, 0, 0, 0),
            PortfolioItems: [repository],
            ReleaseInboxItems: [],
            DashboardCards: [],
            RepositoryFamilies: [],
            RepositoryFamilyLanes: [],
            QueueSession: queueSession,
            SigningStation: new StationSnapshot<ReleaseSigningArtifact>("No signing batch waiting.", "None.", []),
            SigningReceipts: [],
            SigningReceiptBatch: new ReceiptBatchSnapshot<ReleaseSigningReceipt>("No signing receipts yet.", "None.", []),
            PublishStation: new StationSnapshot<ReleasePublishTarget>("No publish batch ready.", "None.", []),
            PublishReceipts: [],
            PublishReceiptBatch: new ReceiptBatchSnapshot<ReleasePublishReceipt>("No publish receipts yet.", "None.", []),
            VerificationStation: new StationSnapshot<ReleaseVerificationTarget>("No verification batch ready.", "None.", []),
            VerificationReceipts: [],
            VerificationReceiptBatch: new ReceiptBatchSnapshot<ReleaseVerificationReceipt>("No verification receipts yet.", "None.", []),
            GitQuickActionReceipts: [receipt],
            SavedPortfolioView: new RepositoryPortfolioViewState(null, RepositoryPortfolioFocusMode.All, string.Empty, "repoalpha", DateTimeOffset.UtcNow));

        state.ApplyWorkspaceSnapshot(snapshot);
        state.RestoreSavedPortfolioView(snapshot.SavedPortfolioView);

        Assert.Equal(queueSession.SessionId, state.ActiveQueueSession?.SessionId);
        Assert.Single(state.PortfolioSnapshot);
        Assert.Equal("repoalpha", state.SelectedRepositoryFamilyKey);
        Assert.Equal(receipt.ActionTitle, state.GitQuickActionReceiptLookup[repository.RootPath].ActionTitle);
        Assert.Equal(snapshot.BuildEngineResolution.ManifestPath, state.BuildEngineResolution.ManifestPath);
    }

    [Fact]
    public void ApplyPortfolioInteraction_AndProjectionResult_UpdateSelection()
    {
        var state = new ShellWorkspaceSessionState(PSPublishModuleLocator.Resolve());
        var repository = CreateRepository("Repo.Beta", @"C:\Support\GitHub\Repo.Beta");

        state.ApplyPortfolioInteraction(new PortfolioInteractionResult(
            Handled: true,
            SelectedRepositoryFamilyKey: "repobeta",
            SelectedRepositoryRootPath: repository.RootPath,
            ShouldScheduleSave: true));
        state.ApplyProjectionResult(new ShellWorkspaceProjectionResult([repository], repository, "repobeta"));

        Assert.Equal("repobeta", state.SelectedRepositoryFamilyKey);
        Assert.Equal(repository.RootPath, state.SelectedRepositoryRootPath);
        Assert.Equal("Repo.Beta", state.SelectedRepository?.Name);
    }

    private static RepositoryPortfolioItem CreateRepository(string name, string rootPath)
    {
        var familyKey = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return new(
            new RepositoryCatalogEntry(
                Name: name,
                RootPath: rootPath,
                RepositoryKind: ReleaseRepositoryKind.Module,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: Path.Combine(rootPath, "Build", "Build-Module.ps1"),
                ProjectBuildScriptPath: null,
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: "main",
                UpstreamBranch: "origin/main",
                AheadCount: 0,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Ready"),
            PlanResults: [],
            GitHubInbox: null,
            ReleaseDrift: null,
            WorkspaceFamilyKey: familyKey,
            WorkspaceFamilyName: name);
    }
}
