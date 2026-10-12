namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_AppleUpload_BindsNativeExportOutputForNextPinnedEntry()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "synthetic private-key fixture");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.CleanupAfterProcessing = false;
            var states = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, ++states == 1 ? null : "VALID"),
                archiveAppleApp: CreateSuccessfulArchive,
                uploadAppleApp: request => new AppleAppArchiveService(new NativeUploadBoundary())
                    .UploadArchiveAsync(request).GetAwaiter().GetResult());
            var source = EnsureTestSourceCommit(root);
            var result = service.Execute(spec, new()
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleSourceCommit = source,
                AppleAction = PowerForgeAppleReleaseAction.Upload
            });
            Assert.True(result.Success, result.ErrorMessage);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            var export = Assert.Single(result.AppleApps).Upload!.ExportPath;
            Assert.True(File.Exists(Path.Combine(export, "ExportOptions.plist")));
            Assert.Equal(Path.GetRelativePath(root, export).Replace('\\', '/'), target.UploadExportPath);
            Assert.Equal(AppleNotarizationService.ComputeArtifactSha256(export), target.UploadExportSha256);
            var evidence = service.ValidateLocalAppleEvidence(result.AppleAppPlan!);
            Assert.True(evidence.Success, evidence.ErrorMessage);
            Assert.Contains(Path.Combine(export, "ExportOptions.plist"), evidence.EvidencePaths);
        }
        finally { TryDelete(root); }
    }

    private sealed class NativeUploadBoundary : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Assert.Contains("-exportArchive", request.Arguments);
            request.InvokePreStartBoundary();
            request.InvokeStartBoundary();
            return Task.FromResult(new ProcessRunResult(0, "synthetic upload accepted", string.Empty,
                request.FileName, TimeSpan.Zero, false));
        }
    }
}
