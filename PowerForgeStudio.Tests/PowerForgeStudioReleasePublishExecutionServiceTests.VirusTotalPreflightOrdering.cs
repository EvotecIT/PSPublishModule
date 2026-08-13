using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleasePublishExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ModuleOnlyPublisher_MissingVirusTotalSecret_FailsPreflight()
    {
        var repositoryRoot = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
            var releaseConfig = Path.Combine(buildDirectory, "release.json");
            var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
            var moduleZip = Path.Combine(repositoryRoot, "signed", "Example.1.2.3.zip");
            var missingSecret = $"POWERFORGE_MISSING_{Guid.NewGuid():N}";
            Directory.CreateDirectory(Path.GetDirectoryName(moduleZip)!);
            File.WriteAllText(moduleZip, "signed module");
            File.WriteAllText(
                moduleConfig,
                """
                {
                  "Build": { "Name": "Example", "SourcePath": "." },
                  "Segments": [
                    {
                      "Type": "GalleryNuget",
                      "Configuration": {
                        "Destination": "PowerShellGallery",
                        "Enabled": true
                      }
                    }
                  ]
                }
                """);
            File.WriteAllText(
                releaseConfig,
                $$"""
                {
                  "Module": {
                    "RepositoryRoot": "..",
                    "ConfigPath": "powerforge.json"
                  },
                  "VirusTotal": {
                    "Enabled": true,
                    "ProjectName": "Example",
                    "ApiKeyEnvName": "{{missingSecret}}",
                    "ArtifactKinds": [ "PowerShellModule" ]
                  }
                }
                """);
            var unified = new PowerForgeReleaseResult
            {
                Success = true,
                ConfigPath = releaseConfig,
                ModulePlan = new PowerForgeModuleReleasePlanSummary
                {
                    ModuleName = "Example",
                    ConfigPath = moduleConfig,
                    ModuleVersion = "1.2.3",
                    RunMode = ConfigurationGateMode.Build
                },
                ReleaseAssetEntries =
                [
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = moduleZip,
                        Category = PowerForgeReleaseAssetCategory.Module,
                        Version = "1.2.3",
                        IsFinalPackageOutput = true
                    }
                ]
            };
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Example",
                releaseConfig,
                unified,
                [CreateModuleSigningReceipt(repositoryRoot, moduleZip)]);
            var service = new ReleasePublishExecutionService();

            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true")
                .Set(missingSecret, null);
            var targets = service.BuildPendingTargets([queueItem]);
            Assert.Contains(targets, static target => target.TargetKind == "VirusTotal");

            var result = await service.ExecuteAsync(queueItem);

            var receipt = Assert.Single(result.Receipts);
            Assert.Equal("VirusTotal", receipt.TargetKind);
            Assert.Equal(ReleasePublishReceiptStatus.Failed, receipt.Status);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ModuleOwnedPackage_MissingVirusTotalSecret_BlocksPackagePublisher()
    {
        var repositoryRoot = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
            var releaseConfig = Path.Combine(buildDirectory, "release.json");
            var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
            var packagePath = Path.Combine(repositoryRoot, "signed", "Example.Library.1.2.3.nupkg");
            var missingSecret = $"POWERFORGE_MISSING_{Guid.NewGuid():N}";
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            File.WriteAllText(packagePath, "signed package");
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
                $$"""
                {
                  "Module": {
                    "RepositoryRoot": "..",
                    "ConfigPath": "powerforge.json",
                    "IncludesPackages": true
                  },
                  "VirusTotal": {
                    "Enabled": true,
                    "ProjectName": "Example",
                    "ApiKeyEnvName": "{{missingSecret}}",
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
                        PublishNuget = true,
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
                                    Packages = { packagePath }
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
            var packagePublishCalled = false;
            var unifiedPublishCalled = false;
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                new ProjectBuildPublishHostService(),
                (_, _) =>
                {
                    packagePublishCalled = true;
                    return Task.FromResult(new DotNetNuGetPushResult(
                        0,
                        "published",
                        string.Empty,
                        "dotnet",
                        TimeSpan.Zero,
                        false,
                        null));
                },
                publishUnifiedRelease: (_, _) =>
                {
                    unifiedPublishCalled = true;
                    return new PowerForgeReleaseResult { Success = true };
                });

            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true")
                .Set(missingSecret, null);
            var result = await service.ExecuteAsync(queueItem);

            var receipt = Assert.Single(result.Receipts);
            Assert.Equal("VirusTotal", receipt.TargetKind);
            Assert.Equal(ReleasePublishReceiptStatus.Failed, receipt.Status);
            Assert.False(packagePublishCalled);
            Assert.False(unifiedPublishCalled);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }
}
