using System.Text.Json;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioVerificationExecutionServiceTests
{
    [Fact]
    public void BuildPendingTargets_VerifyReadyItem_ReturnsTargetsFromPublishCheckpoint()
    {
        var publishResult = new ReleasePublishExecutionResult(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            Succeeded: true,
            Summary: "Publish completed cleanly.",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleasePublishReceipt(
                    RootPath: @"C:\Support\GitHub\DbaClientX",
                    RepositoryName: "DbaClientX",
                    AdapterKind: "ProjectBuild",
                    TargetName: "GitHub release",
                    TargetKind: "GitHub",
                    Destination: "https://github.com/EvotecIT/DbaClientX/releases/tag/v0.2.0",
                    Status: ReleasePublishReceiptStatus.Published,
                    Summary: "Published.",
                    PublishedAtUtc: DateTimeOffset.UtcNow,
                    SourcePath: @"C:\Support\GitHub\DbaClientX\Artefacts\ProjectBuild\DbaClientX.zip")
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: publishResult.RootPath,
            RepositoryName: "DbaClientX",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Verify,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Verification is ready.",
            CheckpointKey: "verify.ready",
            CheckpointStateJson: JsonSerializer.Serialize(publishResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var service = new ReleaseVerificationExecutionService();
        var targets = service.BuildPendingTargets([queueItem]);

        Assert.Single(targets);
        Assert.Equal("GitHub release", targets[0].TargetName);
        Assert.Equal("GitHub", targets[0].TargetKind);
    }

    [Fact]
    public async Task ExecuteAsync_UnpublishedReceipt_FailsVerificationWithoutNetworkProbe()
    {
        var publishResult = new ReleasePublishExecutionResult(
            RootPath: @"C:\Support\GitHub\PSWriteHTML",
            Succeeded: false,
            Summary: "Publish failed.",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleasePublishReceipt(
                    RootPath: @"C:\Support\GitHub\PSWriteHTML",
                    RepositoryName: "PSWriteHTML",
                    AdapterKind: "ModuleBuild",
                    TargetName: "Module publish",
                    TargetKind: "PowerShellRepository",
                    Destination: "PSGallery",
                    SourcePath: null,
                    Status: ReleasePublishReceiptStatus.Failed,
                    Summary: "Publish is disabled.",
                    PublishedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var queueItem = new ReleaseQueueItem(
            RootPath: publishResult.RootPath,
            RepositoryName: "PSWriteHTML",
            RepositoryKind: ReleaseRepositoryKind.Module,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Verify,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Verification is ready.",
            CheckpointKey: "verify.ready",
            CheckpointStateJson: JsonSerializer.Serialize(publishResult),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var service = new ReleaseVerificationExecutionService();
        var result = await service.ExecuteAsync(queueItem);

        Assert.False(result.Succeeded);
        Assert.Single(result.Receipts);
        Assert.Equal(ReleaseVerificationReceiptStatus.Failed, result.Receipts[0].Status);
        Assert.Contains("Publish receipt status", result.Receipts[0].Summary);
    }

    [Fact]
    public async Task ExecuteAsync_SkippedPublishReceipt_SkipsVerificationInsteadOfFailing()
    {
        var publishResult = new ReleasePublishExecutionResult(
            RootPath: @"C:\Support\GitHub\PSPublishModule",
            Succeeded: true,
            Summary: "Nothing needed publishing.",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleasePublishReceipt(
                    RootPath: @"C:\Support\GitHub\PSPublishModule",
                    RepositoryName: "PSPublishModule",
                    AdapterKind: "Publish",
                    TargetName: "Publish",
                    TargetKind: "Publish",
                    Destination: null,
                    SourcePath: null,
                    Status: ReleasePublishReceiptStatus.Skipped,
                    Summary: "No external publish targets were detected for this queue item, so verification can be skipped.",
                    PublishedAtUtc: DateTimeOffset.UtcNow)
            ]);

        var queueItem = CreateVerifyReadyQueueItem(publishResult.RootPath, "PSPublishModule", ReleaseRepositoryKind.Mixed, JsonSerializer.Serialize(publishResult));
        var service = new ReleaseVerificationExecutionService();

        var result = await service.ExecuteAsync(queueItem);

        Assert.True(result.Succeeded);
        var receipt = Assert.Single(result.Receipts);
        Assert.Equal(ReleaseVerificationReceiptStatus.Skipped, receipt.Status);
        Assert.Contains("verification can be skipped", receipt.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_CustomNuGetV3Feed_VerifiesPackageAgainstConfiguredFeed()
    {
        using var packageScope = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        var publishResult = new ReleasePublishExecutionResult(
            RootPath: @"C:\Support\GitHub\Contoso.ReleaseOps",
            Succeeded: true,
            Summary: "Publish completed.",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleasePublishReceipt(
                    RootPath: @"C:\Support\GitHub\Contoso.ReleaseOps",
                    RepositoryName: "Contoso.ReleaseOps",
                    AdapterKind: "ProjectBuild",
                    TargetName: "Contoso.ReleaseOps.1.2.3.nupkg",
                    TargetKind: "NuGet",
                    Destination: "https://packages.contoso.test/nuget/v3/index.json",
                    Status: ReleasePublishReceiptStatus.Published,
                    Summary: "Published.",
                    PublishedAtUtc: DateTimeOffset.UtcNow,
                    SourcePath: packageScope.PackagePath)
            ]);

        var queueItem = CreateVerifyReadyQueueItem(publishResult.RootPath, "Contoso.ReleaseOps", ReleaseRepositoryKind.Library, JsonSerializer.Serialize(publishResult));
        using var client = new HttpClient(new StubHttpMessageHandler(request => CreateResponse(request.RequestUri)));
        var service = new ReleaseVerificationExecutionService(client, (_, _, _) => Task.FromResult(new PowerShellExecutionResult(1, TimeSpan.Zero, string.Empty, string.Empty)));

        var result = await service.ExecuteAsync(queueItem);

        Assert.True(result.Succeeded);
        Assert.Single(result.Receipts);
        Assert.Equal(ReleaseVerificationReceiptStatus.Verified, result.Receipts[0].Status);
        Assert.Contains("packages.contoso.test", result.Receipts[0].Summary);
    }

    [Fact]
    public async Task ExecuteAsync_CustomPowerShellRepository_VerifiesModuleAgainstResolvedFeed()
    {
        using var moduleScope = CreateTemporaryModule("ContosoModule", "2.5.0", "preview1");
        var publishResult = new ReleasePublishExecutionResult(
            RootPath: @"C:\Support\GitHub\ContosoModule",
            Succeeded: true,
            Summary: "Publish completed.",
            SourceCheckpointStateJson: "{}",
            Receipts: [
                new ReleasePublishReceipt(
                    RootPath: @"C:\Support\GitHub\ContosoModule",
                    RepositoryName: "ContosoModule",
                    AdapterKind: "ModuleBuild",
                    TargetName: "ContosoModule",
                    TargetKind: "PowerShellRepository",
                    Destination: "PrivateGallery",
                    Status: ReleasePublishReceiptStatus.Published,
                    Summary: "Published.",
                    PublishedAtUtc: DateTimeOffset.UtcNow,
                    SourcePath: moduleScope.ModuleRoot)
            ]);

        var queueItem = CreateVerifyReadyQueueItem(publishResult.RootPath, "ContosoModule", ReleaseRepositoryKind.Module, JsonSerializer.Serialize(publishResult));
        using var client = new HttpClient(new StubHttpMessageHandler(request => CreateResponse(request.RequestUri)));
        Task<PowerShellExecutionResult> PowerShellResolver(string _, string script, CancellationToken __)
        {
            if (script.Contains("Get-PSResourceRepository", StringComparison.Ordinal))
            {
                return Task.FromResult(new PowerShellExecutionResult(
                    0,
                    TimeSpan.Zero,
                    "{\"Name\":\"PrivateGallery\",\"SourceUri\":\"https://packages.contoso.test/powershell/v3/index.json\",\"PublishUri\":\"https://packages.contoso.test/powershell/api/v2/package\"}",
                    string.Empty));
            }

            if (script.Contains("Import-PowerShellDataFile", StringComparison.Ordinal))
            {
                return Task.FromResult(new PowerShellExecutionResult(
                    0,
                    TimeSpan.Zero,
                    "{\"ModuleName\":\"ContosoModule\",\"ModuleVersion\":\"2.5.0\",\"PreRelease\":\"preview1\"}",
                    string.Empty));
            }

            return Task.FromResult(new PowerShellExecutionResult(1, TimeSpan.Zero, string.Empty, "Unexpected script"));
        }

        var service = new ReleaseVerificationExecutionService(client, PowerShellResolver);
        var result = await service.ExecuteAsync(queueItem);

        Assert.True(result.Succeeded);
        Assert.Single(result.Receipts);
        Assert.Equal(ReleaseVerificationReceiptStatus.Verified, result.Receipts[0].Status);
        Assert.Contains("packages.contoso.test", result.Receipts[0].Summary);
        Assert.Contains("2.5.0-preview1", result.Receipts[0].Summary);
    }

    private static ReleaseQueueItem CreateVerifyReadyQueueItem(string rootPath, string repositoryName, ReleaseRepositoryKind repositoryKind, string checkpointStateJson)
        => new(
            RootPath: rootPath,
            RepositoryName: repositoryName,
            RepositoryKind: repositoryKind,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: ReleaseQueueStage.Verify,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Verification is ready.",
            CheckpointKey: "verify.ready",
            CheckpointStateJson: checkpointStateJson,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    private static HttpResponseMessage CreateResponse(Uri? requestUri)
    {
        var path = requestUri?.AbsolutePath ?? string.Empty;
        if (path.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("{\"resources\":[{\"@id\":\"https://packages.contoso.test/v3-flatcontainer/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}")
            };
        }

        if (path.Contains("/v3-flatcontainer/", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static TemporaryPackageScope CreateTemporaryPackage(string packageId, string version)
    {
        var root = Path.Combine(Path.GetTempPath(), $"releaseopsstudio-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var packagePath = Path.Combine(root, $"{packageId}.{version}.nupkg");
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"{packageId}.nuspec");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write($"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>{packageId}</id>
                <version>{version}</version>
              </metadata>
            </package>
            """);

        return new TemporaryPackageScope(root, packagePath);
    }

    private static TemporaryModuleScope CreateTemporaryModule(string moduleName, string version, string preRelease)
    {
        var root = Path.Combine(Path.GetTempPath(), $"releaseopsstudio-module-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, moduleName);
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(
            Path.Combine(moduleRoot, $"{moduleName}.psd1"),
            "@{" + Environment.NewLine +
            $"    RootModule = '{moduleName}.psm1'" + Environment.NewLine +
            $"    ModuleVersion = '{version}'" + Environment.NewLine +
            "    PrivateData = @{" + Environment.NewLine +
            "        PSData = @{" + Environment.NewLine +
            $"            Prerelease = '{preRelease}'" + Environment.NewLine +
            "        }" + Environment.NewLine +
            "    }" + Environment.NewLine +
            "}" + Environment.NewLine);
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), "function Test-PowerForgeStudio { }");
        return new TemporaryModuleScope(root, moduleRoot);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }

    private sealed class TemporaryPackageScope(string rootPath, string packagePath) : IDisposable
    {
        public string PackagePath { get; } = packagePath;

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private sealed class TemporaryModuleScope(string rootPath, string moduleRoot) : IDisposable
    {
        public string ModuleRoot { get; } = moduleRoot;

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}

