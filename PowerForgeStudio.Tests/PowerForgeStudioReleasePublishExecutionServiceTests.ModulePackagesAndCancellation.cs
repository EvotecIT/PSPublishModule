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
    public async Task ExecuteAsync_loads_outer_package_publish_settings_from_unified_contract()
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
                    "PublishGitHub": false,
                    "PublishApiKey": "nested-key",
                    "PublishSource": "https://nested.example.test/v3/index.json"
                  }
                }
                """);
            var signedPackage = Path.Combine(repositoryRoot, "Sample.Library.1.0.0.nupkg");
            File.WriteAllText(signedPackage, "signed-package");
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult { Success = true, ConfigPath = releaseConfig },
                [
                    new ReleaseSigningReceipt(
                        repositoryRoot,
                        "Sample",
                        ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                        signedPackage,
                        "File",
                        ReleaseSigningReceiptStatus.Signed,
                        "Signed.",
                        DateTimeOffset.UtcNow)
                ]);
            DotNetNuGetPushRequest? capturedPush = null;
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                new ProjectBuildPublishHostService(),
                (request, _) =>
                {
                    capturedPush = request;
                    return Task.FromResult(new DotNetNuGetPushResult(
                        0,
                        "published",
                        string.Empty,
                        "dotnet",
                        TimeSpan.Zero,
                        timedOut: false,
                        errorMessage: null));
                },
                publishUnifiedRelease: (_, _) => new PowerForgeReleaseResult { Success = true });

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.NotNull(capturedPush);
            Assert.Equal(signedPackage, capturedPush!.PackagePath);
            Assert.Equal("nested-key", capturedPush.ApiKey);
            Assert.Equal("https://nested.example.test/v3/index.json", capturedPush.Source);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_publishes_signed_module_owned_package_without_rebuilding()
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
            var projectPath = Path.Combine(repositoryRoot, "Sample.Library.csproj");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Version>1.0.0</Version>
                    <PackageId>Sample.Library</PackageId>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
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
                        "ExpectedVersion": "1.0.0",
                        "IncludeProjects": [ "Sample.Library" ],
                        "UpdateVersions": false,
                        "Build": true,
                        "PublishNuget": true,
                        "PublishGitHub": false,
                        "PublishApiKey": "test-key",
                        "PublishSource": "https://example.test/v3/index.json"
                      }
                    }
                  ]
                }
                """);
            File.WriteAllText(
                releaseConfig,
                """{ "Module": { "RepositoryRoot": "..", "ConfigPath": "powerforge.json", "IncludesPackages": true } }""");
            var signedPackage = Path.Combine(repositoryRoot, "Sample.Library.1.0.0.nupkg");
            File.WriteAllText(signedPackage, "signed-package");
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult { Success = true, ConfigPath = releaseConfig },
                [
                    new ReleaseSigningReceipt(
                        repositoryRoot,
                        "Sample",
                        ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                        signedPackage,
                        "File",
                        ReleaseSigningReceiptStatus.Signed,
                        "Signed.",
                        DateTimeOffset.UtcNow)
                ]);
            DotNetNuGetPushRequest? capturedPush = null;
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                new ProjectBuildPublishHostService(),
                (request, _) =>
                {
                    capturedPush = request;
                    return Task.FromResult(new DotNetNuGetPushResult(
                        0,
                        "published",
                        string.Empty,
                        "dotnet",
                        TimeSpan.Zero,
                        timedOut: false,
                        errorMessage: null));
                },
                publishUnifiedRelease: (_, _) => new PowerForgeReleaseResult { Success = true });

            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.NotNull(capturedPush);
            Assert.Equal(signedPackage, capturedPush!.PackagePath);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal("ModulePackages", receipt.TargetKind);
            Assert.Contains("without rebuilding", receipt.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_publishes_signed_module_owned_github_assets_without_rebuilding()
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
                Path.Combine(repositoryRoot, "Sample.Library.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Version>1.0.0</Version>
                    <PackageId>Sample.Library</PackageId>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
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
                        "ExpectedVersion": "1.0.0",
                        "IncludeProjects": [ "Sample.Library" ],
                        "UpdateVersions": false,
                        "Build": true,
                        "PublishNuget": false,
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
            var signedZip = Path.Combine(repositoryRoot, "Sample.Library.1.0.0.zip");
            File.WriteAllText(signedZip, "signed-zip");
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult { Success = true, ConfigPath = releaseConfig },
                [
                    new ReleaseSigningReceipt(
                        repositoryRoot,
                        "Sample",
                        ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                        signedZip,
                        "File",
                        ReleaseSigningReceiptStatus.Signed,
                        "Signed.",
                        DateTimeOffset.UtcNow)
                ]);
            ProjectBuildGitHubPublishRequest? capturedPublish = null;
            var publishHost = new ProjectBuildPublishHostService(
                new NullLogger(),
                request =>
                {
                    capturedPublish = request;
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
            Assert.NotNull(capturedPublish);
            Assert.Contains(
                capturedPublish!.Release.Projects,
                project => string.Equals(project.ReleaseZipPath, signedZip, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                result.Receipts,
                receipt =>
                    receipt.TargetKind == "ModulePackages" &&
                    receipt.Status == ReleasePublishReceiptStatus.Published);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_forwards_cancellation_to_staged_unified_publication()
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
                """{ "GitHub": { "Publish": true, "Owner": "EvotecIT", "Repository": "Sample" } }""");
            var asset = Path.Combine(repositoryRoot, "Sample.zip");
            File.WriteAllText(asset, "asset");
            var queueItem = CreateUnifiedPublishQueueItem(
                repositoryRoot,
                "Sample",
                releaseConfig,
                new PowerForgeReleaseResult
                {
                    Success = true,
                    ConfigPath = releaseConfig,
                    ReleaseAssets = [asset]
                },
                []);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new ReleasePublishExecutionService(
                new RepositoryCatalogScanner(),
                new ModuleBuildHostService(),
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(),
                new ProjectBuildPublishHostService(),
                (_, _) => Task.FromResult(new DotNetNuGetPushResult(
                    0,
                    string.Empty,
                    string.Empty,
                    "dotnet",
                    TimeSpan.Zero,
                    false,
                    null)),
                publishUnifiedReleaseWithCancellation: (_, _, cancellationToken) =>
                {
                    Assert.True(cancellationToken.CanBeCanceled);
                    entered.SetResult();
                    cancellationToken.WaitHandle.WaitOne();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Cancellation was not observed.");
                });
            using var cancellation = new CancellationTokenSource();
            using var _ = new EnvironmentScope().Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", "true");

            var execution = service.ExecuteAsync(queueItem, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    private static ReleaseQueueItem CreateUnifiedPublishQueueItem(
        string repositoryRoot,
        string repositoryName,
        string releaseConfig,
        PowerForgeReleaseResult unified,
        IReadOnlyList<ReleaseSigningReceipt> receipts)
    {
        var buildResult = new ReleaseBuildExecutionResult(
            repositoryRoot,
            true,
            "Build completed.",
            1,
            [],
            UnifiedReleaseStateJson: JsonSerializer.Serialize(unified),
            UnifiedReleaseConfigSha256: UnifiedReleaseConfigFingerprint.Compute(releaseConfig));
        var signingResult = new ReleaseSigningExecutionResult(
            repositoryRoot,
            true,
            "Signing completed.",
            JsonSerializer.Serialize(buildResult),
            receipts);
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
            JsonSerializer.Serialize(signingResult),
            DateTimeOffset.UtcNow);
    }
}
