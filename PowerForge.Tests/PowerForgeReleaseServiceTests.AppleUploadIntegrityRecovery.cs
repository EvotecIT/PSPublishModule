namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppleArchiveUploadSnapshot_retains_failure_without_following_injected_alias(bool hardLink)
    {
        if (OperatingSystem.IsWindows()) return;
        var root = CreateSandbox();
        string? retained = null;
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root, "approved.xcarchive")).FullName;
            File.WriteAllText(Path.Combine(archive, "payload"), "approved");
            var outside = Path.Combine(root, "outside-private-data");
            File.WriteAllText(outside, "unrelated private bytes");
            var snapshot = AppleArchiveUploadSnapshot.Create(archive, AppleNotarizationService.ComputeArtifactSha256(archive));
            retained = snapshot.RootPath;
            var alias = Path.Combine(snapshot.ArchivePath, "injected-alias");
            if (hardLink) TestFileLink.CreateHardLink(alias, outside);
            else File.CreateSymbolicLink(alias, outside);

            var evidence = snapshot.RetainIntegrityFailure("injected alias invalidated the input");
            snapshot.Dispose();

            Assert.True(Directory.Exists(retained));
            Assert.Equal("unrelated private bytes", File.ReadAllText(outside));
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(evidence.Path));
            var entry = manifest.RootElement.GetProperty("After").EnumerateArray()
                .Single(value => value.GetProperty("Path").GetString() == "injected-alias");
            Assert.Equal(hardLink ? "hard-link alias (not opened)" : "link (not followed)", entry.GetProperty("Kind").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, entry.GetProperty("Sha256").ValueKind);
            Assert.False(manifest.RootElement.GetProperty("Cleanup").GetProperty("AutomaticDeletionAllowed").GetBoolean());
        }
        finally
        {
            if (retained is not null) TryDelete(retained);
            TryDelete(root);
        }
    }

    [Fact]
    public void AppleArchiveUploadSnapshot_does_not_write_or_cleanup_through_replaced_container()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = CreateSandbox();
        string? original = null;
        string? snapshotRoot = null;
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root, "approved.xcarchive")).FullName;
            File.WriteAllText(Path.Combine(archive, "payload"), "approved");
            var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
            File.WriteAllText(Path.Combine(outside, "keep"), "unrelated");
            var snapshot = AppleArchiveUploadSnapshot.Create(archive, AppleNotarizationService.ComputeArtifactSha256(archive));
            snapshotRoot = snapshot.RootPath;
            original = snapshotRoot + "-original";
            Directory.Move(snapshotRoot, original);
            Directory.CreateSymbolicLink(snapshotRoot, outside);

            Assert.Throws<InvalidOperationException>(() => snapshot.RetainIntegrityFailure("container replaced"));
            snapshot.Dispose();

            Assert.Equal("unrelated", File.ReadAllText(Path.Combine(outside, "keep")));
            Assert.False(File.Exists(Path.Combine(outside, "upload-integrity-failure.json")));
            Assert.True(Directory.Exists(original));
        }
        finally
        {
            if (snapshotRoot is not null && new DirectoryInfo(snapshotRoot).LinkTarget is not null)
                Directory.Delete(snapshotRoot);
            if (original is not null) TryDelete(original);
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("transient")]
    [InlineData("legacy")]
    [InlineData("pending")]
    [InlineData("adopt-changed-policy")]
    [InlineData("prepare-only")]
    [InlineData("resume-disabled")]
    public void Execute_AppleShipRelease_blocks_integrity_failed_upload_with_original_intent(string failureKind)
    {
        var root = CreateSandbox();
        string? retainedRoot = null;
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            WriteXcodeGenVersionSource(root, "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.CleanupAfterProcessing = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            EnableInternalAppleShipTargets(spec);
            var sourceCommit = CommitAppleShipSource(root);
            var uploaded = false;
            var planRequest = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = sourceCommit,
                AppleShipAppStoreTargets = ["CasaRay iOS"]
            };
            var initialService = CreateAppleAutomationService(
                request => CreateReleaseState(request, uploaded ? "VALID" : null),
                archiveAppleApp: CreateSuccessfulArchive,
                uploadAppleApp: request =>
                {
                    uploaded = true;
                    retainedRoot = Path.GetDirectoryName(request.ArchivePath);
                    var payload = Path.Combine(request.ArchivePath, "archive.bin");
                    File.WriteAllText(payload, "unapproved payload");
                    if (failureKind == "transient")
                        File.WriteAllText(payload, "archive");
                    var result = CreateSuccessfulUpload(request);
                    result.BuildUploadId = "ship-upload-14";
                    return result;
                },
                prepareAppleDistribution: _ => throw new InvalidOperationException("simulated interruption after upload"),
                generateAppleProject: _ => true,
                getHighestAppleBuildNumber: (_, _, _) => uploaded ? 14 : 13,
                checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request));
            var planned = initialService.Execute(spec, planRequest);

            var interrupted = initialService.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = planRequest.ConfigPath,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = sourceCommit,
                AppleShipAppStoreTargets = ["CasaRay iOS"],
                AppleExpectedPlanSha256 = planned.AppleReceipt!.PlanSha256,
                AppleActionConfirmed = true,
                AppleWaitForProcessing = false
            });

            Assert.False(interrupted.Success);
            Assert.Contains("private Apple upload", interrupted.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                new AppleReleaseReceiptStore().ReadAll(interrupted.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadCompleted" &&
                           string.Equals(receipt.PlanSha256, planned.AppleReceipt.PlanSha256, StringComparison.OrdinalIgnoreCase));

            var journal = new AppleReleaseReceiptStore();
            var receipts = journal.ReadAll(interrupted.AppleAppPlan!);
            var rejected = Assert.Single(receipts, receipt => receipt.OperationPhase == "UploadIntegrityFailed");
            var rejectedTarget = Assert.Single(rejected.Targets);
            Assert.NotNull(rejectedTarget.UploadIntegrityEvidencePath);
            Assert.True(File.Exists(rejectedTarget.UploadIntegrityEvidencePath));
            Assert.Equal(rejectedTarget.UploadIntegrityEvidenceSha256,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(rejectedTarget.UploadIntegrityEvidencePath))).ToLowerInvariant());
            Assert.True(Directory.Exists(retainedRoot));
            Assert.Equal(failureKind == "transient" ? "archive" : "unapproved payload",
                File.ReadAllText(Path.Combine(Directory.GetDirectories(retainedRoot!, "*.xcarchive").Single(), "archive.bin")));
            Assert.DoesNotContain(receipts, receipt => receipt.OperationPhase == "UploadAttested");

            if (failureKind is "legacy" or "pending")
            {
                // Model the shipped legacy chain, or a process interruption after delivery and
                // before the private guard can durably qualify/reject it. Only task fixture files.
                TryDelete(interrupted.AppleAppPlan!.ReceiptHistoryPath);
                File.Delete(interrupted.AppleAppPlan.ReceiptPath);
                foreach (var receipt in receipts.Reverse())
                {
                    if (receipt.OperationPhase == "UploadIntegrityFailed" ||
                        (failureKind == "pending" && receipt.OperationPhase == "Completed"))
                        continue;
                    if (failureKind == "legacy" && receipt.OperationPhase == "UploadCompleted")
                    {
                        receipt.OperationPhase = "UploadAttested";
                        receipt.Success = true;
                    }
                    if (failureKind == "legacy" && receipt.OperationPhase == "Completed")
                        Assert.Single(receipt.Targets).ErrorMessage =
                            "The private Apple upload snapshot changed while xcodebuild was reading it. Expected 'approved', received 'changed'. Discard the upload/export result and inspect remote state before retrying.";
                    journal.WriteAttempt(interrupted.AppleAppPlan, receipt);
                }
            }

            var archiveCalls = 0;
            var uploadCalls = 0;
            var prepareCalls = 0;
            var submitCalls = 0;
            var resumedService = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                archiveAppleApp: request =>
                {
                    archiveCalls++;
                    return CreateSuccessfulArchive(request);
                },
                uploadAppleApp: request =>
                {
                    uploadCalls++;
                    return CreateSuccessfulUpload(request);
                },
                prepareAppleDistribution: request =>
                {
                    prepareCalls++;
                    return CreateSuccessfulPreparation(request);
                },
                generateAppleProject: _ => true,
                getHighestAppleBuildNumber: (_, _, _) => 14,
                getAppleBuildUpload: (_, id) => new AppStoreConnectBuildUploadInfo
                {
                    Id = id,
                    State = "COMPLETE",
                    MarketingVersion = "1.6.0",
                    BuildNumber = "14",
                    Platform = "IOS"
                },
                submitAppleReview: request =>
                {
                    submitCalls++;
                    return new AppStoreConnectReviewSubmissionResult
                    {
                        AppId = request.AppId,
                        VersionString = request.VersionString,
                        BuildNumber = request.BuildNumber,
                        Platform = request.Platform
                    };
                },
                checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request));
            planRequest.AppleExpectedPlanSha256 = planned.AppleReceipt.PlanSha256;
            var resumedPlan = resumedService.Execute(spec, planRequest);

            Assert.Equal(planned.AppleReceipt.PlanSha256, resumedPlan.AppleReceipt!.PlanSha256);
            var resumeRequest = new PowerForgeReleaseRequest
            {
                ConfigPath = planRequest.ConfigPath,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = sourceCommit,
                AppleShipAppStoreTargets = ["CasaRay iOS"],
                AppleExpectedPlanSha256 = planned.AppleReceipt.PlanSha256,
                AppleActionConfirmed = true,
                AppleWaitForProcessing = false
            };
            if (failureKind is "adopt-changed-policy" or "prepare-only" or "resume-disabled")
            {
                resumeRequest.AppleAction = failureKind == "prepare-only"
                    ? PowerForgeAppleReleaseAction.Prepare
                    : PowerForgeAppleReleaseAction.UploadExisting;
                resumeRequest.AppleExpectedPlanSha256 = null;
                resumeRequest.AppleShipAppStoreTargets = [];
                if (failureKind == "adopt-changed-policy")
                {
                    spec.AppleApps.TeamId = "DIFFERENTTEAM";
                    resumeRequest.AppleAdoptExistingBuild = true;
                }
                if (failureKind == "resume-disabled")
                    spec.AppleApps.Automation.Resume = false;
            }
            var resumed = resumedService.Execute(spec, resumeRequest);

            Assert.False(resumed.Success);
            Assert.Contains("integrity", resumed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, archiveCalls);
            Assert.Equal(0, uploadCalls);
            Assert.Equal(0, prepareCalls);
            Assert.Equal(0, submitCalls);

        }
        finally
        {
            if (retainedRoot is not null) TryDelete(retainedRoot);
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("nonzero", true)]
    [InlineData("timeout", true)]
    [InlineData("exception", true)]
    [InlineData("nonzero", false)]
    [InlineData("timeout", false)]
    [InlineData("exception", false)]
    public void Execute_AppleUpload_rejects_mutated_ambiguous_attempt_before_adoption(string outcome, bool mutatePrivateInput)
    {
        var root = CreateSandbox();
        string? privateRoot = null;
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "synthetic-fixture");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.CleanupAfterProcessing = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            var source = EnsureTestSourceCommit(root);
            var initial = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null),
                archiveAppleApp: CreateSuccessfulArchive,
                uploadAppleApp: request =>
                {
                    request.InvokeRemoteMutationStarted();
                    privateRoot = Path.GetDirectoryName(request.ArchivePath);
                    if (mutatePrivateInput)
                        File.WriteAllText(Path.Combine(request.ArchivePath, "archive.bin"), "changed private payload");
                    if (outcome == "exception") throw new IOException("response lost after remote handoff");
                    return new AppleAppArchiveUploadResult
                    {
                        ArchivePath = request.ArchivePath,
                        ExportPath = request.ExportPath!,
                        ExportOptionsPlistPath = Path.Combine(request.ExportPath!, "ExportOptions.plist"),
                        ProcessResult = new ProcessRunResult(outcome == "timeout" ? -1 : 65,
                            string.Empty, "process failed after remote handoff", "xcodebuild", TimeSpan.FromMinutes(5), outcome == "timeout")
                    };
                }).Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = source,
                    AppleWaitForProcessing = false
                });
            Assert.False(initial.Success);
            var receipts = new AppleReleaseReceiptStore().ReadAll(initial.AppleAppPlan!);
            var ambiguous = Assert.Single(receipts, receipt => receipt.OperationPhase == "UploadAmbiguous");
            if (mutatePrivateInput)
            {
                var rejected = Assert.Single(receipts, receipt => receipt.OperationPhase == "UploadIntegrityFailed");
                var target = Assert.Single(rejected.Targets);
                Assert.True(target.UploadPerformed);
                Assert.Equal(ambiguous.AttemptId, target.UploadAttestationAttemptId);
                Assert.True(File.Exists(target.UploadIntegrityEvidencePath));
                Assert.True(Directory.Exists(privateRoot));
                Assert.Equal("changed private payload", File.ReadAllText(Path.Combine(
                    Directory.GetDirectories(privateRoot!, "*.xcarchive").Single(), "archive.bin")));
            }
            else
            {
                Assert.DoesNotContain(receipts, receipt => receipt.OperationPhase == "UploadIntegrityFailed");
                Assert.False(Directory.Exists(privateRoot));
            }
            var uploadCalls = 0;
            var archiveCalls = 0;
            var prepareCalls = 0;
            var recovery = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                archiveAppleApp: request => { archiveCalls++; return CreateSuccessfulArchive(request); },
                uploadAppleApp: request => { uploadCalls++; return CreateSuccessfulUpload(request); },
                prepareAppleDistribution: request => { prepareCalls++; return CreateSuccessfulPreparation(request); });
            var resumed = recovery.Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.UploadExisting,
                    AppleSourceCommit = source,
                    AppleAdoptExistingBuild = true,
                    AppleActionConfirmed = true,
                    AppleWaitForProcessing = false
                });
            Assert.Equal(!mutatePrivateInput, resumed.Success);
            Assert.Equal(0, archiveCalls);
            Assert.Equal(0, uploadCalls);
            if (mutatePrivateInput)
            {
                Assert.Contains("integrity", resumed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                var distribution = recovery.Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Prepare,
                    AppleSourceCommit = source,
                    AppleActionConfirmed = true
                });
                Assert.False(distribution.Success);
                Assert.Contains("integrity", distribution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(0, prepareCalls);
            }
            else
            {
                Assert.True(Assert.Single(resumed.AppleApps!).AdoptedExistingBuild);
            }
        }
        finally { if (privateRoot is not null) TryDelete(privateRoot); TryDelete(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_AppleUpload_local_private_failure_does_not_reject_future_remote_attempt(bool rehearse)
    {
        var root = CreateSandbox();
        string? privateRoot = null;
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "synthetic-fixture");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.CleanupAfterProcessing = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            var source = EnsureTestSourceCommit(root);
            var initial = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null),
                archiveAppleApp: CreateSuccessfulArchive,
                uploadAppleApp: request =>
                {
                    Assert.Equal(rehearse ? "export" : "upload", request.Destination);
                    // Local validation/export failed before any remote handoff.
                    privateRoot = Path.GetDirectoryName(request.ArchivePath);
                    File.WriteAllText(Path.Combine(request.ArchivePath, "archive.bin"), "local failed input");
                    return new AppleAppArchiveUploadResult
                    {
                        ArchivePath = request.ArchivePath,
                        ExportPath = request.ExportPath!,
                        ExportOptionsPlistPath = Path.Combine(request.ExportPath!, "ExportOptions.plist"),
                        ProcessResult = new ProcessRunResult(65, string.Empty, "local export failed", "xcodebuild", TimeSpan.Zero, false)
                    };
                }).Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = rehearse ? PowerForgeAppleReleaseAction.Rehearse : PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = source,
                    AppleWaitForProcessing = false
                });
            Assert.False(initial.Success);
            var receipts = new AppleReleaseReceiptStore().ReadAll(initial.AppleAppPlan!);
            Assert.DoesNotContain(receipts, receipt => receipt.OperationPhase == "UploadAmbiguous");
            var target = Assert.Single(Assert.Single(receipts, receipt => receipt.OperationPhase == "UploadIntegrityFailed").Targets);
            Assert.False(target.UploadPerformed);
            Assert.Null(target.UploadAttestationAttemptId);
            Assert.True(File.Exists(target.UploadIntegrityEvidencePath));
            var uploadCalls = 0;
            var retry = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null),
                archiveAppleApp: _ => throw new InvalidOperationException("Reuse the unchanged approved public archive."),
                uploadAppleApp: request => { uploadCalls++; return CreateSuccessfulUpload(request); })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.UploadExisting,
                    AppleSourceCommit = source,
                    AppleWaitForProcessing = false
                });
            Assert.True(retry.Success, retry.ErrorMessage);
            Assert.Equal(1, uploadCalls);
        }
        finally { if (privateRoot is not null) TryDelete(privateRoot); TryDelete(root); }
    }

}
