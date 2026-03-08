using System;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePublisherRepositoryVersionTests
{
    [Fact]
    public void PSResourceGetClient_Find_PreservesPrereleaseField()
    {
        var stdout = string.Join("::", new[]
        {
            "PFPSRG::ITEM",
            Encode("Mailozaurr"),
            Encode("2.0.1"),
            Encode("PSGallery"),
            Encode("Przemyslaw Klys"),
            Encode("Mailozaurr"),
            Encode("2b0ea9f1-3ff1-4300-b939-106d5da608fa"),
            Encode("Preview1")
        });

        var client = new PSResourceGetClient(
            new StubPowerShellRunner(new PowerShellRunResult(0, stdout, string.Empty, "pwsh.exe")),
            new NullLogger());

        var results = client.Find(
            new PSResourceFindOptions(
                names: new[] { "Mailozaurr" },
                prerelease: true,
                repositories: new[] { "PSGallery" }));

        var item = Assert.Single(results);
        Assert.Equal("2.0.1", item.Version);
        Assert.Equal("Preview1", item.PreRelease);
    }

    [Fact]
    public void GetRepositoryVersionText_AppendsPrereleaseSuffix()
    {
        var resource = new PSResourceInfo(
            name: "Mailozaurr",
            version: "2.0.1",
            repository: "PSGallery",
            author: null,
            description: null,
            guid: null,
            preRelease: "Preview1");

        var versionText = ModulePublisher.GetRepositoryVersionText(resource);

        Assert.Equal("2.0.1-Preview1", versionText);
    }

    [Fact]
    public void GetGitHubTag_Default_IncludesPrereleaseSuffix()
    {
        var publish = new PublishConfiguration
        {
            Destination = PublishDestination.GitHub,
            Enabled = true
        };

        var tag = ModulePublisher.GetGitHubTag(
            publish,
            moduleName: "Mailozaurr",
            resolvedVersion: "2.0.1",
            preRelease: "Preview2");

        Assert.Equal("v2.0.1-Preview2", tag);
    }

    private static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly PowerShellRunResult _result;

        public StubPowerShellRunner(PowerShellRunResult result)
        {
            _result = result;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            return _result;
        }
    }
}
