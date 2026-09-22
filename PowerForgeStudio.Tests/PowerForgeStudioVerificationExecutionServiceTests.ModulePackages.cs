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
