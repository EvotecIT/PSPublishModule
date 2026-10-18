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
            var guard = Regex.Match(apache, @"RewriteCond %\{REQUEST_URI\} !(\^/allianz/\$)\r?$", RegexOptions.Multiline);
            Assert.Equal(guarded, guard.Success);
            if (guarded)
            {
                Assert.Matches(guard.Groups[1].Value.Trim(), "/allianz/");
                Assert.DoesNotMatch(guard.Groups[1].Value.Trim(), "/allianz");
                Assert.Contains("RewriteRule ^/?allianz/?$", apache);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
