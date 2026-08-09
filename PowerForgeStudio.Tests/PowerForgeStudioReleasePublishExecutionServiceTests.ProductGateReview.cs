using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleasePublishExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_keeps_duplicate_named_signed_packages_in_their_checkpointed_lanes()
    {
        var repositoryRoot = CreateReviewRepository(out var releaseConfig);
        try
        {
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
                        "Name": "Shared packages",
                        "RootPath": ".",
                        "PublishGitHub": true,
                        "CreateReleaseZip": true,
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
            var laneA = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Signed", "LaneA")).FullName;
            var laneB = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Signed", "LaneB")).FullName;
            var packageA = Path.Combine(laneA, "Shared.1.0.0.nupkg");
            var packageB = Path.Combine(laneB, "Shared.1.0.0.nupkg");
            var zipA = Path.Combine(laneA, "Shared.1.0.0.zip");
            var zipB = Path.Combine(laneB, "Shared.1.0.0.zip");
            File.WriteAllText(packageA, "lane-a");
            File.WriteAllText(packageB, "lane-b");
            File.WriteAllText(zipA, "lane-a-zip");
            File.WriteAllText(zipB, "lane-b-zip");
            var checkpointedPlan = new DotNetRepositoryReleaseResult
            {
                Success = true,
                Projects =
                {
                    new DotNetRepositoryProjectResult
                    {
                        ProjectName = "LaneA",
                        PackageId = "Shared",
                        NewVersion = "1.0.0",
                        Packages = { packageA },
                        ReleaseZipPath = zipA
                    },
                    new DotNetRepositoryProjectResult
                    {
                        ProjectName = "LaneB",
                        PackageId = "Shared",
                        NewVersion = "1.0.0",
                        Packages = { packageB },
                        ReleaseZipPath = zipB
                    }
                }
            };
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult
                {
                    Success = true,
                    ConfigPath = releaseConfig,
                    ModulePackagePlans =
                    [
                        new PowerForgeModulePackageReleaseCheckpoint
                        {
                            Key = "PackageBuild:0",
                            Name = "Shared packages",
                            ConfigPath = moduleConfig,
                            Release = checkpointedPlan
                        }
                    ]
                },
                [
                    CreateSigningReceipt(
                        repositoryRoot,
                        packageA,
                        "File",
                        ReleaseBuildAdapterKind.ModuleBuild),
                    CreateSigningReceipt(
                        repositoryRoot,
                        packageB,
                        "File",
                        ReleaseBuildAdapterKind.ModuleBuild),
                    CreateSigningReceipt(
                        repositoryRoot,
                        zipA,
                        "File",
                        ReleaseBuildAdapterKind.ModuleBuild),
                    CreateSigningReceipt(
                        repositoryRoot,
                        zipB,
                        "File",
                        ReleaseBuildAdapterKind.ModuleBuild)
                ]);
            ProjectBuildGitHubPublishRequest? captured = null;
            var publishHost = new ProjectBuildPublishHostService(
                new NullLogger(),
                request =>
                {
                    captured = request;
                    return new ProjectBuildGitHubPublishSummary { Success = true };
                });
            var service = CreateReviewPublishService(projectBuildPublishHostService: publishHost);

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(
                result.Succeeded,
                $"{result.Summary} | {string.Join(" | ", result.Receipts.Select(receipt => receipt.Summary))}");
            Assert.NotNull(captured);
            var projects = captured!.Release.Projects.ToDictionary(
                static project => project.ProjectName,
                StringComparer.OrdinalIgnoreCase);
            Assert.Equal([packageA], projects["LaneA"].Packages);
            Assert.Equal([packageB], projects["LaneB"].Packages);
            Assert.Equal(zipA, projects["LaneA"].ReleaseZipPath);
            Assert.Equal(zipB, projects["LaneB"].ReleaseZipPath);
        }
        finally
        {
            TryDeleteReviewRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_coordinated_module_github_release_includes_signed_package_assets()
    {
        var repositoryRoot = CreateReviewRepository(out var releaseConfig);
        try
        {
            var packedDirectory = Directory.CreateDirectory(
                Path.Combine(repositoryRoot, "Artifacts", "Packed")).FullName;
            var moduleZip = Path.Combine(packedDirectory, "Sample.zip");
            var package = Path.Combine(repositoryRoot, "Sample.2.0.0.nupkg");
            var symbols = Path.Combine(repositoryRoot, "Sample.2.0.0.snupkg");
            File.WriteAllText(moduleZip, "zip");
            File.WriteAllText(package, "package");
            File.WriteAllText(symbols, "symbols");
            var moduleDirectory = CreateModulePackage(repositoryRoot, "current", "2.0.0", null);
            File.WriteAllText(
                Path.Combine(repositoryRoot, "powerforge.json"),
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "." },
                  "Segments": [
                    {
                      "Type": "Packed",
                      "Configuration": {
                        "Enabled": true,
                        "Path": "Artifacts/Packed",
                        "ArtefactName": "Sample.zip"
                      }
                    },
                    {
                      "Type": "GitHubNuget",
                      "Configuration": {
                        "Destination": "GitHub",
                        "Enabled": true,
                        "UserName": "EvotecIT",
                        "RepositoryName": "Sample",
                        "ApiKey": "test-token"
                      }
                    },
                    {
                      "Type": "Release",
                      "Configuration": {}
                    }
                  ]
                }
                """);
            WriteModuleReleaseConfig(releaseConfig);
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult
                {
                    Success = true,
                    ConfigPath = releaseConfig,
                    ModulePlan = new PowerForgeModuleReleasePlanSummary
                    {
                        ModuleName = "Sample",
                        ModuleVersion = "2.0.0"
                    }
                },
                [
                    CreateSigningReceipt(repositoryRoot, moduleDirectory, "Directory"),
                    CreateSigningReceipt(repositoryRoot, moduleZip, "File"),
                    CreateSigningReceipt(repositoryRoot, package, "File"),
                    CreateSigningReceipt(repositoryRoot, symbols, "File")
                ]);
            GitHubReleasePublishRequest? captured = null;
            var service = CreateReviewPublishService(
                publishGitHubReleaseAsync: (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        AllAssetUploadsSucceeded = true
                    });
                });

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded, result.Summary);
            Assert.NotNull(captured);
            Assert.Equal(
                new[] { moduleZip, package, symbols }.OrderBy(static path => path),
                captured!.AssetFilePaths.OrderBy(static path => path));
        }
        finally
        {
            TryDeleteReviewRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_rejects_enabled_unified_github_without_checkpointed_assets()
    {
        var repositoryRoot = CreateReviewRepository(out var releaseConfig);
        try
        {
            File.WriteAllText(
                releaseConfig,
                """
                {
                  "GitHub": {
                    "Publish": true,
                    "Owner": "EvotecIT",
                    "Repository": "Sample"
                  }
                }
                """);
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult { Success = true, ConfigPath = releaseConfig },
                []);
            var publishCalls = 0;
            var service = CreateReviewPublishService(
                publishGitHubReleaseAsync: (_, _) =>
                {
                    publishCalls++;
                    return Task.FromResult(new GitHubReleasePublishResult { Succeeded = true });
                });

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.False(result.Succeeded);
            Assert.Equal(0, publishCalls);
            Assert.Contains(
                result.Receipts,
                receipt =>
                    receipt.Status == ReleasePublishReceiptStatus.Failed &&
                    receipt.Summary.Contains("no release assets", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteReviewRepository(repositoryRoot);
        }
    }

    [Fact]
    public void PrepareApplePublishFromCheckpoint_reuses_the_checkpointed_archive()
    {
        var spec = new PowerForgeReleaseSpec
        {
            AppleApps = new PowerForgeAppleReleaseOptions { Archive = true }
        };
        var built = new PowerForgeReleaseResult
        {
            AppleAppPlan = new PowerForgeAppleReleasePlan()
        };

        ReleasePublishExecutionService.PrepareApplePublishFromCheckpoint(spec, built);

        Assert.False(spec.AppleApps.Archive);
    }

    [Fact]
    public void CreateUnifiedPublishRequest_preserves_checkpointed_Apple_provenance_and_recovery_options()
    {
        var built = new PowerForgeReleaseResult
        {
            AppleAppPlan = new PowerForgeAppleReleasePlan
            {
                SourceCommit = "0123456789abcdef0123456789abcdef01234567",
                RequestedMarketingVersion = "1.6",
                AdoptExistingBuild = true,
                Automation = new PowerForgeAppleReleaseAutomationOptions
                {
                    Resume = false,
                    WaitForProcessing = true,
                    ProcessingTimeoutSeconds = 1200,
                    PollIntervalSeconds = 30
                },
                Apps =
                [
                    new PowerForgeAppleAppReleaseTargetPlan
                    {
                        Name = "CasaRay iOS",
                        ExpectedArchiveSha256 = "1111111111111111111111111111111111111111111111111111111111111111"
                    }
                ]
            },
            AppleReceipt = new PowerForgeAppleReleaseReceipt
            {
                PlanOnly = true,
                PlanSha256 = "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd"
            }
        };

        var request = ReleasePublishExecutionService.CreateUnifiedPublishRequest(
            "/repo/powerforge.release.json",
            built);

        Assert.Equal(PowerForgeAppleReleaseAction.Configured, request.AppleAction);
        Assert.Equal("1.6", request.AppleMarketingVersion);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", request.AppleSourceCommit);
        Assert.Equal("abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd", request.AppleExpectedPlanSha256);
        Assert.Equal(
            "1111111111111111111111111111111111111111111111111111111111111111",
            Assert.Single(request.AppleExpectedArchiveSha256ByTarget).Value);
        Assert.True(request.AppleAdoptExistingBuild);
        Assert.False(request.AppleResume);
        Assert.True(request.AppleWaitForProcessing);
        Assert.Equal(1200, request.AppleProcessingTimeoutSeconds);
        Assert.Equal(30, request.ApplePollIntervalSeconds);
    }

    [Fact]
    public async Task ExecuteAsync_rethrows_cancellation_from_module_repository_publication()
    {
        var repositoryRoot = CreateReviewRepository(out var releaseConfig);
        try
        {
            File.WriteAllText(
                Path.Combine(repositoryRoot, "powerforge.json"),
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "." },
                  "Segments": [
                    {
                      "Type": "GalleryNuget",
                      "Configuration": {
                        "Destination": "PowerShellGallery",
                        "Enabled": true,
                        "ApiKey": "test-key"
                      }
                    }
                  ]
                }
                """);
            WriteModuleReleaseConfig(releaseConfig);
            var moduleDirectory = CreateModulePackage(repositoryRoot, "current", "2.0.0", null);
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult
                {
                    Success = true,
                    ConfigPath = releaseConfig,
                    ModulePlan = new PowerForgeModuleReleasePlanSummary
                    {
                        ModuleName = "Sample",
                        ModuleVersion = "2.0.0"
                    }
                },
                [CreateSigningReceipt(repositoryRoot, moduleDirectory, "Directory")]);
            var entered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var service = CreateReviewPublishService(
                publishCheckpointedModuleAsync: async (_, cancellationToken) =>
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("Cancellation was not observed.");
                });
            using var cancellation = new CancellationTokenSource();
            using var _ = new EnvironmentScope().Set(
                "RELEASE_OPS_STUDIO_ENABLE_PUBLISH",
                "true");

            var execution = service.ExecuteAsync(queueItem, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        }
        finally
        {
            TryDeleteReviewRepository(repositoryRoot);
        }
    }
}

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_checkpoints_generated_winget_manifests_for_signing()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("WingetCheckpointRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("WingetCheckpointRepo", "Build"));
        var manifestDirectory = scope.CreateDirectory(
            Path.Combine("WingetCheckpointRepo", "Artifacts", "Winget"));
        var manifestPath = Path.Combine(manifestDirectory, "EvotecIT.Sample.yaml");
        File.WriteAllText(manifestPath, "PackageIdentifier: EvotecIT.Sample");
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """{ "Winget": { "Enabled": true, "Packages": [] } }""");
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ModuleBuildHostService(),
            (configPath, _) => new PowerForgeReleaseResult
            {
                Success = true,
                ConfigPath = configPath,
                WingetManifestPaths = [manifestPath]
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Contains(
            result.AdapterResults,
            adapter => adapter.ArtifactFiles.Contains(
                manifestPath,
                StringComparer.OrdinalIgnoreCase));
    }
}
