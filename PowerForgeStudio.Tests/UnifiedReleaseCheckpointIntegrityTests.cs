using PowerForge;
using System.Text.Json;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class UnifiedReleaseCheckpointIntegrityTests
{
    [Fact]
    public void Fingerprint_changes_when_package_owning_module_config_changes()
    {
        using var scope = new TestDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("FingerprintRepo");
        var buildRoot = scope.CreateDirectory(Path.Combine("FingerprintRepo", "Build"));
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        var releaseConfig = Path.Combine(buildRoot, "release.json");
        File.WriteAllText(moduleConfig, """{ "Build": { "Name": "Sample", "SourcePath": "Module" }, "Segments": [] }""");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "Module": {
                "RepositoryRoot": "..",
                "ConfigPath": "powerforge.json",
                "IncludesPackages": true
              }
            }
            """);

        var fingerprint = UnifiedReleaseConfigFingerprint.Compute(releaseConfig);
        File.WriteAllText(moduleConfig, """{ "Build": { "Name": "Sample", "SourcePath": "Module" }, "Segments": [{ "Type": "PackageBuild", "Configuration": { "Name": "Changed" } }] }""");

        var exception = Assert.Throws<InvalidOperationException>(
            () => UnifiedReleaseConfigFingerprint.Validate(releaseConfig, fingerprint));
        Assert.Contains("changed after the build checkpoint", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fingerprint_changes_when_apple_metadata_input_changes()
    {
        using var scope = new TestDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("AppleFingerprintRepo");
        var buildRoot = scope.CreateDirectory(Path.Combine("AppleFingerprintRepo", "Build"));
        var metadataPath = Path.Combine(repositoryRoot, "metadata.json");
        var releaseConfig = Path.Combine(buildRoot, "release.json");
        File.WriteAllText(metadataPath, """{ "version": 1 }""");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "AppleApps": {
                "ProjectRoot": "..",
                "SyncMetadata": true,
                "MetadataConfigPath": "metadata.json",
                "Apps": []
              }
            }
            """);

        var fingerprint = UnifiedReleaseConfigFingerprint.Compute(releaseConfig);
        File.WriteAllText(metadataPath, """{ "version": 2 }""");

        var exception = Assert.Throws<InvalidOperationException>(
            () => UnifiedReleaseConfigFingerprint.Validate(releaseConfig, fingerprint));
        Assert.Contains("changed after the build checkpoint", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_forwards_cancellation_to_unified_release_request()
    {
        using var scope = new TestDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CancellationRepo");
        var buildRoot = scope.CreateDirectory(Path.Combine("CancellationRepo", "Build"));
        File.WriteAllText(Path.Combine(buildRoot, "release.json"), """{ "WorkspaceValidation": { "ConfigPath": "workspace.json" } }""");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ModuleBuildHostService(),
            executeUnifiedReleaseBuild: (_, request) =>
            {
                Assert.True(request.CancellationToken.CanBeCanceled);
                entered.SetResult();
                request.CancellationToken.WaitHandle.WaitOne();
                request.CancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Cancellation was not observed.");
            });
        using var cancellation = new CancellationTokenSource();

        var execution = service.ExecuteAsync(repositoryRoot, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
    }

    [Fact]
    public void BuildPendingTargets_reports_missing_checkpointed_github_asset_as_configuration_error()
    {
        using var scope = new TestDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("MissingAssetRepo");
        var buildRoot = scope.CreateDirectory(Path.Combine("MissingAssetRepo", "Build"));
        var releaseConfig = Path.Combine(buildRoot, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "GitHub": {
                "Publish": true,
                "Owner": "EvotecIT",
                "Repository": "MissingAssetRepo"
              }
            }
            """);
        var missingAsset = Path.Combine(repositoryRoot, "Artifacts", "missing.zip");
        var unified = new PowerForgeReleaseResult
        {
            Success = true,
            ConfigPath = releaseConfig,
            ReleaseAssets = [missingAsset]
        };
        var build = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(unified),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
        var signing = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(build),
            []);
        var queueItem = new ReleaseQueueItem(
            repositoryRoot,
            "MissingAssetRepo",
            ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository,
            1,
            ReleaseQueueStage.Publish,
            ReleaseQueueItemStatus.ReadyToRun,
            "Ready.",
            "publish.ready",
            JsonSerializer.Serialize(signing),
            DateTimeOffset.UtcNow);

        var target = Assert.Single(new ReleasePublishExecutionService().BuildPendingTargets([queueItem]));

        Assert.Equal("ConfigurationError", target.TargetKind);
        Assert.Contains(missingAsset, target.Destination, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPendingTargets_reports_any_missing_winget_manifest_as_configuration_error()
    {
        using var scope = new TestDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("MissingWingetRepo");
        var buildRoot = scope.CreateDirectory(Path.Combine("MissingWingetRepo", "Build"));
        var releaseConfig = Path.Combine(buildRoot, "release.json");
        var presentManifest = Path.Combine(repositoryRoot, "present.yaml");
        var missingManifest = Path.Combine(repositoryRoot, "missing.yaml");
        File.WriteAllText(presentManifest, "PackageIdentifier: Sample");
        File.WriteAllText(releaseConfig, """{ "Winget": { "Submit": true } }""");
        var unified = new PowerForgeReleaseResult
        {
            Success = true,
            ConfigPath = releaseConfig,
            WingetManifestPaths = [presentManifest, missingManifest]
        };
        var queueItem = CreatePublishQueueItem(
            repositoryRoot,
            "MissingWingetRepo",
            releaseConfig,
            unified);

        var target = Assert.Single(new ReleasePublishExecutionService().BuildPendingTargets([queueItem]));

        Assert.Equal("ConfigurationError", target.TargetKind);
        Assert.Contains(missingManifest, target.Destination, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPendingTargets_omits_build_only_module_package_lanes()
    {
        using var scope = new TestDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("BuildOnlyPackagesRepo");
        scope.CreateDirectory(Path.Combine("BuildOnlyPackagesRepo", "Module"));
        var buildRoot = scope.CreateDirectory(Path.Combine("BuildOnlyPackagesRepo", "Build"));
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        var releaseConfig = Path.Combine(buildRoot, "release.json");
        File.WriteAllText(
            moduleConfig,
            """
            {
              "Build": { "Name": "Sample", "SourcePath": "Module" },
              "Segments": [
                {
                  "Type": "PackageBuild",
                  "Configuration": { "Name": "Sample.Library", "Build": true, "PublishNuget": false, "PublishGitHub": false }
                }
              ]
            }
            """);
        File.WriteAllText(
            releaseConfig,
            """{ "Module": { "RepositoryRoot": "..", "ConfigPath": "powerforge.json", "IncludesPackages": true } }""");
        var unified = new PowerForgeReleaseResult
        {
            Success = true,
            ConfigPath = releaseConfig
        };
        var queueItem = CreatePublishQueueItem(
            repositoryRoot,
            "BuildOnlyPackagesRepo",
            releaseConfig,
            unified);

        Assert.Empty(new ReleasePublishExecutionService().BuildPendingTargets([queueItem]));
    }

    [Fact]
    public void BuildPendingTargets_honors_referenced_package_publish_override()
    {
        using var scope = new TestDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ReferencedPackagesRepo");
        scope.CreateDirectory(Path.Combine("ReferencedPackagesRepo", "Module"));
        var buildRoot = scope.CreateDirectory(Path.Combine("ReferencedPackagesRepo", "Build"));
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        var packageConfig = Path.Combine(buildRoot, "project.build.json");
        var releaseConfig = Path.Combine(buildRoot, "release.json");
        File.WriteAllText(
            packageConfig,
            """{ "RootPath": "..", "Build": true, "PublishNuget": false, "PublishGitHub": false }""");
        File.WriteAllText(
            moduleConfig,
            """
            {
              "Build": { "Name": "Sample", "SourcePath": "Module" },
              "Segments": [
                {
                  "Type": "ProjectBuild",
                  "Configuration": {
                    "Name": "Sample packages",
                    "ConfigPath": "Build/project.build.json",
                    "PublishNuget": true
                  }
                }
              ]
            }
            """);
        File.WriteAllText(
            releaseConfig,
            """{ "Module": { "RepositoryRoot": "..", "ConfigPath": "powerforge.json", "IncludesPackages": true } }""");
        var queueItem = CreatePublishQueueItem(
            repositoryRoot,
            "ReferencedPackagesRepo",
            releaseConfig,
            new PowerForgeReleaseResult
            {
                Success = true,
                ConfigPath = releaseConfig
            });

        var target = Assert.Single(new ReleasePublishExecutionService().BuildPendingTargets([queueItem]));

        Assert.Equal("ModulePackages", target.TargetKind);
    }

    private static ReleaseQueueItem CreatePublishQueueItem(
        string repositoryRoot,
        string repositoryName,
        string releaseConfig,
        PowerForgeReleaseResult unified)
    {
        var build = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(unified),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
        var signing = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(build),
            []);
        return new ReleaseQueueItem(
            repositoryRoot,
            repositoryName,
            ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository,
            1,
            ReleaseQueueStage.Publish,
            ReleaseQueueItemStatus.ReadyToRun,
            "Ready.",
            "publish.ready",
            JsonSerializer.Serialize(signing),
            DateTimeOffset.UtcNow);
    }

    private sealed class TestDirectoryScope : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "powerforge-studio-checkpoint-" + Guid.NewGuid().ToString("N"));

        internal string CreateDirectory(string relativePath)
            => Directory.CreateDirectory(Path.Combine(_root, relativePath)).FullName;

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }
    }
}
