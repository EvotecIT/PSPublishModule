using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleasePublishExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_publishes_direct_json_module_package_lanes_from_checkpoint()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForgeStudio.Tests",
            Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
            File.WriteAllText(
                moduleConfig,
                """
                {
                  "Build": { "Name": "DirectPackages", "SourcePath": "." },
                  "Segments": [
                    {
                      "Type": "PackageBuild",
                      "Configuration": {
                        "Name": "Direct packages",
                        "StagingPath": "PackageArtifacts",
                        "PublishNuget": true,
                        "PublishApiKey": "test-key",
                        "PublishSource": "https://example.test/v3/index.json"
                      }
                    }
                  ]
                }
                """);
            var packageDirectory = Directory.CreateDirectory(
                Path.Combine(repositoryRoot, "PackageArtifacts")).FullName;
            var packagePath = Path.Combine(packageDirectory, "Direct.Library.1.0.0.nupkg");
            File.WriteAllText(packagePath, "signed package");
            var checkpoint = new PowerForgeReleaseResult
            {
                Success = true,
                ConfigPath = moduleConfig,
                ModulePackagePlans =
                [
                    new PowerForgeModulePackageReleaseCheckpoint
                    {
                        Key = "PackageBuild:0",
                        Name = "Direct packages",
                        ConfigPath = moduleConfig,
                        Release = new DotNetRepositoryReleaseResult
                        {
                            Success = true,
                            Projects =
                            {
                                new DotNetRepositoryProjectResult
                                {
                                    ProjectName = "Direct.Library",
                                    PackageId = "Direct.Library",
                                    NewVersion = "1.0.0",
                                    Packages = { packagePath }
                                }
                            }
                        }
                    }
                ]
            };
            var buildResult = new ReleaseBuildExecutionResult(
                repositoryRoot,
                true,
                "Build completed.",
                1,
                [],
                UnifiedReleaseStateJson: JsonSerializer.Serialize(checkpoint),
                ModuleBuildConfigSha256:
                    UnifiedReleaseConfigFingerprint.ComputeModuleConfig(moduleConfig));
            var signingReceipt = ReleaseSigningArtifactIntegrity.Capture(
                new ReleaseSigningReceipt(
                    repositoryRoot,
                    "DirectPackages",
                    ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    packagePath,
                    "File",
                    ReleaseSigningReceiptStatus.Signed,
                    "Signed.",
                    DateTimeOffset.UtcNow));
            var signingResult = new ReleaseSigningExecutionResult(
                repositoryRoot,
                true,
                "Signing completed.",
                JsonSerializer.Serialize(buildResult),
                [signingReceipt]);
            var queueItem = new ReleaseQueueItem(
                repositoryRoot,
                "DirectPackages",
                ReleaseRepositoryKind.Library,
                ReleaseWorkspaceKind.PrimaryRepository,
                1,
                ReleaseQueueStage.Publish,
                ReleaseQueueItemStatus.ReadyToRun,
                "Ready.",
                "publish.ready",
                JsonSerializer.Serialize(signingResult),
                DateTimeOffset.UtcNow);
            DotNetNuGetPushRequest? captured = null;
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                new ProjectBuildPublishHostService(),
                (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new DotNetNuGetPushResult(
                        0,
                        "published",
                        string.Empty,
                        "dotnet",
                        TimeSpan.Zero,
                        false,
                        null));
                });

            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var pending = service.BuildPendingTargets([queueItem]);
            var result = await service.ExecuteAsync(queueItem);

            Assert.Contains(pending, target => target.TargetKind == "ModulePackages");
            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal(packagePath, captured!.PackagePath);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_matches_module_package_archives_to_exact_project_identities()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForgeStudio.Tests",
            Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
            var releaseConfig = Path.Combine(buildDirectory, "release.json");
            var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
            File.WriteAllText(
                moduleConfig,
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "." },
                  "Segments": [
                    {
                      "Type": "PackageBuild",
                      "Configuration": {
                        "Name": "Sample packages",
                        "RootPath": ".",
                        "PublishGitHub": true,
                        "GitHubAccessToken": "test-token",
                        "GitHubUsername": "EvotecIT",
                        "GitHubRepositoryName": "Sample"
                      }
                    }
                  ]
                }
                """);
            File.WriteAllText(
                releaseConfig,
                """{ "Module": { "RepositoryRoot": "..", "ConfigPath": "powerforge.json", "IncludesPackages": true } }""");
            var fooZip = Path.Combine(repositoryRoot, "Foo.1.0.0.zip");
            var fooBarZip = Path.Combine(repositoryRoot, "Foo.Bar.1.0.0.zip");
            File.WriteAllText(fooZip, "foo");
            File.WriteAllText(fooBarZip, "foo-bar");
            var unified = new PowerForgeReleaseResult
            {
                Success = true,
                ConfigPath = releaseConfig,
                ModulePackagePlans =
                [
                    new PowerForgeModulePackageReleaseCheckpoint
                    {
                        Name = "Sample packages",
                        ConfigPath = moduleConfig,
                        Release = new DotNetRepositoryReleaseResult
                        {
                            Success = true,
                            Projects =
                            {
                                new DotNetRepositoryProjectResult
                                {
                                    ProjectName = "Foo",
                                    PackageId = "Foo",
                                    IsPackable = true,
                                    NewVersion = "1.0.0",
                                    ReleaseZipPath = fooZip
                                },
                                new DotNetRepositoryProjectResult
                                {
                                    ProjectName = "Foo.Bar",
                                    PackageId = "Foo.Bar",
                                    IsPackable = true,
                                    NewVersion = "1.0.0",
                                    ReleaseZipPath = fooBarZip
                                }
                            }
                        }
                    }
                ]
            };
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                unified,
                [
                    CreateModuleSigningReceipt(repositoryRoot, fooBarZip),
                    CreateModuleSigningReceipt(repositoryRoot, fooZip)
                ]);
            ProjectBuildGitHubPublishRequest? captured = null;
            var publishHost = new ProjectBuildPublishHostService(
                new NullLogger(),
                request =>
                {
                    captured = request;
                    return new ProjectBuildGitHubPublishSummary
                    {
                        Success = true,
                        SummaryReleaseUrl = "https://example.test/release"
                    };
                });
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                publishHost,
                (_, _) => Task.FromResult(new DotNetNuGetPushResult(
                    0,
                    string.Empty,
                    string.Empty,
                    "dotnet",
                    TimeSpan.Zero,
                    false,
                    null)),
                publishUnifiedRelease: (_, _) => new PowerForgeReleaseResult { Success = true });

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal(
                fooZip,
                captured!.Release.Projects.Single(project => project.ProjectName == "Foo").ReleaseZipPath);
            Assert.Equal(
                fooBarZip,
                captured.Release.Projects.Single(project => project.ProjectName == "Foo.Bar").ReleaseZipPath);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    private static ReleaseSigningReceipt CreateModuleSigningReceipt(
        string repositoryRoot,
        string path)
        => new(
            repositoryRoot,
            "Sample",
            ReleaseBuildAdapterKind.ModuleBuild.ToString(),
            path,
            "File",
            ReleaseSigningReceiptStatus.Signed,
            "Signed.",
            DateTimeOffset.UtcNow);
}
