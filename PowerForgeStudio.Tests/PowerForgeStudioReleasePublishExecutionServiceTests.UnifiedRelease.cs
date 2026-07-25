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
    public async Task ExecuteAsync_UnifiedRelease_PublishesTopLevelGitHubThroughSharedEngine()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "Tools": {
                "ProjectRoot": "..",
                "Targets": []
              },
              "GitHub": {
                "Publish": true,
                "Owner": "EvotecIT",
                "Repository": "UnifiedRepo"
              }
            }
            """);
        var zipPath = Path.Combine(repositoryRoot, "Artifacts", "UnifiedRepo.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        File.WriteAllText(zipPath, "zip");
        var buildResult = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [
                new ReleaseBuildAdapterResult(
                    ReleaseBuildAdapterKind.ToolBuild,
                    true,
                    "Tool build completed.",
                    0,
                    1,
                    [],
                    [zipPath])
            ],
            UnifiedReleaseStateJson: "{}");
        var signingResult = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(buildResult),
            [
                new ReleaseSigningReceipt(
                    repositoryRoot,
                    "UnifiedRepo",
                    ReleaseBuildAdapterKind.ToolBuild.ToString(),
                    zipPath,
                    "File",
                    ReleaseSigningReceiptStatus.Signed,
                    "Signed.",
                    DateTimeOffset.UtcNow)
            ]);
        var queueItem = new ReleaseQueueItem(
            repositoryRoot,
            "UnifiedRepo",
            ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository,
            1,
            ReleaseQueueStage.Publish,
            ReleaseQueueItemStatus.ReadyToRun,
            "Ready.",
            "publish.ready",
            JsonSerializer.Serialize(signingResult),
            DateTimeOffset.UtcNow);
        string? capturedConfig = null;
        string? capturedState = null;
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, false, null)),
            publishUnifiedGitHub: (configPath, stateJson) =>
            {
                capturedConfig = configPath;
                capturedState = stateJson;
                return new PowerForgeReleaseResult {
                    Success = true,
                    UnifiedGitHubRelease = new PowerForgeUnifiedGitHubReleaseResult {
                        Owner = "EvotecIT",
                        Repository = "UnifiedRepo",
                        TagName = "v1.0.0",
                        Success = true,
                        ReleaseUrl = "https://github.com/EvotecIT/UnifiedRepo/releases/tag/v1.0.0",
                        AssetPaths = [zipPath]
                    }
                };
            });

        try
        {
            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.Equal(releaseConfig, capturedConfig);
            Assert.Equal("{}", capturedState);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal(ReleasePublishReceiptStatus.Published, receipt.Status);
            Assert.Equal("Unified GitHub release", receipt.TargetName);
            Assert.Equal("GitHub", receipt.TargetKind);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }
}
