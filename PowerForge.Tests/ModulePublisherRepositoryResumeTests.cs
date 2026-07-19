using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePublisherRepositoryVersionTests
{
    [Fact]
    public void ValidateVersionForPublish_InitialForceStillBypassesRepositoryCheck()
    {
        var publisher = new ModulePublisher(
            new NullLogger(),
            new StubPowerShellRunner(_ => throw new InvalidOperationException("Repository query should be bypassed.")));

        var result = publisher.ValidateVersionForPublish(
            CreateGalleryPublish(force: true),
            CreatePlan(resolvedVersion: "3.0.0"));

        Assert.Equal(ModulePublishVersionPreflightResult.Available, result);
    }

    [Fact]
    public void ValidateVersionForPublish_ForcedResumeChecksExactRepositoryVersion()
    {
        using var client = new HttpClient(new FakePowerShellGalleryFeedHandler());
        var publisher = new ModulePublisher(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "3.0.0"), string.Empty, "pwsh.exe")),
            client);
        var publish = CreateGalleryPublish(force: true);

        var result = publisher.ValidateVersionForPublish(
            publish,
            CreatePlan(resolvedVersion: "3.0.0"),
            allowExistingExactVersion: true);

        Assert.Equal(ModulePublishVersionPreflightResult.AlreadyPublished, result);
    }

    [Fact]
    public void ValidateVersionForPublish_AutoResumeFallsBackWhenPsResourceGetIsUnavailable()
    {
        var tools = new List<string>();
        var publisher = new ModulePublisher(
            new NullLogger(),
            new StubPowerShellRunner(request =>
            {
                var script = File.ReadAllText(request.ScriptPath!);
                if (script.Contains("Find-PSResource", StringComparison.Ordinal))
                {
                    tools.Add("PSResourceGet");
                    return new PowerShellRunResult(3, string.Empty, "PSResourceGet is unavailable.", "pwsh.exe");
                }

                if (script.Contains("Find-Module", StringComparison.Ordinal))
                {
                    tools.Add("PowerShellGet");
                    return new PowerShellRunResult(
                        0,
                        VisiblePowerShellGetRepositoryItem("PSPublishModule", "3.0.0", "CompanyGallery"),
                        string.Empty,
                        "powershell.exe");
                }

                throw new InvalidOperationException("Unexpected repository query.");
            }));
        var publish = new PublishConfiguration
        {
            Enabled = true,
            Destination = PublishDestination.PowerShellGallery,
            RepositoryName = "CompanyGallery",
            Tool = PublishTool.Auto,
            Force = true
        };

        var result = publisher.ValidateVersionForPublish(
            publish,
            CreatePlan(resolvedVersion: "3.0.0"),
            allowExistingExactVersion: true);

        Assert.Equal(ModulePublishVersionPreflightResult.AlreadyPublished, result);
        Assert.Equal(new[] { "PSResourceGet", "PowerShellGet" }, tools);
    }

    [Fact]
    public void ValidateVersionForPublish_ResumeFindsExactVersionBehindNewerRelease()
    {
        using var client = new HttpClient(new FakePowerShellGalleryFeedHandler());
        var publisher = new ModulePublisher(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "3.0.0"), string.Empty, "pwsh.exe")),
            client);

        var result = publisher.ValidateVersionForPublish(
            CreateGalleryPublish(force: false),
            CreatePlan(resolvedVersion: "2.0.27"),
            allowExistingExactVersion: true);

        Assert.Equal(ModulePublishVersionPreflightResult.AlreadyPublished, result);
    }

    [Fact]
    public void ValidateVersionForPublish_ResumeRejectsMissingVersionBehindNewerRelease()
    {
        using var client = new HttpClient(new FakePowerShellGalleryFeedHandler());
        var publisher = new ModulePublisher(
            new NullLogger(),
            new StubPowerShellRunner(new PowerShellRunResult(0, VisibleRepositoryItem("PSPublishModule", "3.0.0"), string.Empty, "pwsh.exe")),
            client);

        var exception = Assert.Throws<InvalidOperationException>(() => publisher.ValidateVersionForPublish(
            CreateGalleryPublish(force: false),
            CreatePlan(resolvedVersion: "2.5.0"),
            allowExistingExactVersion: true));

        Assert.Contains("not greater than repository version '3.0.0'", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PublishConfiguration CreateGalleryPublish(bool force)
        => new()
        {
            Enabled = true,
            Destination = PublishDestination.PowerShellGallery,
            RepositoryName = "PSGallery",
            Tool = PublishTool.PSResourceGet,
            Force = force
        };

    private static string VisiblePowerShellGetRepositoryItem(string name, string version, string repository)
        => string.Join("::", new[]
        {
            "PFPWSGET::ITEM",
            Encode(name),
            Encode(version),
            Encode(repository),
            Encode(Guid.NewGuid().ToString("D"))
        });
}
