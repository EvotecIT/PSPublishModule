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
    public async Task ExecuteAsync_revalidates_module_config_after_project_publication()
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
                  "PublishSource": "https://example.test/v3/index.json"
                }
                """);

            var moduleRoot = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Module")).FullName;
            var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
            var originalModuleConfig =
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "Module", "Version": "1.0.0" },
                  "Segments": [
                    {
                      "Type": "GalleryNuget",
                      "Configuration": {
                        "Destination": "PowerShellGallery",
                        "Enabled": true,
                        "Tool": "PSResourceGet",
                        "RepositoryName": "PSGallery"
                      }
                    }
                  ]
                }
                """;
            File.WriteAllText(moduleConfig, originalModuleConfig);
            File.WriteAllText(
                Path.Combine(moduleRoot, "Sample.psd1"),
                "@{ RootModule = 'Sample.psm1'; ModuleVersion = '1.0.0' }");

            var packagePath = Path.Combine(repositoryRoot, "Sample.Library.1.0.0.nupkg");
            File.WriteAllText(packagePath, "signed-package");
            var buildResult = new ReleaseBuildExecutionResult(
                repositoryRoot,
                true,
                "Build completed.",
                1,
                [],
                ModuleBuildConfigSha256: UnifiedReleaseConfigFingerprint.ComputeModuleConfig(moduleConfig));
            var signingResult = new ReleaseSigningExecutionResult(
                repositoryRoot,
                true,
                "Signing completed.",
                JsonSerializer.Serialize(buildResult),
                [
                    ReleaseSigningArtifactIntegrity.Capture(new ReleaseSigningReceipt(
                        repositoryRoot,
                        "Sample",
                        ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                        packagePath,
                        "File",
                        ReleaseSigningReceiptStatus.Signed,
                        "Signed.",
                        DateTimeOffset.UtcNow)),
                    ReleaseSigningArtifactIntegrity.Capture(new ReleaseSigningReceipt(
                        repositoryRoot,
                        "Sample",
                        ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                        moduleRoot,
                        "Directory",
                        ReleaseSigningReceiptStatus.Signed,
                        "Signed.",
                        DateTimeOffset.UtcNow))
                ]);
            var queueItem = new ReleaseQueueItem(
                repositoryRoot,
                "Sample",
                ReleaseRepositoryKind.Mixed,
                ReleaseWorkspaceKind.PrimaryRepository,
                1,
                ReleaseQueueStage.Publish,
                ReleaseQueueItemStatus.ReadyToRun,
                "Ready.",
                "publish.ready",
                JsonSerializer.Serialize(signingResult),
                DateTimeOffset.UtcNow);

            var modulePublishCalls = 0;
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                new ProjectBuildPublishHostService(),
                (_, _) =>
                {
                    File.WriteAllText(
                        moduleConfig,
                        originalModuleConfig.Replace("\"Version\": \"1.0.0\"", "\"Version\": \"2.0.0\""));
                    return Task.FromResult(new DotNetNuGetPushResult(
                        0,
                        "published",
                        string.Empty,
                        "dotnet",
                        TimeSpan.Zero,
                        timedOut: false,
                        errorMessage: null));
                },
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
            Assert.Contains(result.Receipts, receipt =>
                receipt.Status == ReleasePublishReceiptStatus.Published &&
                receipt.TargetKind == "NuGet");
            var failure = Assert.Single(result.Receipts, receipt =>
                receipt.Status == ReleasePublishReceiptStatus.Failed);
            Assert.Contains("changed after the build checkpoint", failure.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }
}
