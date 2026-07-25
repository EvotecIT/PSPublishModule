using System.Text.Json;
using System.Text.RegularExpressions;
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
    public async Task ExecuteAsync_rejects_changed_script_exported_publish_configuration()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForgeStudio.Tests",
            Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
            var scriptPath = Path.Combine(repositoryRoot, "Build-Module.ps1");
            File.WriteAllText(scriptPath, ". ./Publish.Settings.ps1");
            File.WriteAllText(Path.Combine(repositoryRoot, "Publish.Settings.ps1"), "$Destination = 'PSGallery'");
            var releaseConfig = Path.Combine(buildDirectory, "release.json");
            File.WriteAllText(
                releaseConfig,
                """{ "Module": { "RepositoryRoot": "..", "ScriptPath": "Build-Module.ps1" } }""");
            var packageDirectory = Directory.CreateDirectory(
                Path.Combine(repositoryRoot, "Artifacts", "Packed", "Sample")).FullName;
            File.WriteAllText(
                Path.Combine(packageDirectory, "Sample.psd1"),
                "@{ RootModule = 'Sample.psm1'; ModuleVersion = '1.0.0' }");

            var approvedConfigurations = new[]
            {
                new PublishConfiguration {
                    Destination = PublishDestination.PowerShellGallery,
                    Enabled = true,
                    RepositoryName = "PSGallery"
                }
            };
            var unified = new PowerForgeReleaseResult {
                Success = true,
                ConfigPath = releaseConfig,
                ModulePlan = new PowerForgeModuleReleasePlanSummary {
                    ModuleName = "Sample",
                    ScriptPath = scriptPath,
                    ModuleVersion = "1.0.0"
                }
            };
            var buildResult = new ReleaseBuildExecutionResult(
                repositoryRoot,
                true,
                "Build completed.",
                1,
                [],
                UnifiedReleaseStateJson: JsonSerializer.Serialize(unified),
                UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig),
                ModulePublishConfigSha256:
                    UnifiedReleaseConfigFingerprint.ComputeModulePublishConfigurations(
                        approvedConfigurations));
            var signingResult = new ReleaseSigningExecutionResult(
                repositoryRoot,
                true,
                "Signing completed.",
                JsonSerializer.Serialize(buildResult),
                [
                    ReleaseSigningArtifactIntegrity.Capture(new ReleaseSigningReceipt(
                        repositoryRoot,
                        "Sample",
                        ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                        packageDirectory,
                        "Directory",
                        ReleaseSigningReceiptStatus.Signed,
                        "Signed.",
                        DateTimeOffset.UtcNow))
                ]);
            var queueItem = new ReleaseQueueItem(
                repositoryRoot,
                "Sample",
                ReleaseRepositoryKind.Module,
                ReleaseWorkspaceKind.PrimaryRepository,
                1,
                ReleaseQueueStage.Publish,
                ReleaseQueueItemStatus.ReadyToRun,
                "Ready.",
                "publish.ready",
                JsonSerializer.Serialize(signingResult),
                DateTimeOffset.UtcNow);

            var modulePublishCalls = 0;
            var moduleRunner = new StubPowerShellRunner(request =>
            {
                var match = Regex.Match(
                    request.CommandText ?? string.Empty,
                    "\\$targetJson = '([^']+)'");
                if (!match.Success)
                    return new PowerShellRunResult(1, string.Empty, "Export path missing.", "pwsh");

                File.WriteAllText(
                    match.Groups[1].Value,
                    """
                    {
                      "Segments": [
                        {
                          "Type": "GalleryNuget",
                          "Configuration": {
                            "Destination": "PowerShellGallery",
                            "Enabled": true,
                            "RepositoryName": "ChangedRepository"
                          }
                        }
                      ]
                    }
                    """);
                return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
            });
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(moduleRunner),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                new ProjectBuildPublishHostService(),
                (_, _) => Task.FromResult(new DotNetNuGetPushResult(
                    0,
                    string.Empty,
                    string.Empty,
                    "dotnet",
                    TimeSpan.Zero,
                    timedOut: false,
                    errorMessage: null)),
                publishRepositoryAsync: (request, _) =>
                {
                    modulePublishCalls++;
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

            Assert.False(result.Succeeded);
            Assert.Equal(0, modulePublishCalls);
            var failure = Assert.Single(result.Receipts);
            Assert.Contains(
                "publish configuration changed after the build checkpoint",
                failure.Summary,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_stops_unified_publication_after_project_package_failure()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForgeStudio.Tests",
            Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
            var releaseConfig = Path.Combine(buildDirectory, "release.json");
            File.WriteAllText(
                releaseConfig,
                """
                {
                  "Packages": {
                    "RootPath": "..",
                    "PublishNuget": true,
                    "PublishApiKey": "test-key",
                    "PublishFailFast": true
                  }
                }
                """);
            var packagePath = Path.Combine(repositoryRoot, "Sample.Library.1.0.0.nupkg");
            File.WriteAllText(packagePath, "signed-package");
            var checkpointedPlan = new DotNetRepositoryReleaseResult {
                Success = true,
                Projects = {
                    new DotNetRepositoryProjectResult {
                        ProjectName = "Sample.Library",
                        PackageId = "Sample.Library",
                        NewVersion = "1.0.0",
                        Packages = { packagePath }
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
                [
                    new ReleaseSigningReceipt(
                        repositoryRoot,
                        "Sample",
                        ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                        packagePath,
                        "File",
                        ReleaseSigningReceiptStatus.Signed,
                        "Signed.",
                        DateTimeOffset.UtcNow)
                ]);
            var unifiedPublishCalls = 0;
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                new ProjectBuildPublishHostService(),
                (_, _) => Task.FromResult(new DotNetNuGetPushResult(
                    1,
                    string.Empty,
                    "push failed",
                    "dotnet",
                    TimeSpan.Zero,
                    timedOut: false,
                    errorMessage: "push failed")),
                publishUnifiedRelease: (_, _) =>
                {
                    unifiedPublishCalls++;
                    return new PowerForgeReleaseResult { Success = true };
                });

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.False(result.Succeeded);
            Assert.Equal(0, unifiedPublishCalls);
            Assert.Equal(
                ReleasePublishReceiptStatus.Failed,
                Assert.Single(result.Receipts).Status);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }
}
