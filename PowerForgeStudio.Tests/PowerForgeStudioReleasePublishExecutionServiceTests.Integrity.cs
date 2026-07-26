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
    public async Task ExecuteAsync_omits_unified_zip_target_when_github_publication_is_disabled()
    {
        var repositoryRoot = CreateIntegrityRepository(
            """{ "GitHub": { "Publish": false }, "Tools": { "Targets": [] } }""",
            out var releaseConfig);
        try
        {
            var zipPath = Path.Combine(repositoryRoot, "Artifacts", "Sample.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            File.WriteAllText(zipPath, "build-only");
            var queueItem = CreateIntegrityQueueItem(
                repositoryRoot,
                releaseConfig,
                new PowerForgeReleaseResult {
                    Success = true,
                    ConfigPath = releaseConfig,
                    ReleaseAssets = [zipPath]
                },
                [
                    ReleaseSigningArtifactIntegrity.Capture(new ReleaseSigningReceipt(
                        repositoryRoot,
                        "BuildOnlyRepo",
                        ReleaseBuildAdapterKind.ToolBuild.ToString(),
                        zipPath,
                        "File",
                        ReleaseSigningReceiptStatus.Signed,
                        "Signed.",
                        DateTimeOffset.UtcNow))
                ]);
            var service = new ReleasePublishExecutionService();

            Assert.Empty(service.BuildPendingTargets([queueItem]));

            var result = await service.ExecuteAsync(queueItem);
            Assert.True(result.Succeeded);
            Assert.Equal(ReleasePublishReceiptStatus.Skipped, Assert.Single(result.Receipts).Status);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_rejects_checkpointed_asset_modified_after_signing()
    {
        var repositoryRoot = CreateIntegrityRepository(
            """{ "GitHub": { "Publish": true, "Owner": "EvotecIT", "Repository": "TamperRepo" } }""",
            out var releaseConfig);
        try
        {
            var assetPath = Path.Combine(repositoryRoot, "Artifacts", "TamperRepo.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            File.WriteAllText(assetPath, "signed");
            var receipt = ReleaseSigningArtifactIntegrity.Capture(new ReleaseSigningReceipt(
                repositoryRoot,
                "TamperRepo",
                ReleaseBuildAdapterKind.ToolBuild.ToString(),
                assetPath,
                "File",
                ReleaseSigningReceiptStatus.Signed,
                "Signed.",
                DateTimeOffset.UtcNow));
            var queueItem = CreateIntegrityQueueItem(
                repositoryRoot,
                releaseConfig,
                new PowerForgeReleaseResult {
                    Success = true,
                    ConfigPath = releaseConfig,
                    ReleaseAssets = [assetPath]
                },
                [receipt]);
            File.WriteAllText(assetPath, "tampered");
            var publishCalls = 0;
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                new ProjectBuildPublishHostService(),
                (request, _) => Task.FromResult(new DotNetNuGetPushResult(
                    0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, false, null)),
                publishUnifiedRelease: (_, _) =>
                {
                    publishCalls++;
                    return new PowerForgeReleaseResult { Success = true };
                });

            var target = Assert.Single(service.BuildPendingTargets([queueItem]));
            Assert.Equal("ConfigurationError", target.TargetKind);
            Assert.Contains("changed after approval", target.Destination, StringComparison.OrdinalIgnoreCase);

            var result = await service.ExecuteAsync(queueItem);
            Assert.False(result.Succeeded);
            Assert.Equal(0, publishCalls);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    private static string CreateIntegrityRepository(string releaseJson, out string releaseConfig)
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForgeStudio.Tests",
            Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(releaseConfig, releaseJson);
        return repositoryRoot;
    }

    private static ReleaseQueueItem CreateIntegrityQueueItem(
        string repositoryRoot,
        string releaseConfig,
        PowerForgeReleaseResult unified,
        IReadOnlyList<ReleaseSigningReceipt> receipts)
    {
        var build = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(unified),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
        var signing = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(build),
            receipts);
        return new ReleaseQueueItem(
            repositoryRoot,
            "IntegrityRepo",
            ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository,
            1,
            ReleaseQueueStage.Publish,
            ReleaseQueueItemStatus.ReadyToRun,
            "Ready.",
            "publish.ready",
            JsonSerializer.Serialize(signing),
            DateTimeOffset.UtcNow);
    }
}
