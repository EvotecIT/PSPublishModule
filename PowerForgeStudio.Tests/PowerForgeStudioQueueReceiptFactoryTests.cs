using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueueReceiptFactoryTests
{
    [Fact]
    public void GitHubReceiptCapturesEveryPublishedAssetSize()
    {
        var root = Path.Combine(Path.GetTempPath(), $"powerforge-github-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "app.zip");
            var second = Path.Combine(root, "symbols.zip");
            File.WriteAllBytes(first, new byte[12]);
            File.WriteAllBytes(second, new byte[5]);

            var receipt = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Fixture", "ProjectBuild",
                "GitHub release", "GitHub", "https://github.com/Contoso/Fixture/releases/tag/v1",
                ReleasePublishReceiptStatus.Published, "Published.", first, githubAssetPaths: [first, second]);

            Assert.Equal(12, receipt.GitHubAssets!["app.zip"]);
            Assert.Equal(5, receipt.GitHubAssets["symbols.zip"]);
            File.Delete(second);
            var incomplete = ReleaseQueueReceiptFactory.CreatePublishReceipt(root, "Fixture", "ProjectBuild",
                "GitHub release", "GitHub", "https://github.com/Contoso/Fixture/releases/tag/v1",
                ReleasePublishReceiptStatus.Published, "Published.", first, githubAssetPaths: [first, second]);
            Assert.Null(incomplete.GitHubAssets);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FailedPublishReceipt_UsesTargetNameAsFallbackTargetKind()
    {
        var receipt = ReleaseQueueReceiptFactory.FailedPublishReceipt(
            rootPath: @"C:\Support\GitHub\Testimo",
            repositoryName: "Testimo",
            adapterKind: "ProjectBuild",
            targetName: "NuGet publish",
            destination: "nuget.org",
            summary: "API key missing.");

        Assert.Equal("NuGet publish", receipt.TargetName);
        Assert.Equal("NuGet publish", receipt.TargetKind);
        Assert.Equal(ReleasePublishReceiptStatus.Failed, receipt.Status);
    }

    [Fact]
    public void CreateVerificationReceipt_MapsPublishReceiptIdentity()
    {
        var publishReceipt = new ReleasePublishReceipt(
            RootPath: @"C:\Support\GitHub\DbaClientX",
            RepositoryName: "DbaClientX",
            AdapterKind: "ProjectBuild",
            TargetName: "GitHub release",
            TargetKind: "GitHub",
            Destination: "EvotecIT/DbaClientX",
            SourcePath: @"C:\Support\GitHub\DbaClientX\Artefacts\ProjectBuild\release.zip",
            Status: ReleasePublishReceiptStatus.Published,
            Summary: "Published.",
            PublishedAtUtc: DateTimeOffset.UtcNow);

        var verificationReceipt = ReleaseQueueReceiptFactory.CreateVerificationReceipt(
            publishReceipt,
            ReleaseVerificationReceiptStatus.Verified,
            "Verified.");

        Assert.Equal(publishReceipt.RootPath, verificationReceipt.RootPath);
        Assert.Equal(publishReceipt.TargetKind, verificationReceipt.TargetKind);
        Assert.Equal(ReleaseVerificationReceiptStatus.Verified, verificationReceipt.Status);
    }

    [Fact]
    public void PublicationReceiptsPersistOnlySafeDestinationAndSummary()
    {
        const string address = "https://feed-user:feed-password@packages.example.test/v3/index.json?token=query-secret#fragment-secret";
        var published = ReleaseQueueReceiptFactory.CreatePublishReceipt(
            "root", "Package", "ProjectBuild", "Package.1.0.0.nupkg", "NuGet", address,
            ReleasePublishReceiptStatus.Published, $"Published to {address}.");
        var verified = ReleaseQueueReceiptFactory.CreateVerificationReceipt(published,
            ReleaseVerificationReceiptStatus.Verified, $"Verified at {address}.");

        Assert.Equal("https://packages.example.test/v3/index.json", published.Destination);
        Assert.True(published.DestinationCredentialsOmitted);
        Assert.DoesNotContain("feed-password", System.Text.Json.JsonSerializer.Serialize(published));
        Assert.DoesNotContain("query-secret", System.Text.Json.JsonSerializer.Serialize(published));
        Assert.DoesNotContain("feed-password", System.Text.Json.JsonSerializer.Serialize(verified));
        Assert.DoesNotContain("query-secret", System.Text.Json.JsonSerializer.Serialize(verified));
    }

    [Fact]
    public async Task PublicationProgressOmitsCredentialsFromPlannedAndFailedDetails()
    {
        const string address = "https://feed-user:feed-password@packages.example.test/v3/index.json?token=query-secret";
        var target = new ReleasePublishTarget("root", "Package", "ProjectBuild", "NuGet", "NuGet", "Package.1.0.0.nupkg", address);
        var sink = new CapturingProgressSink();
        var tracker = new ReleasePublicationProgressTracker([target], sink);

        await tracker.InitializeAsync(CancellationToken.None);
        await tracker.CompleteAsync(static _ => true, [], $"Push failed at {address}", CancellationToken.None);

        Assert.Equal(2, sink.Events.Count);
        foreach (var update in sink.Events)
        {
            Assert.DoesNotContain("feed-password", update.Detail);
            Assert.DoesNotContain("query-secret", update.Detail);
            Assert.Contains("https://packages.example.test/v3/index.json", update.Detail);
        }
    }

    private sealed class CapturingProgressSink : IReleaseArtifactProgressSink
    {
        public List<ReleaseArtifactProgress> Events { get; } = [];

        public ValueTask ReportAsync(ReleaseArtifactProgress progress, CancellationToken cancellationToken = default)
        {
            Events.Add(progress);
            return ValueTask.CompletedTask;
        }
    }
}
