using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioReleasePublishExecutionServiceTests
{
    [Fact]
    public void BuildPendingTargets_PublishReadyItem_ReturnsGroupedTargetsFromSigningCheckpoint()
    {
        var repositoryRoot = @"C:\Support\GitHub\PSPublishModule";
        var signingResult = new ReleaseSigningExecutionResult(
            RootPath: repositoryRoot,
            Succeeded: true,
            Summary: "Signing completed.",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleaseSigningReceipt(
                    RootPath: repositoryRoot,
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                    ArtifactPath: Path.Combine(repositoryRoot, "Artefacts", "ProjectBuild", "Package.1.0.0.nupkg"),
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseSigningReceipt(
                    RootPath: repositoryRoot,
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                    ArtifactPath: Path.Combine(repositoryRoot, "Artefacts", "ProjectBuild", "Package.1.0.0.zip"),
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseSigningReceipt(
                    RootPath: repositoryRoot,
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    ArtifactPath: Path.Combine(repositoryRoot, "Artefacts", "Packed", "PSPublishModule"),
                    ArtifactKind: "Directory",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: "PSPublishModule",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready for publish.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: JsonSerializer.Serialize(signingResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var service = new ReleasePublishExecutionService();
        var targets = service.BuildPendingTargets([queueItem]);

        Assert.Equal(3, targets.Count);
        Assert.Contains(targets, target => target.TargetKind == "NuGet");
        Assert.Contains(targets, target => target.TargetKind == "GitHub");
        Assert.Contains(targets, target => target.TargetKind == "PowerShellRepository");
    }

    [Fact]
    public async Task ExecuteAsync_ProjectNuGetPublish_UsesSharedDotNetNuGetClient()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        File.WriteAllText(Path.Combine(buildDirectory, "Build-Project.ps1"), "# build");

        var packagePath = Path.Combine(repositoryRoot, "Artifacts", "Package.1.0.0.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        File.WriteAllText(packagePath, "package");

        File.WriteAllText(
            Path.Combine(buildDirectory, "project.build.json"),
            """
            {
              "PublishNuget": true,
              "PublishSource": "https://api.nuget.org/v3/index.json",
              "PublishApiKey": "secret"
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
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                    ArtifactPath: packagePath,
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Package signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: "PSPublishModule",
            RepositoryKind: ReleaseRepositoryKind.Library,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready for publish.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: JsonSerializer.Serialize(signingResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        DotNetNuGetPushRequest? captured = null;
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, _) => {
                captured = request;
                return Task.FromResult(new DotNetNuGetPushResult(0, "published", string.Empty, "dotnet", TimeSpan.Zero, timedOut: false, errorMessage: null));
            });

        try
        {
            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");

            var result = await service.ExecuteAsync(queueItem);

            Assert.True(
                result.Succeeded,
                $"{result.Summary} | {string.Join(" | ", result.Receipts.Select(receipt => $"{receipt.TargetName}:{receipt.Status}:{receipt.Summary}"))}");
            Assert.NotNull(captured);
            Assert.Equal(packagePath, captured!.PackagePath);
            Assert.Equal("secret", captured.ApiKey);
            Assert.Equal("https://api.nuget.org/v3/index.json", captured.Source);
            Assert.Equal(Path.GetDirectoryName(packagePath), captured.WorkingDirectory);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal(Domain.Publish.ReleasePublishReceiptStatus.Published, receipt.Status);
            Assert.Contains("dotnet nuget push", receipt.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProjectGitHubPublish_UsesSharedProjectBuildHostPlan()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        File.WriteAllText(Path.Combine(buildDirectory, "Build-Project.ps1"), "# build");

        var zipPath = Path.Combine(repositoryRoot, "Artifacts", "ProjectBuild", "PSPublishModule.1.2.3.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        File.WriteAllText(zipPath, "zip");

        File.WriteAllText(
            Path.Combine(buildDirectory, "project.build.json"),
            """
            {
              "PublishGitHub": true,
              // the shared host config reader should resolve this from the environment
              "GitHubAccessTokenEnvName": "PFGH_TOKEN",
              "GitHubUsername": "EvotecIT",
              "GitHubRepositoryName": "PSPublishModule",
              "GitHubGenerateReleaseNotes": true,
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
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                    ArtifactPath: zipPath,
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Asset signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: "PSPublishModule",
            RepositoryKind: ReleaseRepositoryKind.Library,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready for publish.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: JsonSerializer.Serialize(signingResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        ProjectBuildGitHubPublishRequest? captured = null;
        var projectBuildHostService = new ProjectBuildHostService(
            new NullLogger(),
            executeRelease: spec => new DotNetRepositoryReleaseResult {
                Success = true,
                Projects = {
                    new DotNetRepositoryProjectResult {
                        ProjectName = "PSPublishModule",
                        IsPackable = true,
                        NewVersion = "1.2.3",
                        ReleaseZipPath = zipPath
                    }
                }
            },
            publishGitHub: null,
            validateGitHubPreflight: null);
        var projectBuildPublishHostService = new ProjectBuildPublishHostService(
            new NullLogger(),
            request => {
                captured = request;
                return new ProjectBuildGitHubPublishSummary {
                    Success = true,
                    SummaryTag = "v1.2.3",
                    SummaryReleaseUrl = "https://github.com/EvotecIT/PSPublishModule/releases/tag/v1.2.3",
                    SummaryAssetsCount = 1
                };
            });
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            projectBuildHostService,
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            projectBuildPublishHostService,
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, "published", string.Empty, "dotnet", TimeSpan.Zero, timedOut: false, errorMessage: null)));

        try
        {
            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true")
                .Set("PFGH_TOKEN", "token");

            var result = await service.ExecuteAsync(queueItem);

            Assert.True(
                result.Succeeded,
                $"{result.Summary} | {string.Join(" | ", result.Receipts.Select(receipt => $"{receipt.TargetName}:{receipt.Status}:{receipt.Summary}"))}");
            Assert.NotNull(captured);
            Assert.Equal("EvotecIT", captured!.Owner);
            Assert.Equal("PSPublishModule", captured.Repository);
            Assert.Equal("token", captured.Token);
            Assert.True(captured.GenerateReleaseNotes);
            Assert.Equal("Single", captured.ReleaseMode);
            Assert.Equal(
                zipPath,
                Assert.Single(captured.Release.Projects.Select(project => project.ReleaseZipPath), path => !string.IsNullOrWhiteSpace(path)));
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal(ReleasePublishReceiptStatus.Published, receipt.Status);
            Assert.Contains("GitHub release", receipt.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ModuleGitHubPublish_UsesSharedGitHubReleasePublisher()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        File.WriteAllText(Path.Combine(buildDirectory, "Build-Module.ps1"), "# build");

        var packageDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Artifacts", "Packed", "PSPublishModule")).FullName;
        var manifestPath = Path.Combine(packageDirectory, "PSPublishModule.psd1");
        var zipPath = Path.Combine(repositoryRoot, "Artifacts", "Packed", "PSPublishModule.1.2.3.zip");
        File.WriteAllText(
            manifestPath,
            """
            @{
                RootModule = 'PSPublishModule.psm1'
                ModuleVersion = '1.2.3'
            }
            """);
        File.WriteAllText(zipPath, "zip");

        var signingResult = new ReleaseSigningExecutionResult(
            RootPath: repositoryRoot,
            Succeeded: true,
            Summary: "Signing completed.",
            SourceCheckpointStateJson: null,
            Receipts: [
                new ReleaseSigningReceipt(
                    RootPath: repositoryRoot,
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    ArtifactPath: packageDirectory,
                    ArtifactKind: "Directory",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Package directory signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseSigningReceipt(
                    RootPath: repositoryRoot,
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    ArtifactPath: manifestPath,
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Manifest signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseSigningReceipt(
                    RootPath: repositoryRoot,
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    ArtifactPath: zipPath,
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Asset signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: "PSPublishModule",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Publish,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Ready for publish.",
            CheckpointKey: "publish.ready",
            CheckpointStateJson: JsonSerializer.Serialize(signingResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        GitHubReleasePublishRequest? captured = null;
        var moduleRunner = new StubPowerShellRunner((request) => {
                if (request.InvocationMode != PowerShellInvocationMode.Command || string.IsNullOrWhiteSpace(request.CommandText))
                    return new PowerShellRunResult(1, string.Empty, "Unexpected invocation.", "pwsh");

                if (request.CommandText.Contains("$targetJson =", StringComparison.Ordinal))
                {
                    var match = Regex.Match(request.CommandText, "\\$targetJson = '([^']+)'");
                    if (!match.Success)
                        return new PowerShellRunResult(1, string.Empty, "Export path missing.", "pwsh");

                    File.WriteAllText(
                        match.Groups[1].Value,
                        """
                        {
                          "Segments": [
                            {
                              "Type": "GitHubNuget",
                              "Configuration": {
                                "Destination": "GitHub",
                                "Enabled": true,
                                "UserName": "EvotecIT",
                                "RepositoryName": "PSPublishModule",
                                "ApiKey": "token",
                                "GenerateReleaseNotes": true
                              }
                            }
                          ]
                        }
                        """);

                    return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
                }

                return new PowerShellRunResult(1, string.Empty, "Unexpected command.", "pwsh");
            });
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(moduleRunner),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, "published", string.Empty, "dotnet", TimeSpan.Zero, timedOut: false, errorMessage: null)),
            (request, _) => {
                captured = request;
                return Task.FromResult(new GitHubReleasePublishResult {
                    Succeeded = true,
                    ReleaseCreationSucceeded = true,
                    AllAssetUploadsSucceeded = true,
                    HtmlUrl = "https://github.com/EvotecIT/PSPublishModule/releases/tag/v1.2.3"
                });
            });

        try
        {
            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");

            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal("EvotecIT", captured!.Owner);
            Assert.Equal("PSPublishModule", captured.Repository);
            Assert.Equal("token", captured.Token);
            Assert.Equal("v1.2.3", captured.TagName);
            Assert.Equal("v1.2.3", captured.ReleaseName);
            Assert.True(captured.GenerateReleaseNotes);
            Assert.Equal([zipPath], captured.AssetFilePaths);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal(ReleasePublishReceiptStatus.Published, receipt.Status);
            Assert.Contains("GitHub release v1.2.3 published.", receipt.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ModuleRepositoryPublish_UsesSharedRepositoryPublisher()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Build")).FullName;
        File.WriteAllText(Path.Combine(buildDirectory, "Build-Module.ps1"), "# build");

        var packageDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Artifacts", "Packed", "PSPublishModule")).FullName;
        var manifestPath = Path.Combine(packageDirectory, "PSPublishModule.psd1");
        File.WriteAllText(
            manifestPath,
            """
            @{
                RootModule = 'PSPublishModule.psm1'
                ModuleVersion = '2.0.0'
                PrivateData = @{
                    PSData = @{
                        Prerelease = 'preview1'
                    }
                }
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
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    ArtifactPath: packageDirectory,
                    ArtifactKind: "Directory",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Package directory signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow),
                new ReleaseSigningReceipt(
                    RootPath: repositoryRoot,
                    RepositoryName: "PSPublishModule",
                    AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    ArtifactPath: manifestPath,
                    ArtifactKind: "File",
                    Status: ReleaseSigningReceiptStatus.Signed,
                    Summary: "Manifest signed.",
                    SignedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: "PSPublishModule",
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
        var moduleRunner = new StubPowerShellRunner((request) => {
                if (request.InvocationMode != PowerShellInvocationMode.Command || string.IsNullOrWhiteSpace(request.CommandText))
                    return new PowerShellRunResult(1, string.Empty, "Unexpected invocation.", "pwsh");

                if (request.CommandText.Contains("$targetJson =", StringComparison.Ordinal))
                {
                    var match = Regex.Match(request.CommandText, "\\$targetJson = '([^']+)'");
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
                                "Tool": "PSResourceGet",
                                "ApiKey": "gallery-key",
                                "RepositoryName": "PSGallery"
                              }
                            }
                          ]
                        }
                        """);

                    return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
                }

                return new PowerShellRunResult(1, string.Empty, "Unexpected command.", "pwsh");
            });
        var service = new ReleasePublishExecutionService(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(moduleRunner),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, _) => Task.FromResult(new DotNetNuGetPushResult(0, "published", string.Empty, "dotnet", TimeSpan.Zero, timedOut: false, errorMessage: null)),
            publishRepositoryAsync: (request, _) => {
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
            using var _ = new EnvironmentScope()
                .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");

            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.True(
                captured is not null,
                string.Join(" | ", result.Receipts.Select(receipt => $"{receipt.TargetKind}:{receipt.Status}:{receipt.Summary}")));
            Assert.Equal(packageDirectory, captured!.Path);
            Assert.False(captured.IsNupkg);
            Assert.Equal("PSGallery", captured.RepositoryName);
            Assert.Equal(PublishTool.PSResourceGet, captured.Tool);
            Assert.Equal("gallery-key", captured.ApiKey);
            Assert.True(captured.SkipDependenciesCheck);
            Assert.False(captured.SkipModuleManifestValidate);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal(ReleasePublishReceiptStatus.Published, receipt.Status);
            Assert.Contains("PSGallery", receipt.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PSResourceGet", receipt.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _execute;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> execute)
        {
            _execute = execute;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _execute(request);
    }

    private sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell should not be used for project publish planning when shared host service is available.");
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentScope Set(string name, string? value)
        {
            if (!_originalValues.ContainsKey(name))
                _originalValues[name] = Environment.GetEnvironmentVariable(name);

            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }
    }
}
