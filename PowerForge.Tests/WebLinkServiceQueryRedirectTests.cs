using System;
using System.IO;
using PowerForge.Web;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebLinkServiceQueryRedirectTests
{
    [Fact]
    public void Validate_RejectsConflictingOrUnsafeQueryParameterSelectors()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "conflicting-query-selector",
                    SourcePath = "/blog/",
                    SourceQuery = "tag=powershell",
                    SourceQueryParameter = "tag",
                    TargetUrl = "/blog/"
                },
                new LinkRedirectRule
                {
                    Id = "unsafe-query-parameter",
                    SourcePath = "/blog/",
                    SourceQueryParameter = "tag|category",
                    TargetUrl = "/blog/"
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.QUERY_SELECTOR_CONFLICT");
        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.QUERY_PARAMETER");
    }

    [Fact]
    public void ExportApache_EmitsParameterPresenceAndCanonicalHostRedirects()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-query-redirects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outputPath = Path.Combine(root, "links.conf");
            var dataSet = new WebLinkDataSet
            {
                Redirects = new[]
                {
                    new LinkRedirectRule
                    {
                        Id = "legacy-tag-query",
                        SourceHost = "evotec.xyz",
                        SourcePath = "/blog/",
                        SourceQueryParameter = "tag",
                        MatchType = LinkRedirectMatchType.Query,
                        TargetUrl = "https://evotec.xyz/blog/",
                        Status = 301
                    },
                    new LinkRedirectRule
                    {
                        Id = "canonical-www-host",
                        SourceHost = "www.evotec.xyz",
                        SourcePath = "/",
                        MatchType = LinkRedirectMatchType.Prefix,
                        TargetUrl = "https://evotec.xyz/{path}",
                        Status = 301,
                        PreserveQuery = true,
                        AllowExternal = true
                    }
                }
            };

            var validation = WebLinkService.Validate(dataSet);
            Assert.True(validation.Success, string.Join(Environment.NewLine, Array.ConvertAll(validation.Issues, issue => issue.Message)));

            var result = WebLinkService.ExportApache(dataSet, new WebLinkApacheExportOptions
            {
                OutputPath = outputPath
            });

            Assert.Equal(2, result.RuleCount);
            var apache = File.ReadAllText(outputPath);
            Assert.Contains("RewriteCond %{HTTP_HOST} ^(www\\.)?evotec\\.xyz$ [NC]", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{QUERY_STRING} (^|&)tag=[^&]*(&|$)", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^/?blog/?$ https://evotec.xyz/blog/ [R=301,L,QSD]", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{HTTP_HOST} ^www\\.evotec\\.xyz$ [NC]", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^/?(.*)$ https://evotec.xyz/$1 [R=301,L,QSA]", apache, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validate_DetectsQueryParameterSelectorLoopsAcrossRoutes()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "tag-to-category",
                    SourcePath = "/blog/",
                    SourceQueryParameter = "tag",
                    MatchType = LinkRedirectMatchType.Query,
                    TargetUrl = "/archive/?category=legacy"
                },
                new LinkRedirectRule
                {
                    Id = "category-to-tag",
                    SourcePath = "/archive/",
                    SourceQueryParameter = "category",
                    MatchType = LinkRedirectMatchType.Query,
                    TargetUrl = "/blog/?tag=legacy"
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.LOOP");
    }
}
