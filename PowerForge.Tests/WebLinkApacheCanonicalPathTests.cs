using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebLinkApacheCanonicalPathTests
{
    [Theory]
    [InlineData("/allianz/", false)]
    [InlineData("/allianz/", true)]
    [InlineData("https://evotec.pl/allianz/", false)]
    [InlineData("https://evotec.pl/allianz/", true)]
    public void ExportApache_CanonicalGuardKeepsQueryDiscardReachable(string destination, bool preserveQuery)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apache-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "links.conf");
            WebLinkService.ExportApache(new WebLinkDataSet
            {
                Redirects = [new LinkRedirectRule
                {
                    SourceHost = "evotec.pl", SourcePath = "/allianz/",
                    TargetUrl = destination, PreserveQuery = preserveQuery
                }]
            }, new WebLinkApacheExportOptions { OutputPath = path });
            var apache = File.ReadAllText(path);
            Assert.Equal(!preserveQuery, apache.Contains("RewriteCond %{QUERY_STRING} !^$"));
            if (!preserveQuery)
            {
                Assert.Contains("RewriteCond %{REQUEST_URI} !^/allianz/$ [OR]", apache);
                Assert.Contains("[R=301,L,QSD]", apache);
                if (destination.StartsWith("https://", StringComparison.Ordinal))
                    Assert.Contains("RewriteCond %{HTTPS} !^on$ [OR]", apache);
            }
            else
                Assert.Contains("[R=301,L,QSA]", apache);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("/pl/allianz/", true)]
    [InlineData("https://evotec.pl/pl/allianz/", true)]
    [InlineData("/allianz/", true)]
    [InlineData("https://elsewhere.example/allianz/", true)]
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
            Assert.Contains("RewriteRule ^/?café/?$", File.ReadAllText(path));
            Assert.Contains("[R=301,L,QSD,NE]", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("evotec.pl")]
    [InlineData(null)]
    [InlineData("*")]
    public void ExportApache_GuardsAbsoluteCanonicalHostWithinSourceMatch(string? host)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apache-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "links.conf");
            WebLinkService.ExportApache(new WebLinkDataSet
            {
                Redirects = [new LinkRedirectRule { SourceHost = host, SourcePath = "/foo/", TargetUrl = "https://www.evotec.pl/foo/", AllowExternal = true }]
            }, new WebLinkApacheExportOptions { OutputPath = path });
            Assert.Contains(@"RewriteCond %{HTTP_HOST} !^www\.evotec\.pl(?::443)?$ [NC,OR]", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("/space%20report/", @"space\x20report")]
    [InlineData("/double%22quote/", @"double\x22quote")]
    [InlineData("/single%27quote/", @"single\x27quote")]
    public void ExportApache_EncodesDecodedConfigurationDelimiters(string source, string expectedPattern)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apache-delimiter-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "links.conf");
            WebLinkService.ExportApache(new WebLinkDataSet
            {
                Redirects = [new LinkRedirectRule { SourceHost = "evotec.pl", SourcePath = source, TargetUrl = "https://evotec.pl" + source }]
            }, new WebLinkApacheExportOptions { OutputPath = path });
            Assert.Contains($"RewriteRule ^/?{expectedPattern}/?$", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(LinkRedirectMatchType.Exact, "/space%20report/", @"/space\%20report/", true)]
    [InlineData(LinkRedirectMatchType.Query, "/space%20report/", @"/space\%20report/", true)]
    [InlineData(LinkRedirectMatchType.Exact, "/price$1/", @"/price\$1/", false)]
    [InlineData(LinkRedirectMatchType.Exact, "/page/#section", "/page/#section", true)]
    public void ExportApache_PreservesLiteralDestinationCharacters(LinkRedirectMatchType matchType, string destination, string expected, bool noEscape)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apache-literal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "links.conf");
            WebLinkService.ExportApache(new WebLinkDataSet
            {
                Redirects = [new LinkRedirectRule { SourceHost = "evotec.pl", SourcePath = "/old/", TargetUrl = destination, MatchType = matchType }]
            }, new WebLinkApacheExportOptions { OutputPath = path });
            Assert.Contains($"RewriteRule ^/?old/?$ {expected} [R=301,L,QSD{(noEscape ? ",NE" : string.Empty)}]", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExportApache_UsesGuardedOrdinaryRuleForCanonicalIndexedShortlink()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apache-map-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "links.conf");
            var result = WebLinkService.ExportApache(new WebLinkDataSet
            {
                Shortlinks = [new LinkShortlinkRule { Host = "evotec.pl", PathPrefix = "/", Slug = "foo", TargetUrl = "/pl/foo/" }]
            }, new WebLinkApacheExportOptions
            {
                OutputPath = path, ShortlinkMapOutputPath = Path.Combine(root, "links.map"),
                ShortlinkMapRuntimePath = "/tmp/pf-test-links.map",
                LanguageRootHosts = new Dictionary<string, string> { ["evotec.pl"] = "pl" }
            });
            Assert.Equal(0, result.ShortlinkMapEntryCount);
            Assert.Contains("RewriteCond %{REQUEST_URI} !^/foo/$ [OR]", File.ReadAllText(path));
            Assert.Contains("RewriteRule ^/?foo/?$ /foo/ [R=302,L,QSD]", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
