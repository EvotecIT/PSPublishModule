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
            UnifiedReleaseStateJson: "{}",
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
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
            publishUnifiedRelease: (configPath, stateJson) =>
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

    [Fact]
    public async Task ExecuteAsync_UnifiedRelease_PublishesNonZipToolAsset()
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
        var executablePath = Path.Combine(repositoryRoot, "Artifacts", "UnifiedRepo.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, "signed");
        var unified = new PowerForgeReleaseResult {
            Success = true,
            ConfigPath = releaseConfig,
            ReleaseAssets = [executablePath],
            ReleaseAssetEntries = [
                new PowerForgeReleaseAssetEntry {
                    Path = executablePath,
                    Category = PowerForgeReleaseAssetCategory.Tool
                }
            ]
        };
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
                    [executablePath])
            ],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(unified),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
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
                    executablePath,
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
        string? capturedState = null;
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, false, null)),
            publishUnifiedRelease: (_, stateJson) =>
            {
                capturedState = stateJson;
                return new PowerForgeReleaseResult {
                    Success = true,
                    UnifiedGitHubRelease = new PowerForgeUnifiedGitHubReleaseResult {
                        Owner = "EvotecIT",
                        Repository = "UnifiedRepo",
                        TagName = "v1.0.0",
                        Success = true,
                        AssetPaths = [executablePath]
                    }
                };
            });

        try
        {
            var targets = service.BuildPendingTargets([queueItem]);
            var target = Assert.Single(targets);
            Assert.Equal("GitHub", target.TargetKind);
            Assert.Equal(executablePath, target.SourcePath);

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.NotNull(capturedState);
            Assert.Equal(ReleasePublishReceiptStatus.Published, Assert.Single(result.Receipts).Status);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedRelease_ProjectsAndPublishesWingetSubmission()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "Winget": {
                "Enabled": true,
                "Submit": true,
                "Packages": []
              }
            }
            """);
        var manifestPath = Path.Combine(repositoryRoot, "Artifacts", "Winget", "EvotecIT.Tool.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, "PackageIdentifier: EvotecIT.Tool");
        var unified = new PowerForgeReleaseResult {
            Success = true,
            ConfigPath = releaseConfig,
            WingetManifestPaths = [manifestPath]
        };
        var buildResult = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(unified),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
        var signingResult = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(buildResult),
            []);
        var queueItem = new ReleaseQueueItem(
            repositoryRoot,
            "WingetRepo",
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
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, false, null)),
            publishUnifiedRelease: (_, _) => new PowerForgeReleaseResult {
                Success = true,
                WingetManifestPaths = [manifestPath],
                WingetSubmission = new PowerForgeWingetSubmissionResult {
                    Succeeded = true
                }
            });

        try
        {
            var target = Assert.Single(service.BuildPendingTargets([queueItem]));
            Assert.Equal("Winget", target.TargetKind);
            Assert.Equal(manifestPath, target.SourcePath);

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal("Winget", receipt.TargetKind);
            Assert.Equal(ReleasePublishReceiptStatus.Published, receipt.Status);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedRelease_MalformedContractReturnsProjectionFailure()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(releaseConfig, """{ "GitHub": { "Publish": true } }""");
        var assetPath = Path.Combine(repositoryRoot, "Artifacts", "Tool");
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllText(assetPath, "signed");
        var unified = new PowerForgeReleaseResult {
            Success = true,
            ConfigPath = releaseConfig,
            ReleaseAssets = [assetPath]
        };
        var buildResult = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(unified),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
        var signingResult = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(buildResult),
            []);
        var queueItem = new ReleaseQueueItem(
            repositoryRoot,
            "BrokenUnifiedRepo",
            ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository,
            1,
            ReleaseQueueStage.Publish,
            ReleaseQueueItemStatus.ReadyToRun,
            "Ready.",
            "publish.ready",
            JsonSerializer.Serialize(signingResult),
            DateTimeOffset.UtcNow);
        File.WriteAllText(releaseConfig, "{ malformed");
        var service = new ReleasePublishExecutionService();

        try
        {
            var target = Assert.Single(service.BuildPendingTargets([queueItem]));
            Assert.Equal("ConfigurationError", target.TargetKind);

            var result = await service.ExecuteAsync(queueItem);

            Assert.False(result.Succeeded);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal(ReleasePublishReceiptStatus.Failed, receipt.Status);
            Assert.Equal("Configuration", receipt.TargetKind);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedRelease_ValidContractDriftRequiresRebuild()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """{ "GitHub": { "Publish": true, "Owner": "EvotecIT", "Repository": "Original" } }""");
        var unified = new PowerForgeReleaseResult {
            Success = true,
            ConfigPath = releaseConfig
        };
        var buildResult = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(unified),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
        var signingResult = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(buildResult),
            []);
        var queueItem = new ReleaseQueueItem(
            repositoryRoot,
            "DriftedUnifiedRepo",
            ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository,
            1,
            ReleaseQueueStage.Publish,
            ReleaseQueueItemStatus.ReadyToRun,
            "Ready.",
            "publish.ready",
            JsonSerializer.Serialize(signingResult),
            DateTimeOffset.UtcNow);
        File.WriteAllText(
            releaseConfig,
            """{ "GitHub": { "Publish": true, "Owner": "EvotecIT", "Repository": "Changed" } }""");
        var publishCalls = 0;
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, false, null)),
            publishUnifiedRelease: (_, _) =>
            {
                publishCalls++;
                return new PowerForgeReleaseResult { Success = true };
            });

        try
        {
            var target = Assert.Single(service.BuildPendingTargets([queueItem]));
            Assert.Equal("ConfigurationError", target.TargetKind);
            Assert.Contains("changed after the build checkpoint", target.Destination, StringComparison.OrdinalIgnoreCase);

            var result = await service.ExecuteAsync(queueItem);

            Assert.False(result.Succeeded);
            Assert.Equal(0, publishCalls);
            Assert.Equal(ReleasePublishReceiptStatus.Failed, Assert.Single(result.Receipts).Status);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedRelease_DisabledAppleAppsDoNotProjectPublishTarget()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "AppleApps": {
                "Archive": true,
                "Apps": [
                  { "Name": "Disabled", "Enabled": false, "BundleId": "com.evotecit.disabled" }
                ]
              }
            }
            """);
        var buildResult = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(new PowerForgeReleaseResult {
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
            "DisabledAppleRepo",
            ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository,
            1,
            ReleaseQueueStage.Publish,
            ReleaseQueueItemStatus.ReadyToRun,
            "Ready.",
            "publish.ready",
            JsonSerializer.Serialize(signingResult),
            DateTimeOffset.UtcNow);
        var service = new ReleasePublishExecutionService();

        try
        {
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
    public async Task ExecuteAsync_UnifiedRelease_ProjectsAndPublishesAppleActions()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "AppleApps": {
                "Apps": [
                  {
                    "Name": "Sample iOS",
                    "Enabled": true,
                    "BundleId": "com.evotecit.sample"
                  }
                ]
              }
            }
            """);
        var unified = new PowerForgeReleaseResult {
            Success = true,
            ConfigPath = releaseConfig
        };
        var buildResult = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(unified),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
        var signingResult = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(buildResult),
            []);
        var queueItem = new ReleaseQueueItem(
            repositoryRoot,
            "AppleRepo",
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
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, false, null)),
            publishUnifiedRelease: (_, _) => new PowerForgeReleaseResult {
                Success = true,
                AppleApps = [
                    new PowerForgeAppleAppReleaseResult {
                        Success = true,
                        Plan = new PowerForgeAppleAppReleaseTargetPlan {
                            Name = "Sample iOS",
                            BundleId = "com.evotecit.sample"
                        }
                    }
                ]
            });

        try
        {
            var target = Assert.Single(service.BuildPendingTargets([queueItem]));
            Assert.Equal("Apple", target.TargetKind);

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal("Apple", receipt.TargetKind);
            Assert.Equal(ReleasePublishReceiptStatus.Published, receipt.Status);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }
}
