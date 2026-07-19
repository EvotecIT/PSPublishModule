using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePublisherManagedModuleTests
{
    [Fact]
    public void ValidateVersionForPublish_ManagedForcedResumeFindsExactVersionBehindNewerRelease()
    {
        using var feed = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "PSPublishModule.3.0.13.nupkg"),
            "PSPublishModule",
            "3.0.13");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "PSPublishModule.3.0.14.nupkg"),
            "PSPublishModule",
            "3.0.14");
        var publisher = new ModulePublisher(
            new NullLogger(),
            new StubPowerShellRunner(_ => throw new InvalidOperationException("PowerShell runner should not be used by managed preflight.")));
        var publish = new PublishConfiguration
        {
            Destination = PublishDestination.PowerShellGallery,
            Enabled = true,
            Force = true,
            Tool = PublishTool.ManagedModule,
            RepositoryName = "Local",
            Repository = new PublishRepositoryConfiguration
            {
                Name = "Local",
                Uri = feed.Path
            }
        };

        var result = publisher.ValidateVersionForPublish(
            publish,
            CreatePlan(),
            allowExistingExactVersion: true);

        Assert.Equal(ModulePublishVersionPreflightResult.AlreadyPublished, result);
    }
}
