using System.Net;
using System.Net.Http;
using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioVerificationExecutionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ModuleOwnedPrivateFeed_ReopensOnlyMatchingJsonLane(bool referencedProjectBuild)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-module-private-feed-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            const string address = "https://packages.contoso.test/nuget/v3/index.json?token=module-secret";
            var moduleConfig = Path.Combine(root, "powerforge.json");
            var projectConfig = Path.Combine(root, "Module", "project.build.json");
            Directory.CreateDirectory(Path.GetDirectoryName(projectConfig)!);
            if (referencedProjectBuild)
            {
                File.WriteAllText(projectConfig, JsonSerializer.Serialize(new { PublishNuget = true,
                    PublishSource = "https://unused.contoso.test/nuget/v3/index.json" }));
                File.WriteAllText(moduleConfig, JsonSerializer.Serialize(new {
                    Build = new { Name = "Sample", SourcePath = "Module" },
                    Segments = new[] { new { Type = "ProjectBuild", Configuration = new {
                        Enabled = true, ConfigPath = "project.build.json", Options = new { PublishSource = address } } } }
                }));
                var build = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
                File.WriteAllText(Path.Combine(build, "release.json"),
                    """{ "Module": { "RepositoryRoot": "..", "ConfigPath": "powerforge.json", "IncludesPackages": true } }""");
            }
            else
            {
                File.WriteAllText(moduleConfig, JsonSerializer.Serialize(new {
                    Build = new { Name = "Sample", SourcePath = "." },
                    Segments = new[] { new { Type = "PackageBuild", Configuration = new {
                        Name = "Sample packages", PublishNuget = true, PublishSource = address } } }
                }));
            }

            var packagePath = Path.Combine(root, "Package.1.2.3.nupkg");
            var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Sample", "UnifiedRelease",
                Path.GetFileName(packagePath), "ModulePackages", address, ReleasePublishReceiptStatus.Published,
                "Published.", packagePath, "Package", "1.2.3");
            Assert.True(receipt.DestinationCredentialsOmitted);
            Assert.DoesNotContain("module-secret", receipt.Destination);
            var item = CreateVerifyReadyQueueItem(root, "Sample", ReleaseRepositoryKind.Module,
                JsonSerializer.Serialize(new ReleasePublishExecutionResult(root, true, "Published.", "{}", [receipt])));
            var requests = new List<Uri>();
            using var client = new HttpClient(new StubHttpMessageHandler(request => {
                requests.Add(request.RequestUri!);
                return CreateResponse(request.RequestUri);
            }));
            using var service = new ReleaseVerificationExecutionService(client,
                new PowerShellRepositoryResolver(new StubPowerShellRunner(_ =>
                    throw new InvalidOperationException("Verification must not execute PowerShell."))));

            var verified = await service.ExecuteAsync(item);
            Assert.True(verified.Succeeded, verified.Summary);
            Assert.Equal(ReleaseVerificationReceiptStatus.Verified, Assert.Single(verified.Receipts).Status);
            Assert.Contains(requests, request => request.Query.Contains("module-secret", StringComparison.Ordinal));
            Assert.DoesNotContain("module-secret", JsonSerializer.Serialize(verified));

            if (referencedProjectBuild)
                File.WriteAllText(moduleConfig, JsonSerializer.Serialize(new {
                    Build = new { Name = "Sample", SourcePath = "Module" },
                    Segments = new[] { new { Type = "ProjectBuild", Configuration = new {
                        Enabled = true, ConfigPath = "project.build.json", Options = new {
                            PublishSource = "https://other.contoso.test/nuget/v3/index.json?token=rotated" } } } }
                }));
            else
                File.WriteAllText(moduleConfig, JsonSerializer.Serialize(new {
                    Build = new { Name = "Sample", SourcePath = "." },
                    Segments = new[] { new { Type = "PackageBuild", Configuration = new {
                        Name = "Sample packages", PublishNuget = true,
                        PublishSource = "https://other.contoso.test/nuget/v3/index.json?token=rotated" } } }
                }));
            var priorCalls = requests.Count;
            var changed = await service.ExecuteAsync(item);
            Assert.False(changed.Succeeded);
            Assert.Equal(priorCalls, requests.Count);
            Assert.Contains("matching current project configuration is unavailable", Assert.Single(changed.Receipts).Summary);
            Assert.DoesNotContain("rotated", JsonSerializer.Serialize(changed));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ExecuteAsync_ModuleOwnedPrivateFeed_RejectsAmbiguousRedactedLanes()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-module-feed-ambiguous-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            const string address = "https://packages.contoso.test/nuget/v3/index.json?token=first-secret";
            File.WriteAllText(Path.Combine(root, "powerforge.json"), JsonSerializer.Serialize(new {
                Build = new { Name = "Sample", SourcePath = "." },
                Segments = new[] {
                    new { Type = "PackageBuild", Configuration = new { Name = "First", PublishNuget = true, PublishSource = address } },
                    new { Type = "PackageBuild", Configuration = new { Name = "Second", PublishNuget = true,
                        PublishSource = "https://packages.contoso.test/nuget/v3/index.json?token=second-secret" } }
                }
            }));
            var packagePath = Path.Combine(root, "Package.1.2.3.nupkg");
            var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Sample", "UnifiedRelease",
                Path.GetFileName(packagePath), "ModulePackages", address, ReleasePublishReceiptStatus.Published,
                "Published.", packagePath, "Package", "1.2.3");
            var item = CreateVerifyReadyQueueItem(root, "Sample", ReleaseRepositoryKind.Module,
                JsonSerializer.Serialize(new ReleasePublishExecutionResult(root, true, "Published.", "{}", [receipt])));
            var calls = 0;
            using var client = new HttpClient(new StubHttpMessageHandler(_ => {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            using var service = new ReleaseVerificationExecutionService(client,
                new PowerShellRepositoryResolver(new StubPowerShellRunner(_ =>
                    throw new InvalidOperationException("Verification must not execute PowerShell."))));

            var result = await service.ExecuteAsync(item);
            Assert.False(result.Succeeded);
            Assert.Equal(0, calls);
            Assert.DoesNotContain("first-secret", JsonSerializer.Serialize(result));
            Assert.DoesNotContain("second-secret", JsonSerializer.Serialize(result));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ExecuteAsync_ScriptBackedModulePrivateFeed_DoesNotRunScriptForVerification()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-module-feed-script-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var build = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            File.WriteAllText(Path.Combine(build, "Build-Module.ps1"), "throw 'Must not execute during verification'");
            const string address = "https://packages.contoso.test/nuget/v3/index.json?token=module-secret";
            var packagePath = Path.Combine(root, "Package.1.2.3.nupkg");
            var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Sample", "UnifiedRelease",
                Path.GetFileName(packagePath), "ModulePackages", address, ReleasePublishReceiptStatus.Published,
                "Published.", packagePath, "Package", "1.2.3");
            var item = CreateVerifyReadyQueueItem(root, "Sample", ReleaseRepositoryKind.Module,
                JsonSerializer.Serialize(new ReleasePublishExecutionResult(root, true, "Published.", "{}", [receipt])));
            var calls = 0;
            using var client = new HttpClient(new StubHttpMessageHandler(_ => {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            using var service = new ReleaseVerificationExecutionService(client,
                new PowerShellRepositoryResolver(new StubPowerShellRunner(_ =>
                    throw new InvalidOperationException("Verification must not execute PowerShell."))));

            var result = await service.ExecuteAsync(item);
            Assert.False(result.Succeeded);
            Assert.Equal(0, calls);
            Assert.Contains("matching current project configuration is unavailable", Assert.Single(result.Receipts).Summary);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ExecuteAsync_ModuleOwnedNuGetPackage_VerifiesLocalFeed()
    {
        using var package = CreateTemporaryPackage("Contoso.ReleaseOps", "1.2.3");
        var root = Path.GetDirectoryName(package.PackagePath)!;
        var feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
        var deliveredPath = Path.Combine(feed, Path.GetFileName(package.PackagePath));
        File.Copy(package.PackagePath, deliveredPath);
        var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Contoso.ReleaseOps", "UnifiedRelease",
            Path.GetFileName(package.PackagePath), "ModulePackages", feed, ReleasePublishReceiptStatus.Published,
            "Published.", package.PackagePath, "Contoso.ReleaseOps", "1.2.3");
        var signing = new ReleaseSigningExecutionResult(root, true, "Signed.", "{}", [
            ReleaseSigningArtifactIntegrity.Capture(new ReleaseSigningReceipt(root, "Contoso.ReleaseOps", "ModuleBuild",
                package.PackagePath, "NuGetPackage", ReleaseSigningReceiptStatus.Signed, "Captured package fixture.",
                DateTimeOffset.UtcNow))
        ]);
        var published = new ReleasePublishExecutionResult(root, true, "Published.", JsonSerializer.Serialize(signing), [receipt]);
        var item = CreateVerifyReadyQueueItem(root, "Contoso.ReleaseOps", ReleaseRepositoryKind.Module,
            JsonSerializer.Serialize(published));
        File.Delete(package.PackagePath);
        using var service = new ReleaseVerificationExecutionService();

        var verified = await service.ExecuteAsync(item);
        Assert.True(verified.Succeeded, verified.Summary);
        Assert.Equal(ReleaseVerificationReceiptStatus.Verified, Assert.Single(verified.Receipts).Status);

        File.Delete(deliveredPath);
        var missing = await service.ExecuteAsync(item);
        Assert.False(missing.Succeeded);
        Assert.Equal(ReleaseVerificationReceiptStatus.Failed, Assert.Single(missing.Receipts).Status);
    }

    [Fact]
    public async Task ExecuteAsync_ModuleOwnedGitHubRelease_ProbesRecordedUrl()
    {
        const string root = "C:/workspace/Contoso.ReleaseOps";
        var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Contoso.ReleaseOps", "UnifiedRelease",
            "Docs GitHub release", "ModulePackages", "https://github.com/Contoso/ReleaseOps/releases/tag/v1.2.3",
            ReleasePublishReceiptStatus.Published, "Published.");
        var published = new ReleasePublishExecutionResult(root, true, "Published.", "{}", [receipt]);
        var item = CreateVerifyReadyQueueItem(root, "Contoso.ReleaseOps", ReleaseRepositoryKind.Module,
            JsonSerializer.Serialize(published));
        var calls = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(request => {
            calls++;
            Assert.Equal("github.com", request.RequestUri!.Host);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var service = new ReleaseVerificationExecutionService(client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ =>
                throw new InvalidOperationException("PowerShell was not expected."))));

        var verified = await service.ExecuteAsync(item);

        Assert.True(verified.Succeeded, verified.Summary);
        Assert.Equal(ReleaseVerificationReceiptStatus.Verified, Assert.Single(verified.Receipts).Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_ModuleOwnedUnknownTarget_FailsBeforeNetworkProbe()
    {
        const string root = "C:/workspace/Contoso.ReleaseOps";
        var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Contoso.ReleaseOps", "UnifiedRelease",
            "Unclassified asset", "ModulePackages", "https://example.test/unclassified",
            ReleasePublishReceiptStatus.Published, "Published.");
        var published = new ReleasePublishExecutionResult(root, true, "Published.", "{}", [receipt]);
        var item = CreateVerifyReadyQueueItem(root, "Contoso.ReleaseOps", ReleaseRepositoryKind.Module,
            JsonSerializer.Serialize(published));
        var calls = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(_ => {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var service = new ReleaseVerificationExecutionService(client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ =>
                throw new InvalidOperationException("PowerShell was not expected."))));

        var result = await service.ExecuteAsync(item);

        Assert.False(result.Succeeded);
        Assert.Contains("does not identify", Assert.Single(result.Receipts).Summary);
        Assert.Equal(0, calls);
    }
}
