using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleasePublishExecutionServiceTests
{
    [Fact]
    public void CreateUnifiedReleaseBuildRequest_EnablesCheckpointModuleProvenance()
    {
        var root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var releaseConfig = Path.Combine(root, "release.json");
        File.WriteAllText(releaseConfig, "{}");
        try
        {
            var request = ReleaseBuildExecutionService.CreateUnifiedReleaseBuildRequest(
                releaseConfig,
                "pwsh",
                Path.Combine(root, "staging"));

            Assert.True(request.CaptureModuleArtifactProvenance);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(true, 1, ReleasePublishReceiptStatus.Published)]
    [InlineData(false, 1, ReleasePublishReceiptStatus.Failed)]
    [InlineData(true, 0, ReleasePublishReceiptStatus.Skipped)]
    public async Task ExecuteAsync_UnifiedRelease_SurfacesVirusTotalReceipt(
        bool monitorSuccess,
        int artifactCount,
        ReleasePublishReceiptStatus expectedStatus)
    {
        var repositoryRoot = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "VirusTotal": {
                "Enabled": true,
                "ApiKeyEnvName": "VIRUSTOTAL_MONITOR_API_KEY",
                "ArtifactKinds": [ "MsiPackage" ]
              }
            }
            """);
        var buildResult = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(new PowerForgeReleaseResult
            {
                Success = true,
                ConfigPath = releaseConfig
            }),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
        var signingResult = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(buildResult),
            []);
        var queueItem = new ReleaseQueueItem(
            repositoryRoot,
            "Example",
            ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository,
            1,
            ReleaseQueueStage.Publish,
            ReleaseQueueItemStatus.ReadyToRun,
            "Ready.",
            "publish.ready",
            JsonSerializer.Serialize(signingResult),
            DateTimeOffset.UtcNow);
        var receiptPath = Path.Combine(repositoryRoot, "Artifacts", "Release", "virustotal-monitor-receipt.json");
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, false, null)),
            publishUnifiedRelease: (_, _) => new PowerForgeReleaseResult
            {
                Success = true,
                VirusTotalMonitorReceiptPath = receiptPath,
                VirusTotalMonitor = new VirusTotalMonitorPublishResult
                {
                    Success = monitorSuccess,
                    ErrorMessage = monitorSuccess ? null : "Monitor upload failed.",
                    Artifacts = artifactCount == 0
                        ? []
                        :
                        [
                            new VirusTotalMonitorArtifactReceipt
                            {
                                SourcePath = Path.Combine(repositoryRoot, "Example.msi"),
                                MonitorId = "monitor-id"
                            }
                        ]
                }
            });

        try
        {
            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true")
                .Set("VIRUSTOTAL_MONITOR_API_KEY", "test-key");
            var result = await service.ExecuteAsync(queueItem);

            var receipt = Assert.Single(result.Receipts);
            Assert.Equal("VirusTotal Monitor", receipt.TargetName);
            Assert.Equal("VirusTotal", receipt.TargetKind);
            Assert.Equal(expectedStatus, receipt.Status);
            Assert.Equal(receiptPath, receipt.Destination);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedRelease_MissingVirusTotalSecret_UsesVirusTotalReceipt()
    {
        var repositoryRoot = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        var secretName = $"POWERFORGE_MISSING_{Guid.NewGuid():N}";
        File.WriteAllText(
            releaseConfig,
            $$"""
            {
              "VirusTotal": {
                "Enabled": true,
                "ApiKeyEnvName": "{{secretName}}",
                "ArtifactKinds": [ "MsiPackage" ]
              }
            }
            """);
        var buildResult = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(new PowerForgeReleaseResult
            {
                Success = true,
                ConfigPath = releaseConfig
            }),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
        var signingResult = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(buildResult),
            []);
        var queueItem = new ReleaseQueueItem(
            repositoryRoot,
            "Example",
            ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository,
            1,
            ReleaseQueueStage.Publish,
            ReleaseQueueItemStatus.ReadyToRun,
            "Ready.",
            "publish.ready",
            JsonSerializer.Serialize(signingResult),
            DateTimeOffset.UtcNow);
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, false, null)));

        try
        {
            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true")
                .Set(secretName, null);
            var result = await service.ExecuteAsync(queueItem);

            var receipt = Assert.Single(result.Receipts);
            Assert.Equal("VirusTotal Monitor", receipt.TargetName);
            Assert.Equal("VirusTotal", receipt.TargetKind);
            Assert.Equal(ReleasePublishReceiptStatus.Failed, receipt.Status);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }
}
