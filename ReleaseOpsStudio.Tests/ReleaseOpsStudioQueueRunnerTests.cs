using System.Text.Json;
using ReleaseOpsStudio.Domain.Catalog;
using ReleaseOpsStudio.Domain.Publish;
using ReleaseOpsStudio.Domain.Queue;
using ReleaseOpsStudio.Domain.Signing;
using ReleaseOpsStudio.Domain.Verification;
using ReleaseOpsStudio.Orchestrator.Queue;

namespace ReleaseOpsStudio.Tests;

public sealed class ReleaseOpsStudioQueueRunnerTests
{
    [Fact]
    public void AdvanceNextReadyItem_BuildReady_MovesItemToSigningGate()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\Testimo",
            RepositoryName: "Testimo",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Build,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready for build.",
            CheckpointKey: "build.ready",
            CheckpointStateJson: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var runner = new ReleaseQueueRunner();
        var result = runner.AdvanceNextReadyItem(session);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Sign, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.WaitingApproval, result.Session.Items[0].Status);
        Assert.Equal(1, result.Session.Summary.WaitingApprovalItems);
    }

    [Fact]
    public void ApproveNextSigningGate_SigningWait_MovesItemToPublishReady()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Sign,
            Status: ReleaseQueueItemStatus.WaitingApproval,
            Summary: "USB token required.",
            CheckpointKey: "sign.waiting.usb",
            CheckpointStateJson: "{}",
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var runner = new ReleaseQueueRunner();
        var result = runner.ApproveNextSigningGate(session);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Publish, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.ReadyToRun, result.Session.Items[0].Status);
        Assert.Equal(0, result.Session.Summary.WaitingApprovalItems);
    }

    [Fact]
    public void CompleteBuild_BuildResult_MovesItemToSigningGateAndStoresCheckpoint()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\OfficeIMO",
            RepositoryName: "OfficeIMO",
            RepositoryKind: ReleaseRepositoryKind.Library,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Build,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready for build.",
            CheckpointKey: "build.ready",
            CheckpointStateJson: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var buildResult = new ReleaseBuildExecutionResult(
            RootPath: session.Items[0].RootPath,
            Succeeded: true,
            Summary: "Build completed safely.",
            DurationSeconds: 12.4,
            AdapterResults: [
                new ReleaseBuildAdapterResult(
                    ReleaseBuildAdapterKind.ProjectBuild,
                    true,
                    "Project build completed.",
                    0,
                    12.4,
                    [@"C:\Support\GitHub\OfficeIMO\Artefacts\ProjectBuild"],
                    [@"C:\Support\GitHub\OfficeIMO\Artefacts\ProjectBuild\OfficeIMO.1.0.0.nupkg"])
            ]);

        var runner = new ReleaseQueueRunner();
        var result = runner.CompleteBuild(session, session.Items[0].RootPath, buildResult);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Sign, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.WaitingApproval, result.Session.Items[0].Status);
        Assert.Contains("Build completed safely.", result.Session.Items[0].Summary);
        Assert.NotNull(result.Session.Items[0].CheckpointStateJson);
    }

    [Fact]
    public void FailBuild_BuildResult_MarksItemFailed()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\Testimo",
            RepositoryName: "Testimo",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Build,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready for build.",
            CheckpointKey: "build.ready",
            CheckpointStateJson: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var buildResult = new ReleaseBuildExecutionResult(
            RootPath: session.Items[0].RootPath,
            Succeeded: false,
            Summary: "Module build failed on dependency resolution.",
            DurationSeconds: 3.2,
            AdapterResults: []);

        var runner = new ReleaseQueueRunner();
        var result = runner.FailBuild(session, session.Items[0].RootPath, buildResult);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueItemStatus.Failed, result.Session.Items[0].Status);
        Assert.Equal(1, result.Session.Summary.BlockedItems);
    }

    [Fact]
    public void CompleteSigning_SigningResult_MovesItemToPublishReady()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Sign,
            Status: ReleaseQueueItemStatus.WaitingApproval,
            Summary: "USB approval required.",
            CheckpointKey: "sign.waiting.usb",
            CheckpointStateJson: "{}",
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var signingResult = new ReleaseSigningExecutionResult(
            RootPath: session.Items[0].RootPath,
            Succeeded: true,
            Summary: "Signing completed with 2 signed and 0 skipped artifact(s).",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleaseSigningReceipt(
                    RootPath: session.Items[0].RootPath,
                    RepositoryName: "DbaClientX",
                    AdapterKind: "ProjectBuild",
                    ArtifactPath: @"C:\Support\GitHub\DbaClientX\Artefacts\ProjectBuild\DbaClientX.Core.0.2.0.nupkg",
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Package signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var runner = new ReleaseQueueRunner();
        var result = runner.CompleteSigning(session, session.Items[0].RootPath, signingResult);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Publish, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.ReadyToRun, result.Session.Items[0].Status);
        Assert.Contains("Signing completed", result.Session.Items[0].Summary);
    }

    [Fact]
    public void FailSigning_SigningResult_MarksItemFailed()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\PSWriteHTML",
            RepositoryName: "PSWriteHTML",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Sign,
            Status: ReleaseQueueItemStatus.WaitingApproval,
            Summary: "USB approval required.",
            CheckpointKey: "sign.waiting.usb",
            CheckpointStateJson: "{}",
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var signingResult = new ReleaseSigningExecutionResult(
            RootPath: session.Items[0].RootPath,
            Succeeded: false,
            Summary: "Signing completed with 1 failure(s), 0 signed, 0 skipped.",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleaseSigningReceipt(
                    RootPath: session.Items[0].RootPath,
                    RepositoryName: "PSWriteHTML",
                    AdapterKind: "ModuleBuild",
                    ArtifactPath: @"C:\Support\GitHub\PSWriteHTML\Artefacts\Packed\PSWriteHTML.psm1",
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Failed,
                    Summary: "Signing is not configured.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var runner = new ReleaseQueueRunner();
        var result = runner.FailSigning(session, session.Items[0].RootPath, signingResult);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Sign, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.Failed, result.Session.Items[0].Status);
        Assert.Equal(1, result.Session.Summary.BlockedItems);
    }

    [Fact]
    public void CompletePublish_PublishResult_MovesItemToVerifyReady()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Publish is ready.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: "{}",
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var publishResult = new ReleasePublishExecutionResult(
            RootPath: session.Items[0].RootPath,
            Succeeded: true,
            Summary: "Publish completed with 2 published and 0 skipped target(s).",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleasePublishReceipt(
                    RootPath: session.Items[0].RootPath,
                    RepositoryName: "DbaClientX",
                    AdapterKind: "ProjectBuild",
                    TargetName: "GitHub release",
                    TargetKind: "GitHub",
                    Destination: "https://github.com/EvotecIT/DbaClientX/releases/tag/v0.2.0",
                    SourcePath: null,
                    Status: ReleasePublishReceiptStatus.Published,
                    Summary: "GitHub release v0.2.0 published.",
                    PublishedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var runner = new ReleaseQueueRunner();
        var result = runner.CompletePublish(session, session.Items[0].RootPath, publishResult);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Verify, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.ReadyToRun, result.Session.Items[0].Status);
    }

    [Fact]
    public void FailPublish_PublishResult_MarksItemFailed()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\PSWriteHTML",
            RepositoryName: "PSWriteHTML",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Publish is ready.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: "{}",
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var publishResult = new ReleasePublishExecutionResult(
            RootPath: session.Items[0].RootPath,
            Succeeded: false,
            Summary: "Publish completed with 0 published, 0 skipped, and 1 failed target(s).",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleasePublishReceipt(
                    RootPath: session.Items[0].RootPath,
                    RepositoryName: "PSWriteHTML",
                    AdapterKind: "ModuleBuild",
                    TargetName: "Module publish",
                    TargetKind: "PowerShellRepository",
                    Destination: "PSGallery",
                    SourcePath: null,
                    Status: ReleasePublishReceiptStatus.Failed,
                    Summary: "Publish is disabled.",
                    PublishedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var runner = new ReleaseQueueRunner();
        var result = runner.FailPublish(session, session.Items[0].RootPath, publishResult);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Publish, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.Failed, result.Session.Items[0].Status);
        Assert.Equal(1, result.Session.Summary.BlockedItems);
    }

    [Fact]
    public void CompleteVerification_VerificationResult_CompletesQueueItem()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Verify,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Verification is ready.",
            CheckpointKey: "verify.ready",
            CheckpointStateJson: "{}",
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var verificationResult = new ReleaseVerificationExecutionResult(
            RootPath: session.Items[0].RootPath,
            Succeeded: true,
            Summary: "Verification completed with 2 verified and 0 skipped check(s).",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleaseVerificationReceipt(
                    RootPath: session.Items[0].RootPath,
                    RepositoryName: "DbaClientX",
                    AdapterKind: "ProjectBuild",
                    TargetName: "GitHub release",
                    TargetKind: "GitHub",
                    Destination: "https://github.com/EvotecIT/DbaClientX/releases/tag/v0.2.0",
                    Status: ReleaseVerificationReceiptStatus.Verified,
                    Summary: "GitHub release probe succeeded.",
                    VerifiedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var runner = new ReleaseQueueRunner();
        var result = runner.CompleteVerification(session, session.Items[0].RootPath, verificationResult);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Completed, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.Succeeded, result.Session.Items[0].Status);
    }

    [Fact]
    public void FailVerification_VerificationResult_MarksItemFailed()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\PSWriteHTML",
            RepositoryName: "PSWriteHTML",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Verify,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Verification is ready.",
            CheckpointKey: "verify.ready",
            CheckpointStateJson: "{}",
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var verificationResult = new ReleaseVerificationExecutionResult(
            RootPath: session.Items[0].RootPath,
            Succeeded: false,
            Summary: "Verification completed with 0 verified, 0 skipped, and 1 failed check(s).",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleaseVerificationReceipt(
                    RootPath: session.Items[0].RootPath,
                    RepositoryName: "PSWriteHTML",
                    AdapterKind: "ModuleBuild",
                    TargetName: "Module publish",
                    TargetKind: "PowerShellRepository",
                    Destination: "PSGallery",
                    Status: ReleaseVerificationReceiptStatus.Failed,
                    Summary: "PSGallery probe failed.",
                    VerifiedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var runner = new ReleaseQueueRunner();
        var result = runner.FailVerification(session, session.Items[0].RootPath, verificationResult);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Verify, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.Failed, result.Session.Items[0].Status);
        Assert.Equal(1, result.Session.Summary.BlockedItems);
    }

    [Fact]
    public void RetryFailedItem_BuildFailure_RearmsBuildStage()
    {
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\Testimo",
            RepositoryName: "Testimo",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Build,
            Status: ReleaseQueueItemStatus.Failed,
            Summary: "Build failed.",
            CheckpointKey: "build.failed",
            CheckpointStateJson: JsonSerializer.Serialize(new ReleaseBuildExecutionResult(
                RootPath: @"C:\Support\GitHub\Testimo",
                Succeeded: false,
                Summary: "Build failed.",
                DurationSeconds: 1.0,
                AdapterResults: [])),
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var runner = new ReleaseQueueRunner();
        var result = runner.RetryFailedItem(session);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Build, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.ReadyToRun, result.Session.Items[0].Status);
        Assert.Equal("build.ready", result.Session.Items[0].CheckpointKey);
        Assert.Null(result.Session.Items[0].CheckpointStateJson);
    }

    [Fact]
    public void RetryFailedItem_SigningFailure_RearmsSigningGateWithBuildCheckpoint()
    {
        var buildCheckpoint = JsonSerializer.Serialize(new ReleaseBuildExecutionResult(
            RootPath: @"C:\Support\GitHub\PSWriteHTML",
            Succeeded: true,
            Summary: "Build completed.",
            DurationSeconds: 5.2,
            AdapterResults: []));
        var failedSigning = new ReleaseSigningExecutionResult(
            RootPath: @"C:\Support\GitHub\PSWriteHTML",
            Succeeded: false,
            Summary: "Signing failed.",
            SourceCheckpointStateJson: buildCheckpoint,
            Receipts: []);
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\PSWriteHTML",
            RepositoryName: "PSWriteHTML",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Sign,
            Status: ReleaseQueueItemStatus.Failed,
            Summary: "Signing failed.",
            CheckpointKey: "sign.failed",
            CheckpointStateJson: JsonSerializer.Serialize(failedSigning),
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var runner = new ReleaseQueueRunner();
        var result = runner.RetryFailedItem(session);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Sign, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.WaitingApproval, result.Session.Items[0].Status);
        Assert.Equal("sign.waiting.usb", result.Session.Items[0].CheckpointKey);
        Assert.Equal(buildCheckpoint, result.Session.Items[0].CheckpointStateJson);
    }

    [Fact]
    public void RetryFailedItem_PublishFailure_RearmsPublishStageWithSigningCheckpoint()
    {
        var signingCheckpoint = JsonSerializer.Serialize(new ReleaseSigningExecutionResult(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            Succeeded: true,
            Summary: "Signing completed.",
            SourceCheckpointStateJson: "{}",
            Receipts: []));
        var failedPublish = new ReleasePublishExecutionResult(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            Succeeded: false,
            Summary: "Publish failed.",
            SourceCheckpointStateJson: signingCheckpoint,
            Receipts: []);
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.Failed,
            Summary: "Publish failed.",
            CheckpointKey: "publish.failed",
            CheckpointStateJson: JsonSerializer.Serialize(failedPublish),
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var runner = new ReleaseQueueRunner();
        var result = runner.RetryFailedItem(session);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Publish, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.ReadyToRun, result.Session.Items[0].Status);
        Assert.Equal("publish.ready", result.Session.Items[0].CheckpointKey);
        Assert.Equal(signingCheckpoint, result.Session.Items[0].CheckpointStateJson);
    }

    [Fact]
    public void RetryFailedItem_VerificationFailure_RearmsVerifyStageWithPublishCheckpoint()
    {
        var publishCheckpoint = JsonSerializer.Serialize(new ReleasePublishExecutionResult(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            Succeeded: true,
            Summary: "Publish completed.",
            SourceCheckpointStateJson: "{}",
            Receipts: []));
        var failedVerification = new ReleaseVerificationExecutionResult(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            Succeeded: false,
            Summary: "Verification failed.",
            SourceCheckpointStateJson: publishCheckpoint,
            Receipts: []);
        var session = CreateSession(new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Verify,
            Status: ReleaseQueueItemStatus.Failed,
            Summary: "Verification failed.",
            CheckpointKey: "verify.failed",
            CheckpointStateJson: JsonSerializer.Serialize(failedVerification),
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var runner = new ReleaseQueueRunner();
        var result = runner.RetryFailedItem(session);

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueStage.Verify, result.Session.Items[0].Stage);
        Assert.Equal(ReleaseQueueItemStatus.ReadyToRun, result.Session.Items[0].Status);
        Assert.Equal("verify.ready", result.Session.Items[0].CheckpointKey);
        Assert.Equal(publishCheckpoint, result.Session.Items[0].CheckpointStateJson);
    }

    [Fact]
    public void RetryFailedItems_FamilyPredicate_RearmsOnlyMatchingFailedItems()
    {
        var buildFailure = new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Build,
            Status: ReleaseQueueItemStatus.Failed,
            Summary: "Build failed.",
            CheckpointKey: "build.failed",
            CheckpointStateJson: JsonSerializer.Serialize(new ReleaseBuildExecutionResult(
                RootPath: @"C:\Support\GitHub\DbaClientX",
                Succeeded: false,
                Summary: "Build failed.",
                DurationSeconds: 1.0,
                AdapterResults: [])),
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        var otherFailure = new ReleaseQueueItem(
            RootPath: @"C:\Support\GitHub\PSWriteHTML",
            RepositoryName: "PSWriteHTML",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 2,
            Stage: ReleaseQueueStage.Build,
            Status: ReleaseQueueItemStatus.Failed,
            Summary: "Build failed.",
            CheckpointKey: "build.failed",
            CheckpointStateJson: JsonSerializer.Serialize(new ReleaseBuildExecutionResult(
                RootPath: @"C:\Support\GitHub\PSWriteHTML",
                Succeeded: false,
                Summary: "Build failed.",
                DurationSeconds: 1.0,
                AdapterResults: [])),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var session = CreateSession(buildFailure, otherFailure);
        var runner = new ReleaseQueueRunner();

        var result = runner.RetryFailedItems(session, item => string.Equals(item.RootPath, buildFailure.RootPath, StringComparison.OrdinalIgnoreCase));

        Assert.True(result.Changed);
        Assert.Equal(ReleaseQueueItemStatus.ReadyToRun, result.Session.Items[0].Status);
        Assert.Equal(ReleaseQueueItemStatus.Failed, result.Session.Items[1].Status);
        Assert.Equal(1, result.Session.Summary.BuildReadyItems);
        Assert.Equal(1, result.Session.Summary.BlockedItems);
    }

    private static ReleaseQueueSession CreateSession(params ReleaseQueueItem[] items)
    {
        return new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: @"C:\Support\GitHub",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(
                TotalItems: items.Length,
                BuildReadyItems: items.Count(item => item.Stage == ReleaseQueueStage.Build && item.Status == ReleaseQueueItemStatus.ReadyToRun),
                PreparePendingItems: items.Count(item => item.Stage == ReleaseQueueStage.Prepare && item.Status == ReleaseQueueItemStatus.Pending),
                WaitingApprovalItems: items.Count(item => item.Status == ReleaseQueueItemStatus.WaitingApproval),
                BlockedItems: items.Count(item => item.Status == ReleaseQueueItemStatus.Blocked || item.Status == ReleaseQueueItemStatus.Failed),
                VerificationReadyItems: items.Count(item => item.Stage == ReleaseQueueStage.Verify && item.Status == ReleaseQueueItemStatus.ReadyToRun)),
            Items: items);
    }
}

