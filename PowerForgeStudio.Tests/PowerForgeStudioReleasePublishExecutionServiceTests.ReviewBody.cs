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
    public async Task ExecuteAsync_selects_module_directory_by_checkpointed_version_and_prerelease()
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
            var oldDirectory = CreateModulePackage(repositoryRoot, "old", "1.0.0", null);
            var currentDirectory = CreateModulePackage(repositoryRoot, "current", "2.0.0", "beta.1");
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult {
                    Success = true,
                    ConfigPath = releaseConfig,
                    ModulePlan = new PowerForgeModuleReleasePlanSummary {
                        ModuleName = "Sample",
                        ModuleVersion = "2.0.0",
                        PreReleaseTag = "beta.1"
                    }
                },
                [
                    CreateSigningReceipt(repositoryRoot, oldDirectory, "Directory"),
                    CreateSigningReceipt(repositoryRoot, currentDirectory, "Directory")
                ]);
            RepositoryPublishRequest? captured = null;
            var service = CreateReviewPublishService(
                publishRepositoryAsync: (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new RepositoryPublishResult(
                        request.Path,
                        request.IsNupkg,
                        request.RepositoryName ?? "PSGallery",
                        request.Tool,
                        repositoryCreated: false,
                        repositoryUnregistered: false));
                });

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(
                result.Succeeded,
                $"{result.Summary} | {string.Join(" | ", result.Receipts.Select(receipt => $"{receipt.TargetName}:{receipt.Status}:{receipt.Summary}"))}");
            Assert.NotNull(captured);
            Assert.Equal(currentDirectory, captured!.Path);
        }
        finally
        {
            TryDeleteReviewRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_publishes_only_the_module_artifact_selected_by_id()
    {
        var repositoryRoot = CreateReviewRepository(out var releaseConfig);
        try
        {
            var firstDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Artifacts", "First")).FullName;
            var secondDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Artifacts", "Second")).FullName;
            var firstZip = Path.Combine(firstDirectory, "First.zip");
            var secondZip = Path.Combine(secondDirectory, "Second.zip");
            File.WriteAllText(firstZip, "first");
            File.WriteAllText(secondZip, "second");
            var packageDirectory = CreateModulePackage(repositoryRoot, "current", "2.0.0", null);
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
                        "ID": "First",
                        "Path": "Artifacts/First",
                        "ArtefactName": "First.zip"
                      }
                    },
                    {
                      "Type": "Packed",
                      "Configuration": {
                        "Enabled": true,
                        "ID": "Second",
                        "Path": "Artifacts/Second",
                        "ArtefactName": "Second.zip"
                      }
                    },
                    {
                      "Type": "GitHubNuget",
                      "Configuration": {
                        "Destination": "GitHub",
                        "Enabled": true,
                        "ID": "Second",
                        "UserName": "EvotecIT",
                        "RepositoryName": "Sample",
                        "ApiKey": "test-token"
                      }
                    }
                  ]
                }
                """);
            WriteModuleReleaseConfig(releaseConfig);
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult {
                    Success = true,
                    ConfigPath = releaseConfig,
                    ModulePlan = new PowerForgeModuleReleasePlanSummary {
                        ModuleName = "Sample",
                        ModuleVersion = "2.0.0"
                    }
                },
                [
                    CreateSigningReceipt(repositoryRoot, packageDirectory, "Directory"),
                    CreateSigningReceipt(repositoryRoot, firstZip, "File"),
                    CreateSigningReceipt(repositoryRoot, secondZip, "File")
                ]);
            GitHubReleasePublishRequest? captured = null;
            var service = CreateReviewPublishService(
                publishGitHubReleaseAsync: (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new GitHubReleasePublishResult {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        AllAssetUploadsSucceeded = true,
                        HtmlUrl = "https://example.test/release"
                    });
                });

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal([secondZip], captured!.AssetFilePaths);
        }
        finally
        {
            TryDeleteReviewRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_uses_checkpointed_unified_package_plan_for_github_publication()
    {
        var repositoryRoot = CreateReviewRepository(out var releaseConfig);
        try
        {
            File.WriteAllText(
                releaseConfig,
                """
                {
                  "Packages": {
                    "RootPath": "..",
                    "PublishGitHub": true,
                    "GitHubAccessToken": "test-token",
                    "GitHubUsername": "EvotecIT",
                    "GitHubRepositoryName": "Sample"
                  }
                }
                """);
            var releaseZip = Path.Combine(repositoryRoot, "Sample.9.9.9.zip");
            File.WriteAllText(releaseZip, "signed");
            var checkpointedPlan = new DotNetRepositoryReleaseResult {
                Success = true,
                ResolvedVersion = "9.9.9",
                Projects = {
                    new DotNetRepositoryProjectResult {
                        ProjectName = "Sample",
                        PackageId = "Sample",
                        IsPackable = true,
                        NewVersion = "9.9.9",
                        ReleaseZipPath = releaseZip
                    }
                }
            };
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult {
                    Success = true,
                    ConfigPath = releaseConfig,
                    Packages = new ProjectBuildHostExecutionResult {
                        Success = true,
                        Result = new ProjectBuildResult {
                            Success = true,
                            Release = checkpointedPlan
                        }
                    }
                },
                [CreateSigningReceipt(repositoryRoot, releaseZip, "File", ReleaseBuildAdapterKind.ProjectBuild)]);
            ProjectBuildGitHubPublishRequest? captured = null;
            var projectHost = new ProjectBuildHostService(
                new NullLogger(),
                executeRelease: _ => throw new InvalidOperationException("The repository must not be replanned during publication."),
                publishGitHub: null,
                validateGitHubPreflight: null);
            var publishHost = new ProjectBuildPublishHostService(
                new NullLogger(),
                request =>
                {
                    captured = request;
                    return new ProjectBuildGitHubPublishSummary {
                        Success = true,
                        SummaryTag = "v9.9.9",
                        SummaryReleaseUrl = "https://example.test/release",
                        SummaryAssetsCount = 1
                    };
                });
            var service = CreateReviewPublishService(
                projectBuildHostService: projectHost,
                projectBuildPublishHostService: publishHost);

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(
                result.Succeeded,
                $"{result.Summary} | {string.Join(" | ", result.Receipts.Select(receipt => $"{receipt.TargetName}:{receipt.Status}:{receipt.Summary}"))}");
            Assert.NotNull(captured);
            var project = Assert.Single(captured!.Release.Projects);
            Assert.Equal("9.9.9", project.NewVersion);
            Assert.Equal(releaseZip, project.ReleaseZipPath);
        }
        finally
        {
            TryDeleteReviewRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_preserves_skip_duplicate_and_stops_after_first_nuget_failure()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForgeStudio.Tests",
            Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
            File.WriteAllText(Path.Combine(buildDirectory, "Build-Project.ps1"), "# build");
            File.WriteAllText(
                Path.Combine(buildDirectory, "project.build.json"),
                """
                {
                  "PublishNuget": true,
                  "PublishApiKey": "test-key",
                  "PublishGitHub": true,
                  "GitHubAccessToken": "test-token",
                  "GitHubUsername": "EvotecIT",
                  "GitHubRepositoryName": "Sample",
                  "SkipDuplicate": false,
                  "PublishFailFast": true
                }
                """);
            var firstPackage = Path.Combine(repositoryRoot, "First.1.0.0.nupkg");
            var secondPackage = Path.Combine(repositoryRoot, "Second.1.0.0.nupkg");
            File.WriteAllText(firstPackage, "first");
            File.WriteAllText(secondPackage, "second");
            var signingResult = new ReleaseSigningExecutionResult(
                repositoryRoot,
                true,
                "Signing completed.",
                SourceCheckpointStateJson: null,
                [
                    CreateSigningReceipt(repositoryRoot, firstPackage, "File", ReleaseBuildAdapterKind.ProjectBuild),
                    CreateSigningReceipt(repositoryRoot, secondPackage, "File", ReleaseBuildAdapterKind.ProjectBuild)
                ]);
            var queueItem = new ReleaseQueueItem(
                repositoryRoot,
                "Sample",
                ReleaseRepositoryKind.Library,
                ReleaseWorkspaceKind.PrimaryRepository,
                1,
                ReleaseQueueStage.Publish,
                ReleaseQueueItemStatus.ReadyToRun,
                "Ready.",
                "publish.ready",
                System.Text.Json.JsonSerializer.Serialize(signingResult),
                DateTimeOffset.UtcNow);
            var requests = new List<DotNetNuGetPushRequest>();
            var service = CreateReviewPublishService(
                projectBuildHostService: new ProjectBuildHostService(
                    new NullLogger(),
                    executeRelease: _ => throw new InvalidOperationException("Fail-fast must prevent GitHub planning."),
                    publishGitHub: null,
                    validateGitHubPreflight: null),
                pushNuGetPackageAsync: (request, _) =>
                {
                    requests.Add(request);
                    return Task.FromResult(new DotNetNuGetPushResult(
                        1,
                        string.Empty,
                        "push failed",
                        "dotnet",
                        TimeSpan.Zero,
                        timedOut: false,
                        errorMessage: "push failed"));
                });

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.False(result.Succeeded);
            var request = Assert.Single(requests);
            Assert.False(request.SkipDuplicate);
            Assert.Equal(firstPackage, request.PackagePath);
        }
        finally
        {
            TryDeleteReviewRepository(repositoryRoot);
        }
    }

    private static string CreateReviewRepository(out string releaseConfig)
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForgeStudio.Tests",
            Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
        releaseConfig = Path.Combine(buildDirectory, "release.json");
        return root;
    }

    private static string CreateModulePackage(
        string repositoryRoot,
        string directoryName,
        string version,
        string? preRelease)
    {
        var directory = Directory.CreateDirectory(Path.Combine(
            repositoryRoot,
            "Artifacts",
            "Modules",
            directoryName)).FullName;
        var lines = new List<string> {
            "@{",
            "    RootModule = 'Sample.psm1'",
            $"    ModuleVersion = '{version}'"
        };
        if (!string.IsNullOrWhiteSpace(preRelease))
        {
            lines.Add("    PrivateData = @{");
            lines.Add("        PSData = @{");
            lines.Add($"            Prerelease = '{preRelease}'");
            lines.Add("        }");
            lines.Add("    }");
        }
        lines.Add("}");
        File.WriteAllLines(Path.Combine(directory, "Sample.psd1"), lines);
        return directory;
    }

    private static void WriteModuleReleaseConfig(string releaseConfig)
        => File.WriteAllText(
            releaseConfig,
            """{ "Module": { "RepositoryRoot": "..", "ConfigPath": "powerforge.json" } }""");

    private static ReleaseSigningReceipt CreateSigningReceipt(
        string repositoryRoot,
        string artifactPath,
        string artifactKind,
        ReleaseBuildAdapterKind adapterKind = ReleaseBuildAdapterKind.ModuleBuild)
        => new(
            repositoryRoot,
            "Sample",
            adapterKind.ToString(),
            artifactPath,
            artifactKind,
            ReleaseSigningReceiptStatus.Signed,
            "Signed.",
            DateTimeOffset.UtcNow);

    private static ReleasePublishExecutionService CreateReviewPublishService(
        ProjectBuildHostService? projectBuildHostService = null,
        ProjectBuildPublishHostService? projectBuildPublishHostService = null,
        Func<DotNetNuGetPushRequest, CancellationToken, Task<DotNetNuGetPushResult>>? pushNuGetPackageAsync = null,
        Func<GitHubReleasePublishRequest, CancellationToken, Task<GitHubReleasePublishResult>>? publishGitHubReleaseAsync = null,
        Func<RepositoryPublishRequest, CancellationToken, Task<RepositoryPublishResult>>? publishRepositoryAsync = null)
        => new(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            projectBuildHostService ?? new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            projectBuildPublishHostService ?? new ProjectBuildPublishHostService(),
            pushNuGetPackageAsync ?? ((_, _) => Task.FromResult(new DotNetNuGetPushResult(
                0,
                string.Empty,
                string.Empty,
                "dotnet",
                TimeSpan.Zero,
                timedOut: false,
                errorMessage: null))),
            publishGitHubReleaseAsync,
            publishRepositoryAsync,
            publishUnifiedRelease: (_, _) => new PowerForgeReleaseResult { Success = true });

    private static void TryDeleteReviewRepository(string repositoryRoot)
    {
        try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
    }
}
