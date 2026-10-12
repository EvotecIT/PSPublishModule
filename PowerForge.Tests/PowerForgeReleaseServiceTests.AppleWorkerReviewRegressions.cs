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
    public void Execute_AppleShip_LeaseAcquiredAfterPreviewRejectsChildBeforeMutation()
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
            using var manual = AppleReleaseAppState.Acquire(preview.AppleAppPlan, Path.Combine(root, "state"));
            var child = AppleTestFlightWorkerTests.RunCli(root, "apple-release", "Ship", "--config", config,
                "--apple-source-commit", source, "--apple-version", "1.2.0", "--apple-testflight-target", "CasaRay iOS", "--apple-expected-plan-sha256",
                preview.AppleReceipt!.PlanSha256!, "--confirm-apple-action", "--output", "json");
            Assert.Equal(AppleReleaseLeaseContentionException.ExitCode, child.ExitCode);
            Assert.True(AppleTestFlightWorker.IsLeaseRejection(child.ExitCode, child.Output), child.Output + child.Error);
            Assert.Equal(planBytes, File.ReadAllBytes(preview.AppleAppPlan.PlanReceiptPath));
            Assert.False(File.Exists(preview.AppleAppPlan.ReceiptPath));
            Assert.False(Directory.Exists(preview.AppleAppPlan.Apps.Single().ArchivePath));
        }
        finally { TryDelete(root); }
    }

}
