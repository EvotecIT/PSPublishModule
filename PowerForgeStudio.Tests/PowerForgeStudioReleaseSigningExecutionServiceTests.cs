using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioReleaseSigningExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_NuGetArtifact_UsesSharedDotNetNuGetClient()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var packagePath = Path.Combine(repositoryRoot, "Artifacts", "Package.1.0.0.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        File.WriteAllText(packagePath, "package");

        var buildResult = new ReleaseBuildExecutionResult(
            RootPath: repositoryRoot,
            Succeeded: true,
            Summary: "Build completed.",
            DurationSeconds: 1.0,
            AdapterResults: [
                new ReleaseBuildAdapterResult(
                    ReleaseBuildAdapterKind.ProjectBuild,
                    true,
                    "Project build completed.",
                    0,
                    1.0,
                    [],
                    [packagePath])
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: "PSPublishModule",
            RepositoryKind: ReleaseRepositoryKind.Library,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Sign,
            Status: ReleaseQueueItemStatus.WaitingApproval,
            Summary: "Ready for signing.",
            CheckpointKey: "sign.waiting.usb",
            CheckpointStateJson: JsonSerializer.Serialize(buildResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        DotNetNuGetSignRequest? captured = null;
        var service = new ReleaseSigningExecutionService(
            new ReleaseBuildCheckpointReader(),
            new ReleaseSigningHostSettingsResolver(
                getEnvironmentVariable: name => name switch
                {
                    "RELEASE_OPS_STUDIO_SIGN_THUMBPRINT" => "thumb",
                    "RELEASE_OPS_STUDIO_SIGN_STORE" => "CurrentUser",
                    "RELEASE_OPS_STUDIO_SIGN_TIMESTAMP_URL" => "http://timestamp.digicert.com",
                    _ => null
                },
                resolveModulePath: () => @"C:\Temp\PSPublishModule.psd1"),
            new CertificateFingerprintResolver((_, _) => "ABC123"),
            (request, _) => Task.FromResult(new AuthenticodeSigningHostResult { ExitCode = 0 }),
            (request, _) => {
                captured = request;
                return Task.FromResult(new DotNetNuGetSignResult(0, "signed", string.Empty, "dotnet", TimeSpan.Zero, timedOut: false, errorMessage: null));
            });

        try
        {
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal(packagePath, captured!.PackagePath);
            Assert.Equal("ABC123", captured.CertificateFingerprint);
            Assert.Equal("CurrentUser", captured.CertificateStoreLocation);
            Assert.Equal(Path.GetDirectoryName(packagePath), captured.WorkingDirectory);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal(ReleaseSigningReceiptStatus.Signed, receipt.Status);
            Assert.Contains("dotnet nuget sign", receipt.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_DirectoryArtifact_UsesSharedAuthenticodeSigningHost()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var artifactDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Artifacts", "Module")).FullName;
        File.WriteAllText(Path.Combine(artifactDirectory, "PSPublishModule.psd1"), "@{}");

        var buildResult = new ReleaseBuildExecutionResult(
            RootPath: repositoryRoot,
            Succeeded: true,
            Summary: "Build completed.",
            DurationSeconds: 1.0,
            AdapterResults: [
                new ReleaseBuildAdapterResult(
                    ReleaseBuildAdapterKind.ModuleBuild,
                    true,
                    "Module build completed.",
                    0,
                    1.0,
                    [artifactDirectory],
                    [])
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: "PSPublishModule",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Sign,
            Status: ReleaseQueueItemStatus.WaitingApproval,
            Summary: "Ready for signing.",
            CheckpointKey: "sign.waiting.usb",
            CheckpointStateJson: JsonSerializer.Serialize(buildResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        AuthenticodeSigningHostRequest? captured = null;
        var service = new ReleaseSigningExecutionService(
            new ReleaseBuildCheckpointReader(),
            new ReleaseSigningHostSettingsResolver(
                getEnvironmentVariable: name => name switch
                {
                    "RELEASE_OPS_STUDIO_SIGN_THUMBPRINT" => "thumb",
                    "RELEASE_OPS_STUDIO_SIGN_STORE" => "CurrentUser",
                    "RELEASE_OPS_STUDIO_SIGN_TIMESTAMP_URL" => "http://timestamp.digicert.com",
                    _ => null
                },
                resolveModulePath: () => @"C:\Temp\PSPublishModule.psd1"),
            new CertificateFingerprintResolver((_, _) => "ABC123"),
            (request, _) => {
                captured = request;
                return Task.FromResult(new AuthenticodeSigningHostResult {
                    ExitCode = 0,
                    Duration = TimeSpan.Zero,
                    StandardOutput = "signed"
                });
            },
            (request, _) => Task.FromResult(new DotNetNuGetSignResult(0, "signed", string.Empty, "dotnet", TimeSpan.Zero, timedOut: false, errorMessage: null)));

        try
        {
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal(artifactDirectory, captured!.SigningPath);
            Assert.Contains("*.ps1", captured.IncludePatterns);
            Assert.Contains("*.psd1", captured.IncludePatterns);
            var receipt = Assert.Single(result.Receipts);
            Assert.Equal(ReleaseSigningReceiptStatus.Signed, receipt.Status);
            Assert.Contains("Authenticode signing completed", receipt.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_UsesSharedSigningSettingsResolver()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var artifactDirectory = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Artifacts", "Module")).FullName;
        File.WriteAllText(Path.Combine(artifactDirectory, "PSPublishModule.psd1"), "@{}");

        var buildResult = new ReleaseBuildExecutionResult(
            RootPath: repositoryRoot,
            Succeeded: true,
            Summary: "Build completed.",
            DurationSeconds: 1.0,
            AdapterResults: [
                new ReleaseBuildAdapterResult(
                    ReleaseBuildAdapterKind.ModuleBuild,
                    true,
                    "Module build completed.",
                    0,
                    1.0,
                    [artifactDirectory],
                    [])]);

        var queueItem = new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: "PSPublishModule",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Sign,
            Status: ReleaseQueueItemStatus.WaitingApproval,
            Summary: "Ready for signing.",
            CheckpointKey: "sign.waiting.usb",
            CheckpointStateJson: JsonSerializer.Serialize(buildResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        AuthenticodeSigningHostRequest? captured = null;
        var service = new ReleaseSigningExecutionService(
            new ReleaseBuildCheckpointReader(),
            new ReleaseSigningHostSettingsResolver(
                getEnvironmentVariable: name => name switch
                {
                    "RELEASE_OPS_STUDIO_SIGN_THUMBPRINT" => "thumb",
                    "RELEASE_OPS_STUDIO_SIGN_STORE" => "LocalMachine",
                    "RELEASE_OPS_STUDIO_SIGN_TIMESTAMP_URL" => "https://timestamp.contoso.test",
                    _ => null
                },
                resolveModulePath: () => @"C:\Resolved\PSPublishModule.psd1"),
            new CertificateFingerprintResolver((_, _) => "ABC123"),
            (request, _) => {
                captured = request;
                return Task.FromResult(new AuthenticodeSigningHostResult { ExitCode = 0 });
            },
            (request, _) => Task.FromResult(new DotNetNuGetSignResult(0, "signed", string.Empty, "dotnet", TimeSpan.Zero, timedOut: false, errorMessage: null)));

        try
        {
            var result = await service.ExecuteAsync(queueItem);

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal("thumb", captured!.Thumbprint);
            Assert.Equal("LocalMachine", captured.StoreName);
            Assert.Equal("https://timestamp.contoso.test", captured.TimeStampServer);
            Assert.Equal(@"C:\Resolved\PSPublishModule.psd1", captured.ModulePath);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }
}
