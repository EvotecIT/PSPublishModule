using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueueReceiptFactoryTests
{
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
}
