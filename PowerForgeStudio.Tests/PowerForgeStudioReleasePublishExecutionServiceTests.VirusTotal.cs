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
        var artifactPath = Path.Combine(repositoryRoot, "Example.msi");
        File.WriteAllText(artifactPath, "signed installer");
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
                ConfigPath = releaseConfig,
                ReleaseAssetEntries =
                [
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = artifactPath,
                        Category = PowerForgeReleaseAssetCategory.Installer,
                        Version = "1.2.3",
                        IsFinalPackageOutput = true
                    }
                ]
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
        var artifactPath = Path.Combine(repositoryRoot, "Example.msi");
        File.WriteAllText(artifactPath, "signed installer");
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
                ConfigPath = releaseConfig,
                ReleaseAssetEntries =
                [
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = artifactPath,
                        Category = PowerForgeReleaseAssetCategory.Installer,
                        Version = "1.2.3",
                        IsFinalPackageOutput = true
                    }
                ]
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

    [Fact]
    public async Task ExecuteAsync_UnifiedRelease_AppleOnlyCheckpoint_SkipsVirusTotalPreflight()
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
              "AppleApps": { "Apps": [] },
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
                ConfigPath = releaseConfig,
                AppleAppPlan = new PowerForgeAppleReleasePlan(),
                ReleaseAssetEntries =
                [
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = Path.Combine(repositoryRoot, "build-only.json"),
                        Category = PowerForgeReleaseAssetCategory.Other,
                        IsFinalPackageOutput = false
                    }
                ]
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
        var publishCalled = false;
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, false, null)),
            publishUnifiedRelease: (_, _) =>
            {
                publishCalled = true;
                return new PowerForgeReleaseResult { Success = true };
            });

        try
        {
            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true")
                .Set(secretName, null);
            var targets = service.BuildPendingTargets([queueItem]);
            Assert.DoesNotContain(targets, static target =>
                target.TargetKind.Equals("VirusTotal", StringComparison.Ordinal));

            var result = await service.ExecuteAsync(queueItem);

            Assert.False(publishCalled);
            Assert.DoesNotContain(result.Receipts, static receipt =>
                receipt.TargetName.Equals("VirusTotal Monitor", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedRelease_ModuleOwnedPackage_IsMaterializedForVirusTotal()
    {
        var repositoryRoot = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
            var releaseConfig = Path.Combine(buildDirectory, "release.json");
            var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
            var unsignedDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "unsigned")).FullName;
            var packagePath = Path.Combine(repositoryRoot, "signed", "Example.Library.1.2.3.nupkg");
            var checkpointPackagePath = Path.Combine(unsignedDirectory, "Example.Library.1.2.3.nupkg");
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            File.WriteAllText(packagePath, "signed package");
            File.WriteAllText(checkpointPackagePath, "unsigned package");
            File.WriteAllText(
                moduleConfig,
                """
                {
                  "Build": { "Name": "Example", "SourcePath": "." },
                  "Segments": [
                    {
                      "Type": "PackageBuild",
                      "Configuration": {
                        "Name": "Example packages",
                        "PublishNuget": true,
                        "PublishApiKey": "test-key",
                        "PublishSource": "https://example.test/v3/index.json"
                      }
                    }
                  ]
                }
                """);
            File.WriteAllText(
                releaseConfig,
                """
                {
                  "Module": {
                    "RepositoryRoot": "..",
                    "ConfigPath": "powerforge.json",
                    "IncludesPackages": true
                  },
                  "VirusTotal": {
                    "Enabled": true,
                    "ProjectName": "Example",
                    "ApiKeyEnvName": "VIRUSTOTAL_MONITOR_API_KEY",
                    "ArtifactKinds": [ "NuGetPackage" ]
                  }
                }
                """);
            var unified = new PowerForgeReleaseResult
            {
                Success = true,
                ConfigPath = releaseConfig,
                ModulePackagePlans =
                [
                    new PowerForgeModulePackageReleaseCheckpoint
                    {
                        Key = "PackageBuild:0",
                        Name = "Example packages",
                        ConfigPath = moduleConfig,
                        Release = new DotNetRepositoryReleaseResult
                        {
                            Success = true,
                            Projects =
                            {
                                new DotNetRepositoryProjectResult
                                {
                                    ProjectName = "Example.Library",
                                    PackageId = "Example.Library",
                                    NewVersion = "1.2.3",
                                    Packages = { checkpointPackagePath }
                                }
                            }
                        }
                    }
                ]
            };
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Example",
                releaseConfig,
                unified,
                [CreateModuleSigningReceipt(repositoryRoot, packagePath)]);
            PowerForgeReleaseResult? capturedCheckpoint = null;
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                new ProjectBuildPublishHostService(),
                (_, _) => Task.FromResult(new DotNetNuGetPushResult(
                    0,
                    "published",
                    string.Empty,
                    "dotnet",
                    TimeSpan.Zero,
                    false,
                    null)),
                publishUnifiedRelease: (_, stateJson) =>
                {
                    capturedCheckpoint = JsonSerializer.Deserialize<PowerForgeReleaseResult>(stateJson);
                    return new PowerForgeReleaseResult
                    {
                        Success = true,
                        VirusTotalMonitor = new VirusTotalMonitorPublishResult { Success = true }
                    };
                });

            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true")
                .Set("VIRUSTOTAL_MONITOR_API_KEY", "test-key");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded, result.Summary);
            var checkpoint = Assert.IsType<PowerForgeReleaseResult>(capturedCheckpoint);
            var package = Assert.Single(checkpoint.ModulePackagePlans)
                .Release.Projects.Single().Packages.Single();
            Assert.Equal(packagePath, package);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }
}
