using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryReleaseInboxServiceTests
{
    [Fact]
    public void BuildInbox_PrioritizesQueueActionsAndDeduplicatesRepositories()
    {
        var waitingUsb = CreateRepository(
            name: "UsbRepo",
            rootPath: @"C:\Support\GitHub\UsbRepo",
            readinessKind: RepositoryReadinessKind.Ready,
            gitHubStatus: RepositoryGitHubInboxStatus.Attention);
        var failed = CreateRepository(
            name: "FailedRepo",
            rootPath: @"C:\Support\GitHub\FailedRepo",
            readinessKind: RepositoryReadinessKind.Attention);
        var readyToday = CreateRepository(
            name: "ReadyRepo",
            rootPath: @"C:\Support\GitHub\ReadyRepo",
            readinessKind: RepositoryReadinessKind.Ready,
            planResults: [
                new RepositoryPlanResult(
                    RepositoryPlanAdapterKind.ProjectPlan,
                    RepositoryPlanStatus.Succeeded,
                    "Plan generated.",
                    PlanPath: @"C:\Temp\ready.plan.json",
                    ExitCode: 0,
                    DurationSeconds: 1.2)
            ]);

        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(3, 0, 0, 1, 1, 0),
            Items: [
                new ReleaseQueueItem(
                    RootPath: failed.RootPath,
                    RepositoryName: failed.Name,
                    RepositoryKind: failed.RepositoryKind,
                    WorkspaceKind: failed.WorkspaceKind,
                    QueueOrder: 1,
                    Stage: ReleaseQueueStage.Publish,
                    Status: ReleaseQueueItemStatus.Failed,
                    Summary: "Publish failed.",
                    CheckpointKey: "publish.failed",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseQueueItem(
                    RootPath: waitingUsb.RootPath,
                    RepositoryName: waitingUsb.Name,
                    RepositoryKind: waitingUsb.RepositoryKind,
                    WorkspaceKind: waitingUsb.WorkspaceKind,
                    QueueOrder: 2,
                    Stage: ReleaseQueueStage.Sign,
                    Status: ReleaseQueueItemStatus.WaitingApproval,
                    Summary: "Waiting for USB token.",
                    CheckpointKey: "sign.waiting.usb",
                    CheckpointStateJson: null,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var service = new RepositoryReleaseInboxService();
        var inbox = service.BuildInbox([waitingUsb, failed, readyToday], queueSession);

        Assert.Collection(
            inbox,
            item => {
                Assert.Equal("FailedRepo", item.RepositoryName);
                Assert.Equal("Failed", item.Badge);
            },
            item => {
                Assert.Equal("UsbRepo", item.RepositoryName);
                Assert.Equal("USB Waiting", item.Badge);
            },
            item => {
                Assert.Equal("ReadyRepo", item.RepositoryName);
                Assert.Equal("Ready Today", item.Badge);
            });
    }

    [Fact]
    public void BuildInbox_IncludesGitGuardItemsWhenQueueIsNotAlreadyBlocking()
    {
        var prFlow = CreateRepository(
            name: "MainBranchRepo",
            rootPath: @"C:\Support\GitHub\MainBranchRepo",
            readinessKind: RepositoryReadinessKind.Attention,
            gitDiagnostics: [
                new RepositoryGitDiagnostic(
                    RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow,
                    RepositoryGitDiagnosticSeverity.Attention,
                    "PR branch required",
                    "Local commits are sitting on main; direct push is likely blocked.",
                    "Use a PR branch instead of pushing directly to main.")
            ]);

        var service = new RepositoryReleaseInboxService();
        var inbox = service.BuildInbox([prFlow], queueSession: null);

        var item = Assert.Single(inbox);
        Assert.Equal("PR Flow", item.Badge);
        Assert.Contains("direct push is likely blocked", item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildInbox_UsesProtectedBranchBadgeWhenGitHubConfirmsProtection()
    {
        var protectedBranch = CreateRepository(
            name: "ProtectedMainRepo",
            rootPath: @"C:\Support\GitHub\ProtectedMainRepo",
            readinessKind: RepositoryReadinessKind.Attention,
            gitHubStatus: RepositoryGitHubInboxStatus.Attention,
            gitDiagnostics: [
                new RepositoryGitDiagnostic(
                    RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow,
                    RepositoryGitDiagnosticSeverity.Attention,
                    "PR branch required",
                    "Local commits are sitting on main; direct push is likely blocked.",
                    "Use a PR branch instead of pushing directly to main.")
            ]);

        var service = new RepositoryReleaseInboxService();
        var inbox = service.BuildInbox([protectedBranch], queueSession: null);

        var item = Assert.Single(inbox);
        Assert.Equal("Protected Branch", item.Badge);
        Assert.Contains("protected on GitHub", item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildInbox_IncludesFailedGitQuickActionBeforeGenericGitGuardSignals()
    {
        var repository = CreateRepository(
            name: "ActionRepo",
            rootPath: @"C:\Support\GitHub\ActionRepo",
            readinessKind: RepositoryReadinessKind.Attention,
            gitDiagnostics: [
                new RepositoryGitDiagnostic(
                    RepositoryGitDiagnosticCode.NoUpstream,
                    RepositoryGitDiagnosticSeverity.Attention,
                    "No upstream configured",
                    "This repo does not have an upstream branch configured yet.",
                    "Set upstream before you try to publish from this branch.")
            ]);

        var receipts = new Dictionary<string, RepositoryGitQuickActionReceipt>(StringComparer.OrdinalIgnoreCase) {
            [repository.RootPath] = new(
                RootPath: repository.RootPath,
                ActionTitle: "Pull --rebase",
                ActionKind: RepositoryGitQuickActionKind.GitCommand,
                Payload: "git pull --rebase",
                Succeeded: false,
                Summary: "Pull --rebase failed with exit code 1.",
                OutputTail: "Auto-merging file",
                ErrorTail: "CONFLICT (content): Merge conflict in README.md",
                ExecutedAtUtc: DateTimeOffset.UtcNow)
        };

        var service = new RepositoryReleaseInboxService();
        var inbox = service.BuildInbox([repository], queueSession: null, receipts);

        var item = Assert.Single(inbox);
        Assert.Equal("Git Action Failed", item.Badge);
        Assert.Contains("Pull --rebase failed", item.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pull --rebase", item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static RepositoryPortfolioItem CreateRepository(
        string name,
        string rootPath,
        RepositoryReadinessKind readinessKind,
        RepositoryGitHubInboxStatus gitHubStatus = RepositoryGitHubInboxStatus.NotProbed,
        IReadOnlyList<RepositoryPlanResult>? planResults = null,
        IReadOnlyList<RepositoryGitDiagnostic>? gitDiagnostics = null)
    {
        var inbox = gitHubStatus == RepositoryGitHubInboxStatus.NotProbed
            ? null
            : new RepositoryGitHubInbox(
                gitHubStatus,
                RepositorySlug: $"EvotecIT/{name}",
                OpenPullRequestCount: 2,
                LatestWorkflowFailed: true,
                LatestReleaseTag: "v0.2.0",
                DefaultBranch: "main",
                ProbedBranch: "main",
                IsDefaultBranch: true,
                BranchProtectionEnabled: true,
                Summary: "GitHub attention.",
                Detail: "GitHub detail.");

        return new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: name,
                RootPath: rootPath,
                RepositoryKind: ReleaseRepositoryKind.Library,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: null,
                ProjectBuildScriptPath: Path.Combine(rootPath, "Build", "Build-Project.ps1"),
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0, gitDiagnostics),
            new RepositoryReadiness(readinessKind, readinessKind.ToString()),
            planResults,
            inbox,
            null);
    }
}

