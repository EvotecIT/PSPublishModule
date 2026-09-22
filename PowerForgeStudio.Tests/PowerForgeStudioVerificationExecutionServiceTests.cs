using System.Text.Json;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioVerificationExecutionServiceTests
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
    public async Task ExecuteAsync_WingetSubmissionRemainsUnverifiedUntilCatalogAcceptance()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-winget-verification");
        var publishResult = new ReleasePublishExecutionResult(
            root, true, "Submission command completed.", "{}",
            [ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Tool", "UnifiedRelease",
                "EvotecIT.Tool 1.0.0 WinGet submission", "Winget", "Windows Package Manager",
                ReleasePublishReceiptStatus.Published, "Submission command completed.",
                packageId: "EvotecIT.Tool", packageVersion: "1.0.0")]);
        var queueItem = CreateVerifyReadyQueueItem(root, "Tool", ReleaseRepositoryKind.Library,
            JsonSerializer.Serialize(publishResult));

        var result = await new ReleaseVerificationExecutionService().ExecuteAsync(queueItem);

        Assert.False(result.Succeeded);
        var receipt = Assert.Single(result.Receipts);
        Assert.Equal("Winget", receipt.TargetKind);
        Assert.Equal(ReleaseVerificationReceiptStatus.Failed, receipt.Status);
        Assert.Contains("catalog availability have not been verified", receipt.Summary, StringComparison.Ordinal);
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
                    SourcePath: packageScope.PackagePath) {
                    PackageId = "Contoso.ReleaseOps",
                    PackageVersion = "1.2.3"
                }
            ]);

        var queueItem = CreateVerifyReadyQueueItem(publishResult.RootPath, "Contoso.ReleaseOps", ReleaseRepositoryKind.Library, JsonSerializer.Serialize(publishResult));
        using var client = new HttpClient(new StubHttpMessageHandler(request => CreateResponse(request.RequestUri)));
        var service = new ReleaseVerificationExecutionService(client, new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => new PowerShellRunResult(1, string.Empty, string.Empty, "pwsh"))));
        var progress = new VerificationProgressSink();

        var result = await service.ExecuteAsync(queueItem, CancellationToken.None, progress);

        Assert.True(result.Succeeded);
        Assert.Single(result.Receipts);
        Assert.Equal(ReleaseVerificationReceiptStatus.Verified, result.Receipts[0].Status);
        Assert.Contains("packages.contoso.test", result.Receipts[0].Summary);
        Assert.Equal(["Planned", "Checking", "Verified"], progress.Events.Select(static item => item.State));
        Assert.Equal(1, progress.Events[^1].CompletedItems);
        Assert.Equal(1, progress.Events[^1].TotalItems);

        File.Delete(packageScope.PackagePath);
        var reopened = await service.ExecuteAsync(queueItem);
        Assert.True(reopened.Succeeded);
        Assert.Contains("saved publication identity", Assert.Single(reopened.Receipts).Summary);
    }

    [Fact]
    public async Task ExecuteAsync_RedactedPrivateFeed_FailsClearlyWithoutUnauthenticatedProbe()
    {
        const string address = "https://user:password@packages.contoso.test/nuget/v3/index.json?token=secret";
        var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt("root", "Package", "ProjectBuild", "Package.1.2.3.nupkg",
            "NuGet", address, ReleasePublishReceiptStatus.Published, "Published.", packageId: "Package", packageVersion: "1.2.3");
        var publishResult = new ReleasePublishExecutionResult("root", true, "Published.", "{}", [receipt]);
        var queueItem = CreateVerifyReadyQueueItem("root", "Package", ReleaseRepositoryKind.Library, JsonSerializer.Serialize(publishResult));
        var calls = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.OK); }));
        using var service = new ReleaseVerificationExecutionService(client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => new PowerShellRunResult(1, string.Empty, string.Empty, "pwsh"))));

        var result = await service.ExecuteAsync(queueItem);

        Assert.False(result.Succeeded);
        Assert.Equal(0, calls);
        Assert.Contains("matching current project configuration is unavailable", Assert.Single(result.Receipts).Summary);
        Assert.DoesNotContain("password", JsonSerializer.Serialize(result));
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task ExecuteAsync_RedactedPrivateFeed_ReopensMatchingConfigOnlyForInMemoryProbe()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-private-feed-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var build = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            File.WriteAllText(Path.Combine(root, "Package.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            const string address = "https://packages.contoso.test/nuget/v3/index.json?token=private-secret";
            File.WriteAllText(Path.Combine(build, "project.build.json"), JsonSerializer.Serialize(new { PublishSource = address }));
            var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Package", "ProjectBuild", "Package.1.2.3.nupkg",
                "NuGet", address, ReleasePublishReceiptStatus.Published, "Published.", packageId: "Package", packageVersion: "1.2.3");
            var publish = new ReleasePublishExecutionResult(root, true, "Published.", "{}", [receipt]);
            var item = CreateVerifyReadyQueueItem(root, "Package", ReleaseRepositoryKind.Library, JsonSerializer.Serialize(publish));
            var requests = new List<Uri>();
            using var client = new HttpClient(new StubHttpMessageHandler(request => {
                requests.Add(request.RequestUri!);
                return CreateResponse(request.RequestUri);
            }));
            using var service = new ReleaseVerificationExecutionService(client,
                new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => new PowerShellRunResult(1, string.Empty, string.Empty, "pwsh"))));

            var result = await service.ExecuteAsync(item);

            Assert.True(result.Succeeded);
            Assert.Equal(ReleaseVerificationReceiptStatus.Verified, Assert.Single(result.Receipts).Status);
            Assert.Contains(requests, request => request.AbsolutePath.EndsWith("index.json") && request.Query.Contains("private-secret"));
            Assert.DoesNotContain("private-secret", JsonSerializer.Serialize(result));

            File.WriteAllText(Path.Combine(build, "project.build.json"),
                JsonSerializer.Serialize(new { PublishSource = "https://other.contoso.test/nuget/v3/index.json?token=rotated" }));
            var priorCalls = requests.Count;
            var changed = await service.ExecuteAsync(item);
            Assert.False(changed.Succeeded);
            Assert.Equal(priorCalls, requests.Count);
            Assert.Contains("matching current project configuration is unavailable", Assert.Single(changed.Receipts).Summary);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_PrivateFeedUserInfo_UsesBasicAuthOnlyForMatchingOrigin(bool crossOriginPackage)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-private-feed-auth-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var build = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            File.WriteAllText(Path.Combine(root, "Package.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            const string address = "https://feed-user:feed-password@packages.contoso.test/nuget/v3/index.json";
            File.WriteAllText(Path.Combine(build, "project.build.json"), JsonSerializer.Serialize(new { PublishSource = address }));
            var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Package", "ProjectBuild", "Package.1.2.3.nupkg",
                "NuGet", address, ReleasePublishReceiptStatus.Published, "Published.", packageId: "Package", packageVersion: "1.2.3");
            var item = CreateVerifyReadyQueueItem(root, "Package", ReleaseRepositoryKind.Library,
                JsonSerializer.Serialize(new ReleasePublishExecutionResult(root, true, "Published.", "{}", [receipt])));
            var observed = new List<(Uri Address, string? Auth)>();
            using var client = new HttpClient(new StubHttpMessageHandler(request => {
                observed.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
                if (request.RequestUri!.AbsolutePath.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
                    return new HttpResponseMessage(HttpStatusCode.OK) {
                        Content = new StringContent("{\"resources\":[{\"@id\":\"https://" +
                            (crossOriginPackage ? "other.contoso.test" : "packages.contoso.test") +
                            "/v3-flatcontainer/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}" )
                    };
                return CreateResponse(request.RequestUri);
            }));
            using var service = new ReleaseVerificationExecutionService(client,
                new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => new PowerShellRunResult(1, string.Empty, string.Empty, "pwsh"))));

            var result = await service.ExecuteAsync(item);

            Assert.True(result.Succeeded);
            Assert.Equal(2, observed.Count);
            Assert.All(observed, request => Assert.DoesNotContain("feed-password", request.Address.AbsoluteUri));
            Assert.StartsWith("Basic ", observed[0].Auth);
            Assert.Equal(crossOriginPackage ? null : observed[0].Auth, observed[1].Auth);
            Assert.DoesNotContain("feed-password", JsonSerializer.Serialize(result));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalProgressFailureRetainsVerifiedReceiptAndStopsBeforeNextProbe(bool failEveryTerminalWrite)
    {
        using var packageScope = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        var root = Path.GetDirectoryName(packageScope.PackagePath)!;
        var first = new ReleasePublishReceipt(root, "Contoso.ReleaseOps", "ProjectBuild", "Contoso.ReleaseOps.1.2.3.nupkg", "NuGet",
            "https://packages.contoso.test/first/index.json", packageScope.PackagePath, ReleasePublishReceiptStatus.Published,
            "Published.", DateTimeOffset.UtcNow);
        var second = first with { Destination = "https://packages.contoso.test/second/index.json" };
        var published = new ReleasePublishExecutionResult(root, true, "Published.", "{}", [first, second]);
        var queueItem = CreateVerifyReadyQueueItem(root, "Contoso.ReleaseOps", ReleaseRepositoryKind.Library, JsonSerializer.Serialize(published));
        var requests = new List<string>();
        using var client = new HttpClient(new StubHttpMessageHandler(request => {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return CreateResponse(request.RequestUri);
        }));
        using var service = new ReleaseVerificationExecutionService(client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => new PowerShellRunResult(1, string.Empty, string.Empty, "pwsh"))));
        var progress = new TerminalFailingProgressSink(failEveryTerminalWrite);

        var result = await service.ExecuteAsync(queueItem, CancellationToken.None, progress);

        Assert.False(result.Succeeded);
        var receipt = Assert.Single(result.Receipts);
        Assert.Equal(ReleaseVerificationReceiptStatus.Verified, receipt.Status);
        Assert.DoesNotContain(result.Receipts, item => item.Status == ReleaseVerificationReceiptStatus.Failed);
        Assert.Contains("progress update could not be saved", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Single(requests, static request => request.Contains("v3-flatcontainer", StringComparison.OrdinalIgnoreCase));
        Assert.Single(progress.Events, static item => item.State == "Verified");
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
        var service = new ReleaseVerificationExecutionService(
            client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(request => {
                if (request.CommandText is not null && request.CommandText.Contains("Get-PSResourceRepository", StringComparison.Ordinal))
                {
                    return new PowerShellRunResult(
                        0,
                        "{\"Name\":\"PrivateGallery\",\"SourceUri\":\"https://packages.contoso.test/powershell/v3/index.json\",\"PublishUri\":\"https://packages.contoso.test/powershell/api/v2/package\"}",
                        string.Empty,
                        "pwsh");
                }

                return new PowerShellRunResult(1, string.Empty, "Unexpected script", "pwsh");
            })));
        var result = await service.ExecuteAsync(queueItem);

        Assert.True(result.Succeeded);
        Assert.Single(result.Receipts);
        Assert.Equal(ReleaseVerificationReceiptStatus.Verified, result.Receipts[0].Status);
        Assert.Contains("packages.contoso.test", result.Receipts[0].Summary);
        Assert.Contains("2.5.0-preview1", result.Receipts[0].Summary);
    }

    [Fact]
    public async Task TwoFeedsForSamePackageRemainVisibleExecuteIndependentlyAndPersist()
    {
        using var package = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        var root = Path.GetDirectoryName(package.PackagePath)!;
        var first = new ReleasePublishReceipt(root, "Fixture", "ProjectBuild", "Package", "NuGet",
            "https://packages.contoso.test/Feed/index.json", package.PackagePath, ReleasePublishReceiptStatus.Published,
            "Published", DateTimeOffset.UtcNow);
        var second = first with { Destination = "https://packages.contoso.test/feed/index.json" };
        var published = new ReleasePublishExecutionResult(root, true, "Published", "{}", [first, second]);
        var item = CreateVerifyReadyQueueItem(root, "Fixture", ReleaseRepositoryKind.Library, JsonSerializer.Serialize(published));
        var requests = new List<string>();
        using var client = new HttpClient(new StubHttpMessageHandler(request => {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return CreateResponse(request.RequestUri);
        }));
        using var service = new ReleaseVerificationExecutionService(client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => throw new InvalidOperationException("Unexpected PowerShell execution"))));
        var targets = service.BuildPendingTargets([item, item]);
        Assert.Equal(2, targets.Count);
        Assert.Equal(new[] { first.Destination, second.Destination }, targets.Select(target => target.Destination));
        var result = await service.ExecuteAsync(item);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Receipts.Count);
        Assert.All(result.Receipts, receipt => Assert.Equal(ReleaseVerificationReceiptStatus.Verified, receipt.Status));
        Assert.Contains(first.Destination!, requests);
        Assert.Contains(second.Destination!, requests);
        var database = new PowerForgeStudio.Orchestrator.Storage.ReleaseStateDatabase(Path.Combine(root, "state.db"));
        await database.InitializeAsync();
        var session = ReleaseQueueSessionFactory.Create(root, [], DateTimeOffset.UtcNow);
        await database.PersistReleaseCheckpointAsync(session, verificationReceipts: result.Receipts);
        var restored = (await database.LoadReleaseCheckpointAsync(session.SessionId))!.VerificationReceipts;
        Assert.Equal(2, restored.Count);
        Assert.All(result.Receipts, receipt => Assert.Contains(receipt, restored));
    }

    [Fact]
    public void VerificationIdentityRetainsDistinctSourcesAndDoesNotJoinDelimitedFields()
    {
        var first = new ReleasePublishReceipt("root", "Fixture", "ProjectBuild", "Package", "NuGet",
            "feed", "one.nupkg", ReleasePublishReceiptStatus.Published, "Published", DateTimeOffset.UtcNow);
        var receipts = new[] { first, first with { SourcePath = "two.nupkg" },
            first with { TargetName = "a|b", TargetKind = "c" },
            first with { TargetName = "a", TargetKind = "b|c" } };
        var published = new ReleasePublishExecutionResult("root", true, "Published", "{}", receipts);
        var item = CreateVerifyReadyQueueItem("root", "Fixture", ReleaseRepositoryKind.Library, JsonSerializer.Serialize(published));
        using var service = new ReleaseVerificationExecutionService();
        Assert.Equal(4, service.BuildPendingTargets([item, item]).Count);
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
            return new HttpResponseMessage(HttpStatusCode.PartialContent) {
                Content = new ByteArrayContent([0x50, 0x4B, 0x03, 0x04])
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private sealed class VerificationProgressSink : IReleaseArtifactProgressSink
    {
        public List<ReleaseArtifactProgress> Events { get; } = [];

        public ValueTask ReportAsync(ReleaseArtifactProgress progress, CancellationToken cancellationToken = default)
        {
            Events.Add(progress);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TerminalFailingProgressSink(bool failEveryTerminalWrite) : IReleaseArtifactProgressSink
    {
        private bool _failed;
        public List<ReleaseArtifactProgress> Events { get; } = [];

        public ValueTask ReportAsync(ReleaseArtifactProgress progress, CancellationToken cancellationToken = default)
        {
            Events.Add(progress);
            var terminal = progress.State is not ("Planned" or "Checking");
            if (terminal && (failEveryTerminalWrite || !_failed))
            {
                _failed = true;
                throw new IOException("fixture terminal journal failure");
            }
            return ValueTask.CompletedTask;
        }
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

