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
    public async Task ExecuteAsync_JsonModuleConfig_PublishesWithoutLegacyScriptExport()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        File.WriteAllText(Path.Combine(buildDirectory, "Build-Project.ps1"), "# unified entry point");
        File.WriteAllText(
            Path.Combine(buildDirectory, "release.json"),
            """
            {
              "Module": {
                "RepositoryRoot": "..",
                "ConfigPath": "powerforge.json"
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(repositoryRoot, "powerforge.json"),
            """
            {
              "SchemaVersion": 1,
              "Build": {
                "Name": "JsonModuleRepo",
                "SourcePath": "src/JsonModuleRepo"
              },
              "Segments": [
                {
                  "Type": "GalleryNuget",
                  "Configuration": {
                    "Destination": "PowerShellGallery",
                    "Enabled": true,
                    "Tool": "PSResourceGet",
                    "ApiKeyFilePath": "secrets/gallery.key",
                    "RepositoryName": "PSGallery"
                  }
                }
              ]
            }
            """);
        var moduleRoot = Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "JsonModuleRepo")).FullName;
        var secretsDirectory = Directory.CreateDirectory(Path.Combine(moduleRoot, "secrets")).FullName;
        File.WriteAllText(Path.Combine(secretsDirectory, "gallery.key"), "gallery-key");

        var packageDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Artifacts", "Packed", "JsonModuleRepo")).FullName;
        var manifestPath = Path.Combine(packageDirectory, "JsonModuleRepo.psd1");
        File.WriteAllText(
            manifestPath,
            """
            @{
                RootModule = 'JsonModuleRepo.psm1'
                ModuleVersion = '1.2.3'
            }
            """);
        var signingResult = new ReleaseSigningExecutionResult(
            RootPath: repositoryRoot,
            Succeeded: true,
            Summary: "Signing completed.",
            SourceCheckpointStateJson: null,
            Receipts: [
                new ReleaseSigningReceipt(
                    RootPath: repositoryRoot,
                    RepositoryName: "JsonModuleRepo",
                    AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    ArtifactPath: packageDirectory,
                    ArtifactKind: "Directory",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Package directory signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseSigningReceipt(
                    RootPath: repositoryRoot,
                    RepositoryName: "JsonModuleRepo",
                    AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    ArtifactPath: manifestPath,
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Manifest signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);
        var queueItem = new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: "JsonModuleRepo",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready for publish.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: JsonSerializer.Serialize(signingResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        RepositoryPublishRequest? captured = null;
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ProjectBuildPublishHostService(),
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, "published", string.Empty, "dotnet", TimeSpan.Zero, timedOut: false, errorMessage: null)),
            publishRepositoryAsync: (request, _) =>
            {
                captured = request;
                return Task.FromResult(new RepositoryPublishResult(
                    path: request.Path,
                    isNupkg: request.IsNupkg,
                    repositoryName: request.RepositoryName ?? "PSGallery",
                    tool: request.Tool,
                    repositoryCreated: false,
                    repositoryUnregistered: false));
            });

        try
        {
            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");

            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal(packageDirectory, captured!.Path);
            Assert.Equal("gallery-key", captured.ApiKey);
            Assert.Equal("PSGallery", captured.RepositoryName);
            Assert.Equal(ReleasePublishReceiptStatus.Published, Assert.Single(result.Receipts).Status);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_JsonModuleConfigReloadFailure_ReturnsFailedReceipt()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        var moduleRoot = Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "BrokenModule")).FullName;
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        File.WriteAllText(moduleConfig, """{ "Build": { "Name": "BrokenModule", "SourcePath": "src/BrokenModule" } }""");
        File.WriteAllText(
            Path.Combine(buildDirectory, "release.json"),
            """{ "Module": { "RepositoryRoot": "..", "ConfigPath": "powerforge.json" } }""");
        var packageDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Artifacts", "Packed", "BrokenModule")).FullName;
        var signingResult = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            null,
            [
                new ReleaseSigningReceipt(
                    repositoryRoot,
                    "BrokenModule",
                    ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    packageDirectory,
                    "Directory",
                    ReleaseSigningReceiptStatus.Signed,
                    "Signed.",
                    DateTimeOffset.UtcNow)
            ]);
        var queueItem = new ReleaseQueueItem(
            repositoryRoot,
            "BrokenModule",
            ReleaseRepositoryKind.Module,
            ReleaseWorkspaceKind.PrimaryRepository,
            1,
            ReleaseQueueStage.Publish,
            ReleaseQueueItemStatus.ReadyToRun,
            "Ready.",
            "publish.ready",
            JsonSerializer.Serialize(signingResult),
            DateTimeOffset.UtcNow);
        File.Delete(moduleConfig);

        try
        {
            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await new ReleasePublishExecutionService().ExecuteAsync(queueItem);

            Assert.False(result.Succeeded);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal(ReleasePublishReceiptStatus.Failed, receipt.Status);
            Assert.Contains("powerforge.json", receipt.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }
}
