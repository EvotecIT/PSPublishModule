using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebEcosystemStatsGeneratorTests
{
    [Theory]
    [InlineData("https://github.com/ExampleOrg/PrivateRepo")]
    [InlineData("https://www.github.com/ExampleOrg/PrivateRepo/tree/main")]
    [InlineData("https://api.github.com/repos/ExampleOrg/PrivateRepo/releases")]
    [InlineData("https://raw.githubusercontent.com/ExampleOrg/PrivateRepo/main/README.md")]
    public void NormalizePublicProjectLink_UsesRepositoryIdentityAcrossGitHubEndpoints(string url)
    {
        var inventory = new WebEcosystemGitHubStats
        {
            Organization = "ExampleOrg", Repositories = new() { new() { FullName = "ExampleOrg/PublicRepo" } }
        };
        Assert.Null(WebEcosystemStatsGenerator.NormalizePublicProjectLink(url, inventory));
        var publicUrl = url.Replace("PrivateRepo", "PublicRepo", StringComparison.Ordinal);
        Assert.Equal(publicUrl, WebEcosystemStatsGenerator.NormalizePublicProjectLink(publicUrl, inventory));
    }

    [Fact]
    public void NormalizePublicProjectLinks_PreservesPublicAndExternalLinksAcrossPackageFeeds()
    {
        var document = new WebEcosystemStatsDocument
        {
            GitHub = new() { Organization = "ExampleOrg", Repositories = new() { new() { FullName = "ExampleOrg/PublicRepo" } } },
            NuGet = new() { Items = new()
            {
                new() { Id = "Public", ProjectUrl = "https://github.com/exampleorg/PublicRepo.git/tree/main" },
                new() { Id = "Private", ProjectUrl = "https://github.com/ExampleOrg/PrivateRepo" },
                new() { Id = "External", ProjectUrl = "https://docs.example.org/start" },
                new() { Id = "OtherOrg", ProjectUrl = "https://github.com/OtherOrg/Library" }
            } },
            PowerShellGallery = new() { Modules = new()
            {
                new() { Id = "PrivateModule", ProjectUrl = "https://github.com/ExampleOrg/%50rivateRepo/releases" },
                new() { Id = "PublicModule", ProjectUrl = "https://github.com/ExampleOrg/PublicRepo" }
            } }
        };

        Assert.Equal(2, WebEcosystemStatsGenerator.NormalizePublicProjectLinks(document));
        Assert.Null(document.NuGet.Items[1].ProjectUrl);
        Assert.Null(document.PowerShellGallery.Modules[0].ProjectUrl);
        Assert.Equal("https://github.com/exampleorg/PublicRepo.git/tree/main", document.NuGet.Items[0].ProjectUrl);
        Assert.Equal("https://docs.example.org/start", document.NuGet.Items[2].ProjectUrl);
        Assert.Equal("https://github.com/OtherOrg/Library", document.NuGet.Items[3].ProjectUrl);
        Assert.Equal("https://github.com/ExampleOrg/PublicRepo", document.PowerShellGallery.Modules[1].ProjectUrl);
        Assert.Equal(0, WebEcosystemStatsGenerator.NormalizePublicProjectLinks(document));
    }

    [Fact]
    public void NormalizePublicProjectLinks_EmptyInventoryWithholdsOrganizationLinks()
    {
        var document = new WebEcosystemStatsDocument
        {
            GitHub = new() { Organization = "ExampleOrg" },
            NuGet = new() { Items = new() { new() { ProjectUrl = "https://github.com/ExampleOrg/Unknown" } } }
        };
        Assert.Equal(1, WebEcosystemStatsGenerator.NormalizePublicProjectLinks(document));
        Assert.Null(document.NuGet.Items[0].ProjectUrl);

        document.GitHub = null;
        document.NuGet.Items[0].ProjectUrl = "https://github.com/ExampleOrg/Unknown";
        Assert.Equal(0, WebEcosystemStatsGenerator.NormalizePublicProjectLinks(document));
        Assert.NotNull(document.NuGet.Items[0].ProjectUrl);
    }

    [Fact]
    public void Generate_ExcludesUnknownOrganizationRepositoryFromPublishedPackageMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-public-project-links-" + Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(root, "stats.json");
            using var handler = new ProjectLinksHandler();
            WebEcosystemStatsGenerator.Generate(new()
            {
                OutputPath = output, GitHubOrganization = "ExampleOrg", NuGetOwner = "ExampleOwner"
            }, handler);
            using var document = JsonDocument.Parse(File.ReadAllText(output));
            var packages = document.RootElement.GetProperty("nuget").GetProperty("packages");
            var package = packages.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "ExamplePackage");
            Assert.False(package.TryGetProperty("projectUrl", out _));
            Assert.Equal(123, package.GetProperty("totalDownloads").GetInt64());
            Assert.Equal(package.GetProperty("projectRepositoryKey").GetString(),
                packages.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "ExamplePackage.Core").GetProperty("projectRepositoryKey").GetString());
            Assert.DoesNotContain("ExampleOrg/PrivateRepo", File.ReadAllText(output));
        }
        finally { TryDeleteDirectory(root); }
    }

    [Fact]
    public void Generate_FailedGitHubUsesApplicableRetainedInventoryBeforePublishingFreshPackages()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-retained-project-links-" + Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(root, "stats.json");
            using var handler = new ProjectLinksHandler(failGitHub: true);
            WebEcosystemStatsGenerator.Generate(new()
            {
                OutputPath = output, GitHubOrganization = "ExampleOrg", NuGetOwner = "ExampleOwner",
                FallbackGitHubInventory = new() { Organization = "ExampleOrg", Repositories = new() { new() { FullName = "ExampleOrg/PublicRepo" } } }
            }, handler);
            using var document = JsonDocument.Parse(File.ReadAllText(output));
            var packages = document.RootElement.GetProperty("nuget").GetProperty("packages");
            Assert.Equal("https://github.com/ExampleOrg/PublicRepo", packages.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "PublicPackage").GetProperty("projectUrl").GetString());
            Assert.False(packages.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "ExamplePackage").TryGetProperty("projectUrl", out _));
            Assert.Contains(document.RootElement.GetProperty("warnings").EnumerateArray(), item => item.GetString()!.StartsWith("GitHub", StringComparison.Ordinal));
        }
        finally { TryDeleteDirectory(root); }
    }

    private sealed class ProjectLinksHandler : HttpMessageHandler
    {
        private readonly bool _failGitHub;
        public ProjectLinksHandler(bool failGitHub = false) => _failGitHub = failGitHub;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_failGitHub && request.RequestUri!.AbsolutePath.Contains("/orgs/"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var json = request.RequestUri!.AbsolutePath.Contains("/orgs/")
                    ? """[{"name":"PublicRepo","full_name":"ExampleOrg/PublicRepo","html_url":"https://github.com/ExampleOrg/PublicRepo"}]"""
                : request.RequestUri.AbsolutePath.Contains("/search/issues") ? """{"total_count":0,"items":[]}"""
                : """{"totalHits":3,"data":[{"id":"ExamplePackage","version":"1.0.0","totalDownloads":123,"projectUrl":"https://github.com/ExampleOrg/PrivateRepo"},{"id":"ExamplePackage.Core","version":"1.0.0","totalDownloads":200,"projectUrl":"https://github.com/ExampleOrg/PrivateRepo"},{"id":"PublicPackage","version":"1.0.0","totalDownloads":5,"projectUrl":"https://github.com/ExampleOrg/PublicRepo"}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
