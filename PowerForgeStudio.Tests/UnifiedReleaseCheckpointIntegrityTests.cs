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
