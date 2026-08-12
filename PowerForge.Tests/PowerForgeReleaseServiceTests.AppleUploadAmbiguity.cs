namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Execute_AppleUpload_blocks_reupload_after_indeterminate_process_result(
        bool throwFromUploader,
        bool pinSource)
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;

            var initial = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        request.InvokeRemoteMutationStarted();
                        if (throwFromUploader)
                            throw new IOException("response channel closed after upload handoff");
                        return new AppleAppArchiveUploadResult
                        {
                            ArchivePath = request.ArchivePath,
                            ExportPath = request.ExportPath!,
                            ExportOptionsPlistPath = Path.Combine(request.ExportPath!, "ExportOptions.plist"),
                            ProcessResult = new ProcessRunResult(
                                -1,
                                string.Empty,
                                "response channel closed after upload handoff",
                                "xcodebuild",
                                TimeSpan.FromMinutes(5),
                                true)
                        };
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = pinSource ? sourceCommit : null,
                    AppleWaitForProcessing = false
                });

            Assert.False(initial.Success);
            Assert.Contains(
                new AppleReleaseReceiptStore().ReadAll(initial.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAmbiguous");

            var uploadCalls = 0;
            var resumed = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: _ => throw new InvalidOperationException("Ambiguous upload must block archiving."),
                    uploadAppleApp: request =>
                    {
                        uploadCalls++;
                        return CreateSuccessfulUpload(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = pinSource ? sourceCommit : null,
                    AppleWaitForProcessing = false
                });

            Assert.False(resumed.Success);
            Assert.Equal(0, uploadCalls);
            Assert.Contains("ambiguous remote result", resumed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("will not upload", resumed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_does_not_checkpoint_ambiguity_before_remote_mutation_starts()
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;

            var initial = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: _ => throw new InvalidOperationException("privacy validation failed before xcodebuild"))
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit,
                    AppleWaitForProcessing = false
                });

            Assert.False(initial.Success);
            Assert.DoesNotContain(
                new AppleReleaseReceiptStore().ReadAll(initial.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAmbiguous");

            var uploadCalls = 0;
            var retry = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        uploadCalls++;
                        return CreateSuccessfulUpload(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit,
                    AppleWaitForProcessing = false
                });

            Assert.True(retry.Success, retry.ErrorMessage);
            Assert.Equal(1, uploadCalls);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_blocks_reupload_when_success_attestation_has_no_delivery_id()
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            var stateCalls = 0;
            var seeded = CreateAppleAutomationService(
                    request => ++stateCalls == 1
                        ? CreateReleaseState(request, processingState: null)
                        : throw new InvalidOperationException("final readback unavailable"),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: CreateSuccessfulUpload)
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit,
                    AppleWaitForProcessing = false
                });
            Assert.False(seeded.Success);
            var attestation = Assert.Single(
                new AppleReleaseReceiptStore().ReadAll(seeded.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAttested");
            Assert.Null(Assert.Single(attestation.Targets).BuildUploadId);

            var archiveCalls = 0;
            var uploadCalls = 0;
            var resumed = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: request =>
                    {
                        archiveCalls++;
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: request =>
                    {
                        uploadCalls++;
                        return CreateSuccessfulUpload(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.UploadExisting,
                    AppleSourceCommit = sourceCommit,
                    AppleWaitForProcessing = false,
                    AppleAdoptExistingBuild = true,
                    AppleActionConfirmed = true
                });

            Assert.False(resumed.Success);
            Assert.Equal(0, archiveCalls);
            Assert.Equal(0, uploadCalls);
            Assert.Contains("without an App Store Connect Delivery UUID", resumed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("will not upload the archive again", resumed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
