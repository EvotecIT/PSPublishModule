using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebLinkApacheCanonicalPathTests
{
    [Theory]
    [InlineData("/pl/allianz/", true)]
    [InlineData("https://evotec.pl/pl/allianz/", true)]
    [InlineData("/allianz/", true)]
    [InlineData("https://elsewhere.example/allianz/", false)]
    [InlineData("/other/", false)]
    [InlineData("/allianz/?view=full", false)]
    public void ExportApache_ExcludesCanonicalDestinationAfterLanguageRootNormalization(string destination, bool guarded)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apache-canonical-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "links.conf");
            WebLinkService.ExportApache(new WebLinkDataSet
            {
                Redirects = [new LinkRedirectRule { SourceHost = "evotec.pl", SourcePath = "/allianz/", TargetUrl = destination }]
            }, new WebLinkApacheExportOptions
            {
                OutputPath = path,
                LanguageRootHosts = new Dictionary<string, string> { ["evotec.pl"] = "pl" }
            });
            var apache = File.ReadAllText(path);
            var guard = Regex.Match(apache, @"RewriteCond %\{REQUEST_URI\} !(\^/allianz/\$)(?: \[OR\])?\r?$", RegexOptions.Multiline);
            Assert.Equal(guarded, guard.Success);
            if (guarded)
            {
                Assert.Matches(guard.Groups[1].Value.Trim(), "/allianz/");
                Assert.DoesNotMatch(guard.Groups[1].Value.Trim(), "/allianz");
                Assert.Contains("RewriteRule ^/?allianz/?$", apache);
                if (destination.StartsWith("https://evotec.pl", StringComparison.Ordinal))
                {
                    Assert.Contains("RewriteCond %{REQUEST_URI} !^/allianz/$ [OR]", apache);
                    Assert.Contains(@"RewriteCond %{HTTP_HOST} !^evotec\.pl(?::443)?$ [NC,OR]", apache);
                    Assert.Contains("RewriteCond %{HTTPS} !^on$", apache);
                }
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("/café/", "https://evotec.pl/caf%C3%A9/")]
    [InlineData("/caf%C3%A9/", "https://evotec.pl/caf%C3%A9/")]
    public void ExportApache_GuardsEquivalentUnicodeAndEncodedTargetPaths(string source, string destination)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apache-encoded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "links.conf");
            WebLinkService.ExportApache(new WebLinkDataSet
            {
                Redirects = [new LinkRedirectRule { SourceHost = "evotec.pl", SourcePath = source, TargetUrl = destination }]
            }, new WebLinkApacheExportOptions { OutputPath = path });
            Assert.Contains("RewriteCond %{REQUEST_URI} !^(?:/caf%C3%A9/|/café/)$ [OR]", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
