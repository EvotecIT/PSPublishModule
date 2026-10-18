using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebLinkApacheMapIdentityTests
{
    [Theory]
    [InlineData("/a%2Fb/", "/a/b/", "example.test", 302)]
    [InlineData("/a%2Fb/", "/a/b/", "example.test", 301)]
    [InlineData("/a%2Fb/", "/a/b/", "www.example.test", 308)]
    [InlineData("/%64ocs/", "/docs/", "example.test", 302)]
    public void ExportApache_MapsOnlyTheFirstMatchingDecodedIdentity(string encodedPrefix, string decodedPrefix, string laterHost, int laterStatus)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apache-map-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configuration = Path.Combine(root, "rules.conf");
            var map = Path.Combine(root, "links.map");
            var result = WebLinkService.ExportApache(new WebLinkDataSet
            {
                Shortlinks = [
                    new LinkShortlinkRule { Host = "example.test", PathPrefix = encodedPrefix, Slug = "go", TargetUrl = "/first/", Status = 302 },
                    new LinkShortlinkRule { Host = laterHost, PathPrefix = decodedPrefix, Slug = "go", TargetUrl = "/second/", Status = laterStatus }
                ]
            }, new WebLinkApacheExportOptions
            {
                OutputPath = configuration, ShortlinkMapOutputPath = map, ShortlinkMapRuntimePath = "/tmp/pf-identity.map"
            });

            Assert.Equal(2, result.RuleCount);
            Assert.Equal(1, result.ShortlinkMapEntryCount);
            Assert.Equal($"302:example.test:{decodedPrefix.Trim('/')}/go /first/", Assert.Single(File.ReadAllLines(map)));
            var rules = File.ReadAllText(configuration);
            Assert.Contains($"/second/ [R={laterStatus},L,QSD]", rules);
            Assert.True(rules.IndexOf("RewriteMap", StringComparison.Ordinal) < rules.IndexOf("/second/", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
