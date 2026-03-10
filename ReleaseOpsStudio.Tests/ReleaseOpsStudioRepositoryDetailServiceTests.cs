using ReleaseOpsStudio.Domain.Catalog;
using ReleaseOpsStudio.Domain.Portfolio;
using ReleaseOpsStudio.Domain.PowerShell;
using ReleaseOpsStudio.Domain.Queue;
using ReleaseOpsStudio.Orchestrator.Portfolio;

namespace ReleaseOpsStudio.Tests;

public sealed class ReleaseOpsStudioRepositoryDetailServiceTests
{
    [Fact]
    public void CreateDetail_NoRepositorySelected_ReturnsPlaceholderSnapshot()
    {
        var service = new RepositoryDetailService();
        var resolution = new PSPublishModuleResolution(
            PSPublishModuleResolutionSource.InstalledModule,
            @"C:\Modules\PSPublishModule\3.0.0.139\PSPublishModule.psd1",
            "3.0.0",
            true,
            "Installed module can lag behind repo DSL changes.");

        var detail = service.CreateDetail(null, queueSession: null, resolution);

        Assert.Equal("No repository selected", detail.RepositoryName);
        Assert.Single(detail.AdapterEvidence);
        Assert.Equal("Plan preview", detail.AdapterEvidence[0].AdapterDisplay);
        Assert.Equal("No checkpoint payload captured yet.", detail.QueueCheckpointPayload);
    }

    [Fact]
    public void CreateDetail_SelectedRepositoryShapesQueueAndPlanEvidence()
    {
        var repository = new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: "DbaClientX",
                RootPath: @"C:\Support\GitHub\DbaClientX",
                RepositoryKind: ReleaseRepositoryKind.Mixed,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: @"C:\Support\GitHub\DbaClientX\Build\Build-Module.ps1",
                ProjectBuildScriptPath: @"C:\Support\GitHub\DbaClientX\Build\Build-Project.ps1",
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: "main",
                UpstreamBranch: "origin/main",
                AheadCount: 1,
                BehindCount: 0,
                TrackedChangeCount: 2,
                UntrackedChangeCount: 1),
            new RepositoryReadiness(RepositoryReadinessKind.Attention, "Plan preview has a failed adapter."),
            [
                new RepositoryPlanResult(
                    RepositoryPlanAdapterKind.ProjectPlan,
                    RepositoryPlanStatus.Failed,
                    "Project plan failed.",
                    PlanPath: @"C:\Temp\project-plan.json",
                    ExitCode: 1,
                    DurationSeconds: 4.2,
                    OutputTail: "Building...\nDone",
                    ErrorTail: "Parameter -NETSearchClass is not recognized."),
                new RepositoryPlanResult(
                    RepositoryPlanAdapterKind.ModuleJsonExport,
                    RepositoryPlanStatus.Succeeded,
                    "Module plan succeeded.",
                    PlanPath: @"C:\Temp\module-plan.json",
                    ExitCode: 0,
                    DurationSeconds: 2.1)
            ]);

        var queueSession = new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Items: [
                new ReleaseQueueItem(
                    RootPath: repository.RootPath,
                    RepositoryName: repository.Name,
                    RepositoryKind: repository.RepositoryKind,
                    WorkspaceKind: repository.WorkspaceKind,
                    QueueOrder: 4,
                    Stage: ReleaseQueueStage.Sign,
                    Status: ReleaseQueueItemStatus.WaitingApproval,
                    Summary: "Artifacts are waiting on the USB token.",
                    CheckpointKey: "sign.waiting.usb",
                    CheckpointStateJson: """{"artifacts":["module.nupkg"],"signingMode":"usb"}""",
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ],
            Summary: new ReleaseQueueSummary(
                TotalItems: 1,
                BuildReadyItems: 0,
                PreparePendingItems: 0,
                WaitingApprovalItems: 1,
                BlockedItems: 0,
                VerificationReadyItems: 0));

        var resolution = new PSPublishModuleResolution(
            PSPublishModuleResolutionSource.InstalledModule,
            @"C:\Users\me\Documents\PowerShell\Modules\PSPublishModule\3.0.0.139\PSPublishModule.psd1",
            "3.0.0",
            true,
            "Installed module can lag behind repo DSL changes.");

        var service = new RepositoryDetailService();
        var detail = service.CreateDetail(repository, queueSession, resolution);

        Assert.Equal("DbaClientX", detail.RepositoryName);
        Assert.Equal("Mixed / PrimaryRepository", detail.RepositoryBadge);
        Assert.Equal("Sign / Waiting", detail.QueueLaneDisplay);
        Assert.Equal("sign.waiting.usb", detail.QueueCheckpointDisplay);
        Assert.Contains("\"signingMode\": \"usb\"", detail.QueueCheckpointPayload);
        Assert.Equal(2, detail.AdapterEvidence.Count);
        Assert.Equal("ProjectPlan", detail.AdapterEvidence[0].AdapterDisplay);
        Assert.Contains("NETSearchClass", detail.AdapterEvidence[0].Detail);
        Assert.Equal(@"C:\Temp\project-plan.json", detail.AdapterEvidence[0].ArtifactPath);
        Assert.Contains("Building...", detail.AdapterEvidence[0].OutputTail);
        Assert.Contains("NETSearchClass", detail.AdapterEvidence[0].ErrorTail);
        Assert.Contains("lag behind repo DSL changes", detail.BuildEngineAdvisory);
    }
}

