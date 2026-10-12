using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_AppleUpload_ExportCaptureFailureRetainsCompletedFailureAndUploadCheckpoint()
    {
        if (OperatingSystem.IsWindows()) return; // Native symbolic-link fixture without privilege requirements.
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "synthetic-private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, processingState: null),
                archiveAppleApp: request =>
                {
                    Directory.CreateDirectory(request.ArchivePath!);
                    File.WriteAllText(Path.Combine(request.ArchivePath!, "payload"), "trusted-archive");
                    return CreateSuccessfulArchive(request);
                },
                uploadAppleApp: request =>
                {
                    // The uploader succeeds, but its retained export is unsafe to attest.
                    var retained = Directory.CreateDirectory(Path.Combine(root, "retained-export"));
                    Directory.CreateDirectory(Path.GetDirectoryName(request.ExportPath!)!);
                    Directory.CreateSymbolicLink(request.ExportPath!, retained.FullName);
                    return CreateSuccessfulUpload(request);
                });
            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleSourceCommit = EnsureTestSourceCommit(root),
                AppleAction = PowerForgeAppleReleaseAction.Upload
            });
            Assert.False(result.Success);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            Assert.Equal("Completed", receipt.OperationPhase);
            Assert.False(receipt.Success);
            Assert.Contains("capture upload export evidence", receipt.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            var history = new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!);
            Assert.Contains(history, value => value.OperationPhase == "UploadCompleted" && value.Targets.Any(target => target.UploadPerformed));
            Assert.Contains(history, value => value.OperationPhase == "Completed" && !value.Success);
            Assert.True(File.Exists(Path.Combine(root, receipt.ReceiptPath!)));
        }
        finally { TryDelete(root); }
    }
    [Fact]
    public void Execute_AppleUpload_PauseAfterArchiveRetainsFailureInsteadOfRetryingEntry()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var key = Path.Combine(root, "AuthKey_TEST.p8"); File.WriteAllText(key, "synthetic-private-key");
            var spec = CreateAppleAutomationSpec(root, key);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            var uploads = 0;
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, null),
                archiveAppleApp: request =>
                {
                    Directory.CreateDirectory(request.ArchivePath!);
                    File.WriteAllText(Path.Combine(request.ArchivePath!, "payload"), "archive");
                    var app = spec.AppleApps.Apps.Single();
                    var directory = Directory.CreateDirectory(Path.Combine(_appleAppStateRoot, "apps", AppleReleaseAppState.Hash(app.BundleId!.ToLowerInvariant()))).FullName;
                    File.WriteAllText(Path.Combine(directory, "paused"), "pause after mutation");
                    return CreateSuccessfulArchive(request);
                }, uploadAppleApp: request => { uploads++; return CreateSuccessfulUpload(request); });
            var result = service.Execute(spec, new() { ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleSourceCommit = EnsureTestSourceCommit(root), AppleAction = PowerForgeAppleReleaseAction.Upload });
            Assert.False(result.Success);
            Assert.Equal(0, uploads);
            Assert.Equal("Completed", result.AppleReceipt!.OperationPhase);
            Assert.Contains("paused", result.AppleReceipt.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(Assert.Single(result.AppleReceipt.Targets).ArchiveCreated);
            Assert.True(Directory.Exists(result.AppleAppPlan!.Apps.Single().ArchivePath));
        }
        finally { TryDelete(root); }
    }

    [Theory]
    [InlineData("lease")]
    [InlineData("pause")]
    [InlineData("off")]
    [InlineData("expired")]
    [InlineData("invalid-gate")]
    public void Execute_AppleShip_EntryRejectedAfterPreviewDoesNotMutate(string rejection)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            WriteXcodeGenVersionSource(root, "1.2.0", "9");
            AppleDeploymentTestFixture.WriteSharedSchemes(root, "CasaRay");
            var key = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(key, "synthetic-private-key");
            var spec = CreateAppleAutomationSpec(root, key);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            spec.AppleApps.Automation.MarketingVersionPattern = "1.2.X";
            EnableInternalAppleShipTargets(spec);
            var config = Path.Combine(root, "powerforge.release.json");
            File.WriteAllText(config, JsonSerializer.Serialize(spec));
            File.WriteAllText(Path.Combine(root, ".gitignore"), "build/\nstate/\n");
            var source = EnsureTestSourceCommit(root);
            var preview = CreateAppleAutomationService(request => CreateReleaseState(request, processingState: null),
                getHighestAppleBuildNumber: (_, _, _) => 8).Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = config, AppleAction = PowerForgeAppleReleaseAction.Ship, PlanOnly = true,
                    AppleSourceCommit = source, AppleMarketingVersion = "1.2.0", AppleShipTestFlightTargets = ["CasaRay iOS"]
                });
            Assert.True(preview.Success, preview.ErrorMessage);
            var planBytes = File.ReadAllBytes(preview.AppleAppPlan!.PlanReceiptPath);
            // This manual checkout wins the lease after the worker's preview.
            // The actual child CLI must attempt acquisition and positively reject before mutation.
            var state = Directory.CreateDirectory(Path.Combine(root, "state")).FullName;
            using var manual = rejection == "lease" ? AppleReleaseAppState.Acquire(preview.AppleAppPlan, state) : null;
            var environment = new Dictionary<string, string>();
            if (rejection == "pause")
            {
                var app = preview.AppleAppPlan.Apps.Single();
                var directory = Directory.CreateDirectory(Path.Combine(state, "apps", AppleReleaseAppState.Hash(app.BundleId!.ToLowerInvariant()))).FullName;
                File.WriteAllText(Path.Combine(directory, "paused"), "local operator");
            }
            else if (rejection != "lease")
            {
                var gate = Path.Combine(root, "gate.json");
                if (rejection == "invalid-gate") File.WriteAllText(gate, "invalid JSON");
                else AppleReleaseAppState.AtomicWrite(gate, new { Allowed = rejection != "off", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(rejection == "expired" ? -1 : 3) });
                environment["POWERFORGE_APPLE_WORKER_GATE"] = gate;
            }
            var child = AppleTestFlightWorkerTests.RunCliWithEnvironment(root, environment, "apple-release", "Ship", "--config", config,
                "--apple-source-commit", source, "--apple-version", "1.2.0", "--apple-testflight-target", "CasaRay iOS", "--apple-expected-plan-sha256",
                preview.AppleReceipt!.PlanSha256!, "--confirm-apple-action", "--output", "json");
            Assert.Equal(rejection == "invalid-gate" ? 1 : AppleReleaseLeaseContentionException.ExitCode, child.ExitCode);
            Assert.Equal(rejection != "invalid-gate", AppleTestFlightWorker.IsPreMutationRejection(child.ExitCode, child.Output));
            if (rejection != "invalid-gate")
                Assert.Equal(AppleTestFlightJobStage.Planning, AppleTestFlightWorker.FailureStage(AppleTestFlightJobStage.CheckpointExecuting, new AppleTestFlightPreMutationRejectedException()));
            Assert.Equal(planBytes, File.ReadAllBytes(preview.AppleAppPlan.PlanReceiptPath));
            Assert.False(File.Exists(preview.AppleAppPlan.ReceiptPath));
            Assert.False(Directory.Exists(preview.AppleAppPlan.Apps.Single().ArchivePath));
        }
        finally { TryDelete(root); }
    }

    [Theory]
    [InlineData((int)PowerForgeAppleReleaseAction.Version)]
    [InlineData((int)PowerForgeAppleReleaseAction.Ship)]
    public void Execute_AppleVersion_PauseDuringApprovedInventoryPreventsSourceWrite(int actionValue)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            WriteXcodeGenVersionSource(root, "1.2.0", "9");
            AppleDeploymentTestFixture.WriteSharedSchemes(root, "CasaRay");
            var key = Path.Combine(root, "AuthKey_TEST.p8"); File.WriteAllText(key, "synthetic-private-key");
            var spec = CreateAppleAutomationSpec(root, key);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            EnableInternalAppleShipTargets(spec);
            File.WriteAllText(Path.Combine(root, ".gitignore"), "build/\n");
            var source = EnsureTestSourceCommit(root);
            var action = (PowerForgeAppleReleaseAction)actionValue;
            PowerForgeReleaseRequest Request(bool preview, string? hash = null) => new()
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleAction = action,
                AppleMarketingVersion = "1.2.1", AppleSourceCommit = source, PlanOnly = preview,
                AppleShipTestFlightTargets = action == PowerForgeAppleReleaseAction.Ship ? ["CasaRay iOS"] : [], AppleExpectedPlanSha256 = hash, AppleActionConfirmed = true
            };
            var preview = CreateAppleAutomationService(request => CreateReleaseState(request, null),
                getHighestAppleBuildNumber: (_, _, _) => 9).Execute(spec, Request(true));
            Assert.True(preview.Success, preview.ErrorMessage);
            Assert.Equal("1.2.1", preview.AppleReceipt!.Versioning!.MarketingVersion);
            Assert.Equal("10", preview.AppleReceipt.Versioning.BuildNumber);
            if (action == PowerForgeAppleReleaseAction.Ship) Assert.Equal(PowerForgeAppleShipPhase.VersionCheckpoint, preview.AppleReceipt.ShipPhase);
            var before = File.ReadAllBytes(Path.Combine(root, "project.yml"));
            var generated = 0;
            var pauseReached = false;
            var result = CreateAppleAutomationService(request => CreateReleaseState(request, null),
                getHighestAppleBuildNumber: (_, _, _) => { pauseReached |= PauseAppleTargetAfterLeaseAcquired(spec); return 9; },
                generateAppleProject: app =>
                {
                    // Existing-project identity inspection is a no-op; only the
                    // Version checkpoint's regeneration request mutates here.
                    if (app.RegenerateProject) generated++;
                    return false;
                }).Execute(spec, Request(false, preview.AppleReceipt!.PlanSha256));
            Assert.False(result.Success);
            Assert.True(pauseReached);
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "project.yml")));
            Assert.Equal(0, generated);
            Assert.Equal("Completed", result.AppleReceipt!.OperationPhase);
            Assert.Contains("paused", result.AppleReceipt.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { TryDelete(root); }
    }

    [Theory]
    [InlineData((int)PowerForgeAppleReleaseAction.Prepare)]
    [InlineData((int)PowerForgeAppleReleaseAction.SubmitTestFlightReview)]
    [InlineData((int)PowerForgeAppleReleaseAction.SubmitAppReview)]
    [InlineData((int)PowerForgeAppleReleaseAction.Release)]
    public void Execute_AppleRemoteStage_PauseDuringPreflightPreventsNextMutation(int actionValue)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var key = Path.Combine(root, "AuthKey_TEST.p8"); File.WriteAllText(key, "synthetic-private-key");
            var spec = CreateAppleAutomationSpec(root, key);
            spec.AppleApps!.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Apps.Single().TestFlightPolicy = AppleTestFlightPolicy.External;
            var mutations = 0;
            var pauseReached = false;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                generateAppleProject: _ =>
                {
                    pauseReached |= PauseAppleTargetAfterLeaseAcquired(spec);
                    return false;
                },
                prepareAppleDistribution: _ => { mutations++; throw new Exception("Preparation reached after pause"); },
                submitTestFlightBetaReview: _ => { mutations++; throw new Exception("Beta review reached after pause"); },
                submitAppleReview: _ => { mutations++; throw new Exception("App review reached after pause"); },
                releaseAppleVersion: _ => { mutations++; throw new Exception("Release reached after pause"); });
            var result = service.Execute(spec, new() { ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = (PowerForgeAppleReleaseAction)actionValue, AppleActionConfirmed = true });
            Assert.False(result.Success);
            Assert.True(pauseReached);
            Assert.Equal(0, mutations);
            Assert.Equal("Completed", result.AppleReceipt!.OperationPhase);
            Assert.Contains("paused", result.AppleReceipt.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Execute_AppleDirect_PauseDuringExportPreventsNotarizationAndRetainsFailure()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var key = Path.Combine(root, "AuthKey_TEST.p8"); File.WriteAllText(key, "synthetic-private-key");
            var spec = CreateAppleAutomationSpec(root, key);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            var app = spec.AppleApps.Apps.Single();
            app.Platform = ApplePlatform.macOS; app.DistributionRoute = AppleDistributionRoute.DirectNotarized; app.AppStoreConnectAppId = null;
            var notarizations = 0;
            var service = CreateAppleAutomationService(_ => throw new Exception("Direct export must not use App Store state"),
                archiveAppleApp: CreateSuccessfulArchive,
                uploadAppleApp: request =>
                {
                    var artifact = Directory.CreateDirectory(Path.Combine(request.ExportPath!, "CasaRay.app"));
                    File.WriteAllText(Path.Combine(artifact.FullName, "payload"), "synthetic export");
                    PauseAppleTestTarget(spec);
                    return CreateSuccessfulUpload(request);
                }, notarizeAppleArtifact: _ => { notarizations++; throw new Exception("Notarization reached after pause"); });
            var result = service.Execute(spec, new() { ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleAction = PowerForgeAppleReleaseAction.Upload });
            Assert.False(result.Success);
            Assert.Equal(0, notarizations);
            Assert.Equal("Completed", result.AppleReceipt!.OperationPhase);
            Assert.Contains("paused", result.AppleReceipt.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(Assert.Single(result.AppleReceipt.Targets).ArchiveCreated);
        }
        finally { TryDelete(root); }
    }

    private bool PauseAppleTargetAfterLeaseAcquired(PowerForgeReleaseSpec spec)
    {
        var lease = Path.Combine(_appleAppStateRoot, "apps", AppleReleaseAppState.Hash(spec.AppleApps!.Apps.Single().BundleId!.ToLowerInvariant()), "operation.lock");
        if (!File.Exists(lease)) return false;
        try { using var probe = new FileStream(lease, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch (IOException) { PauseAppleTestTarget(spec); return true; }
    }

    private void PauseAppleTestTarget(PowerForgeReleaseSpec spec)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_appleAppStateRoot, "apps", AppleReleaseAppState.Hash(spec.AppleApps!.Apps.Single().BundleId!.ToLowerInvariant()))).FullName;
        File.WriteAllText(Path.Combine(directory, "paused"), "synthetic post-entry pause");
    }

}
