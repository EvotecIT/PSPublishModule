using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebLinkServiceTests
{
    [Fact]
    public void Validate_DetectsDuplicateRedirectsAndExternalTargets()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "first",
                    SourcePath = "/old/",
                    TargetUrl = "/new/",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "second",
                    SourcePath = "/old",
                    TargetUrl = "https://example.com/new",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE");
        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.TARGET_EXTERNAL");
    }

    [Fact]
    public void Validate_DetectsShortHostDuplicateRoutes()
    {
        var dataSet = new WebLinkDataSet
        {
            Hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["short"] = "evo.yt"
            },
            Shortlinks = new[]
            {
                new LinkShortlinkRule
                {
                    Slug = "docs",
                    Host = "evo.yt",
                    TargetUrl = "https://docs.example.test",
                    Owner = "evotec",
                    AllowExternal = true
                },
                new LinkShortlinkRule
                {
                    Slug = "docs",
                    Host = "evo.yt",
                    PathPrefix = "/",
                    TargetUrl = "https://docs-alt.example.test",
                    Owner = "evotec",
                    AllowExternal = true
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.SHORTLINK.DUPLICATE");
    }

    [Fact]
    public void ValidateShortlinks_TreatsWildcardAndEmptyHostsAsSameScope()
    {
        var dataSet = new WebLinkDataSet
        {
            Shortlinks = new[]
            {
                new LinkShortlinkRule
                {
                    Slug = "docs",
                    TargetUrl = "/docs/",
                    Owner = "evotec"
                },
                new LinkShortlinkRule
                {
                    Host = "*",
                    Slug = "docs",
                    TargetUrl = "/docs-v2/",
                    Owner = "evotec"
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.SHORTLINK.DUPLICATE");
    }

    [Fact]
    public void ValidateRedirectGraph_KeepsHostScopedChainsSeparate()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "en-old",
                    SourceHost = "evotec.xyz",
                    SourcePath = "/old",
                    TargetUrl = "/new",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "pl-new",
                    SourceHost = "evotec.pl",
                    SourcePath = "/new",
                    TargetUrl = "/old",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.DoesNotContain(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.LOOP");
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.CHAIN");
    }

    [Fact]
    public void ValidateRedirects_TreatsWildcardAndEmptyHostsAsSameScope()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "global-empty",
                    SourcePath = "/old",
                    TargetUrl = "/new",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "global-wildcard",
                    SourceHost = "*",
                    SourcePath = "/old",
                    TargetUrl = "/other",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE");
    }

    [Fact]
    public void ValidateRedirects_TreatsExactSourceQueryAsQueryRuleForDuplicates()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "exact-query",
                    SourcePath = "/",
                    SourceQuery = "p=123",
                    MatchType = LinkRedirectMatchType.Exact,
                    TargetUrl = "/post-a",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "query",
                    SourcePath = "/",
                    SourceQuery = "p=123",
                    MatchType = LinkRedirectMatchType.Query,
                    TargetUrl = "/post-b",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE");
    }

    [Fact]
    public void ValidateRedirects_PreservesTargetQueryWhenComparingDuplicates()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "promo-a",
                    SourcePath = "/promo",
                    TargetUrl = "/landing?src=a",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "promo-b",
                    SourcePath = "/promo",
                    TargetUrl = "/landing?src=b",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE");
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE_SAME_TARGET");
    }

    [Fact]
    public void ValidateRedirects_PreservesSourceQueryCaseInKeys()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "upper",
                    SourcePath = "/promo",
                    SourceQuery = "Token=A",
                    MatchType = LinkRedirectMatchType.Query,
                    TargetUrl = "/landing-a",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "lower",
                    SourcePath = "/promo",
                    SourceQuery = "Token=a",
                    MatchType = LinkRedirectMatchType.Query,
                    TargetUrl = "/landing-b",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.DoesNotContain(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE");
    }

    [Fact]
    public void ValidateRedirects_KeepsRegexSourcePatternsIntactInKeys()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "regex-query-char",
                    SourcePath = "^foo?bar$",
                    MatchType = LinkRedirectMatchType.Regex,
                    TargetUrl = "/first",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "regex-fragment-char",
                    SourcePath = "^foo#bar$",
                    MatchType = LinkRedirectMatchType.Regex,
                    TargetUrl = "/second",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.DoesNotContain(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE");
    }

    [Fact]
    public void ValidateRedirects_RejectsProtocolRelativeTargets()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "protocol-relative",
                    SourcePath = "/old",
                    TargetUrl = "//attacker.example/path",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.TARGET_INVALID");
    }

    [Fact]
    public void ValidateRedirects_TreatsSameHostAbsoluteTargetAsInternal()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "same-host",
                    SourceHost = "example.com",
                    SourcePath = "/old",
                    TargetUrl = "https://example.com/new",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.DoesNotContain(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.TARGET_EXTERNAL");
    }

    [Fact]
    public void ValidateRedirectGraph_DoesNotTreatQueryOrSlashCanonicalRulesAsLoops()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "post-id",
                    SourcePath = "/",
                    SourceQuery = "p=123",
                    MatchType = LinkRedirectMatchType.Query,
                    TargetUrl = "/blog/current/",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "root-slash",
                    SourcePath = "/blog/current",
                    TargetUrl = "/blog/current/",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.DoesNotContain(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.LOOP");
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.CHAIN");
    }

    [Fact]
    public void ValidateRedirectGraph_DetectsLoopsThroughTargetQuery()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "a",
                    SourcePath = "/a",
                    TargetUrl = "/b?x=1",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "b-query",
                    SourcePath = "/b",
                    SourceQuery = "x=1",
                    MatchType = LinkRedirectMatchType.Query,
                    TargetUrl = "/a",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.LOOP");
    }

    [Fact]
    public void ValidateRedirectGraph_DetectsLoopsThroughQuerylessExactNextHop()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "a",
                    SourcePath = "/a",
                    TargetUrl = "/b?x=1",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "b",
                    SourcePath = "/b",
                    TargetUrl = "/a",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.LOOP");
    }

    [Fact]
    public void ValidateRedirectGraph_DetectsDirectSelfRedirects()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "self",
                    SourcePath = "/a",
                    TargetUrl = "/a",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.LOOP");
    }

    [Fact]
    public void ValidateRedirectGraph_DetectsLoopsThroughSameHostAbsoluteTargets()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "a",
                    SourceHost = "example.com",
                    SourcePath = "/a",
                    TargetUrl = "https://example.com/b",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "b",
                    SourceHost = "example.com",
                    SourcePath = "/b",
                    TargetUrl = "/a",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.LOOP");
    }

    [Fact]
    public void ValidateRedirectGraph_DetectsWildcardLoopsThroughHostScopedEdges()
    {
        var dataSet = new WebLinkDataSet
        {
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "wildcard-a",
                    SourcePath = "/a",
                    TargetUrl = "/b",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "host-b",
                    SourceHost = "example.com",
                    SourcePath = "/b",
                    TargetUrl = "/a",
                    Status = 301
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.LOOP");
    }

    [Fact]
    public void ExportApache_EmitsHostScopedRedirectsAndShortlinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outPath = Path.Combine(root, "links.conf");
            var dataSet = new WebLinkDataSet
            {
                Redirects = new[]
                {
                    new LinkRedirectRule
                    {
                        Id = "legacy",
                        SourceHost = "evotec.pl",
                        SourcePath = "/stary/",
                        TargetUrl = "/nowy/",
                        Status = 301
                    },
                    new LinkRedirectRule
                    {
                        Id = "old-post-id",
                        SourcePath = "/",
                        SourceQuery = "p=123",
                        MatchType = LinkRedirectMatchType.Query,
                        TargetUrl = "/blog/current/",
                        Status = 301
                    }
                },
                Shortlinks = new[]
                {
                    new LinkShortlinkRule
                    {
                        Slug = "discord",
                        Host = "evo.yt",
                        TargetUrl = "https://discord.gg/example",
                        Status = 302,
                        Owner = "evotec",
                        AllowExternal = true
                    }
                }
            };

            var result = WebLinkService.ExportApache(dataSet, new WebLinkApacheExportOptions
            {
                OutputPath = outPath,
                IncludeErrorDocument404 = true,
                Hosts = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["short"] = "evo.yt"
                }
            });

            Assert.Equal(3, result.RuleCount);
            var apache = File.ReadAllText(outPath);
            Assert.Contains("ErrorDocument 404 /404.html", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{HTTP_HOST} ^(.+\\.)?evotec\\.pl$ [NC]", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^stary/?$ /nowy/ [R=301,L,QSD]", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{QUERY_STRING} (^|&)p=123(&|$)", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{HTTP_HOST} ^(.+\\.)?evo\\.yt$ [NC]", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^discord/?$ https://discord.gg/example [R=302,L,QSD]", apache, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ExportApache_EscapesExactSourceRegexCharacters()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-export-escape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outPath = Path.Combine(root, "links.conf");
            var dataSet = new WebLinkDataSet
            {
                Redirects = new[]
                {
                    new LinkRedirectRule
                    {
                        Id = "dotted",
                        SourcePath = "/foo.bar/",
                        TargetUrl = "/new/",
                        Status = 301
                    }
                }
            };

            WebLinkService.ExportApache(dataSet, new WebLinkApacheExportOptions
            {
                OutputPath = outPath
            });

            var apache = File.ReadAllText(outPath);
            Assert.Contains(@"RewriteRule ^foo\.bar/?$ /new/ [R=301,L,QSD]", apache, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ExportApache_EmitsGoneRulesForPrefixAndRegexWithoutTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-export-gone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outPath = Path.Combine(root, "links.conf");
            var dataSet = new WebLinkDataSet
            {
                Redirects = new[]
                {
                    new LinkRedirectRule
                    {
                        Id = "gone-prefix",
                        SourcePath = "/gone/*",
                        MatchType = LinkRedirectMatchType.Prefix,
                        Status = 410
                    },
                    new LinkRedirectRule
                    {
                        Id = "gone-regex",
                        SourcePath = "/legacy/.*",
                        MatchType = LinkRedirectMatchType.Regex,
                        Status = 410
                    }
                }
            };

            WebLinkService.ExportApache(dataSet, new WebLinkApacheExportOptions
            {
                OutputPath = outPath
            });

            var apache = File.ReadAllText(outPath);
            Assert.Contains("RewriteRule ^gone", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^legacy/.* - [G,L]", apache, StringComparison.Ordinal);
            Assert.Equal(2, CountOccurrences(apache, "[G,L]"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ExportApache_StripsLeadingSlashAfterRegexAnchor()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-export-regex-anchor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outPath = Path.Combine(root, "links.conf");
            var dataSet = new WebLinkDataSet
            {
                Redirects = new[]
                {
                    new LinkRedirectRule
                    {
                        Id = "anchored-regex",
                        SourcePath = "^/legacy/(.*)$",
                        MatchType = LinkRedirectMatchType.Regex,
                        TargetUrl = "/archive/{path}",
                        Status = 301
                    }
                }
            };

            WebLinkService.ExportApache(dataSet, new WebLinkApacheExportOptions
            {
                OutputPath = outPath
            });

            var apache = File.ReadAllText(outPath);
            Assert.Contains("RewriteRule ^legacy/(.*)$ /archive/$1 [R=301,L,QSD]", apache, StringComparison.Ordinal);
            Assert.DoesNotContain("RewriteRule ^/legacy", apache, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ExportApache_InsertsShortlinkUtmBeforeFragment()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-export-utm-fragment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outPath = Path.Combine(root, "links.conf");
            var dataSet = new WebLinkDataSet
            {
                Shortlinks = new[]
                {
                    new LinkShortlinkRule
                    {
                        Slug = "promo",
                        TargetUrl = "https://example.test/landing#cta",
                        Utm = "utm_source=short",
                        Status = 302,
                        Owner = "evotec",
                        AllowExternal = true
                    }
                }
            };

            WebLinkService.ExportApache(dataSet, new WebLinkApacheExportOptions
            {
                OutputPath = outPath
            });

            var apache = File.ReadAllText(outPath);
            Assert.Contains("https://example.test/landing?utm_source=short#cta", apache, StringComparison.Ordinal);
            Assert.DoesNotContain("https://example.test/landing#cta?utm_source=short", apache, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ImportPrettyLinks_MergesExistingShortlinksAndPreservesImportedHits()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var csvPath = Path.Combine(root, "pretty-links.csv");
            var outPath = Path.Combine(root, "shortlinks.json");
            File.WriteAllText(csvPath,
                """
                id,name,slug,url,clicks
                7,Discord,discord,https://discord.gg/example,42
                8,Docs,/go/docs,https://docs.example.test,12
                9,Google,google,https://google.example.test,3
                """);
            File.WriteAllText(outPath,
                """
                {
                  "shortlinks": [
                    {
                      "slug": "manual",
                      "targetUrl": "https://example.test",
                      "owner": "evotec",
                      "allowExternal": true
                    }
                  ]
                }
                """);

            var result = WebLinkService.ImportPrettyLinks(new WebLinkShortlinkImportOptions
            {
                SourcePath = csvPath,
                OutputPath = outPath,
                Host = "evo.yt",
                PathPrefix = "/go",
                Owner = "evotec",
                Tags = new[] { "imported" }
            });

            Assert.Equal(1, result.ExistingCount);
            Assert.Equal(3, result.ImportedCount);
            Assert.Equal(4, result.WrittenCount);

            var loaded = WebLinkService.Load(new WebLinkLoadOptions
            {
                ShortlinksPath = outPath
            });
            var discord = Assert.Single(loaded.Shortlinks, item => item.Slug == "discord");
            Assert.Equal("evo.yt", discord.Host);
            Assert.Equal("/go", discord.PathPrefix);
            Assert.Equal(42, discord.ImportedHits);
            Assert.Equal("imported-pretty-links", discord.Source);
            Assert.Contains("imported", discord.Tags);
            Assert.Contains(loaded.Shortlinks, item => item.Slug == "google" && item.PathPrefix == "/go");
            Assert.DoesNotContain(loaded.Shortlinks, item => item.Slug == "ogle");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ImportPrettyLinks_NormalizesPrefixWhenMergingExistingShortlinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-import-prefix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var csvPath = Path.Combine(root, "pretty-links.csv");
            var outPath = Path.Combine(root, "shortlinks.json");
            File.WriteAllText(csvPath,
                """
                id,name,slug,url,clicks
                8,Docs,/go/docs,https://docs-new.example.test,12
                """);
            File.WriteAllText(outPath,
                """
                {
                  "shortlinks": [
                    {
                      "host": "evo.yt",
                      "pathPrefix": "/go/",
                      "slug": "docs",
                      "targetUrl": "https://docs.example.test",
                      "owner": "evotec",
                      "allowExternal": true
                    }
                  ]
                }
                """);

            var result = WebLinkService.ImportPrettyLinks(new WebLinkShortlinkImportOptions
            {
                SourcePath = csvPath,
                OutputPath = outPath,
                Host = "evo.yt",
                PathPrefix = "go",
                Owner = "evotec"
            });

            Assert.Equal(1, result.ExistingCount);
            Assert.Equal(1, result.ImportedCount);
            Assert.Equal(1, result.WrittenCount);
            Assert.Equal(1, result.SkippedDuplicateCount);

            var loaded = WebLinkService.Load(new WebLinkLoadOptions
            {
                ShortlinksPath = outPath
            });
            var docs = Assert.Single(loaded.Shortlinks, item => item.Slug == "docs");
            Assert.Equal("https://docs.example.test", docs.TargetUrl);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ImportPrettyLinks_TreatsWildcardAndHostlessShortlinkMergeKeysAsSameScope()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-import-wildcard-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var csvPath = Path.Combine(root, "pretty-links.csv");
            var outPath = Path.Combine(root, "shortlinks.json");
            File.WriteAllText(csvPath,
                """
                id,name,slug,url,clicks
                8,Docs,/go/docs,https://docs-new.example.test,12
                """);
            File.WriteAllText(outPath,
                """
                {
                  "shortlinks": [
                    {
                      "host": "*",
                      "pathPrefix": "/go",
                      "slug": "docs",
                      "targetUrl": "https://docs.example.test",
                      "owner": "evotec",
                      "allowExternal": true
                    }
                  ]
                }
                """);

            var result = WebLinkService.ImportPrettyLinks(new WebLinkShortlinkImportOptions
            {
                SourcePath = csvPath,
                OutputPath = outPath,
                PathPrefix = "/go",
                Owner = "evotec"
            });

            Assert.Equal(1, result.ExistingCount);
            Assert.Equal(1, result.ImportedCount);
            Assert.Equal(1, result.WrittenCount);
            Assert.Equal(1, result.SkippedDuplicateCount);

            var loaded = WebLinkService.Load(new WebLinkLoadOptions
            {
                ShortlinksPath = outPath
            });
            var docs = Assert.Single(loaded.Shortlinks, item => item.Slug == "docs");
            Assert.Equal("*", docs.Host);
            Assert.Equal("https://docs.example.test", docs.TargetUrl);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ImportPrettyLinks_TreatsImplicitPrefixAsGoForNonShortHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-import-implicit-prefix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var csvPath = Path.Combine(root, "pretty-links.csv");
            var outPath = Path.Combine(root, "shortlinks.json");
            File.WriteAllText(csvPath,
                """
                id,name,slug,url,clicks
                8,Docs,/go/docs,https://docs-new.example.test,12
                """);
            File.WriteAllText(outPath,
                """
                {
                  "shortlinks": [
                    {
                      "host": "evotec.xyz",
                      "slug": "docs",
                      "targetUrl": "https://docs.example.test",
                      "owner": "evotec",
                      "allowExternal": true
                    }
                  ]
                }
                """);

            var result = WebLinkService.ImportPrettyLinks(new WebLinkShortlinkImportOptions
            {
                SourcePath = csvPath,
                OutputPath = outPath,
                Host = "evotec.xyz",
                ShortHost = "evo.yt",
                Owner = "evotec"
            });

            Assert.Equal(1, result.ExistingCount);
            Assert.Equal(1, result.ImportedCount);
            Assert.Equal(1, result.WrittenCount);
            Assert.Equal(1, result.SkippedDuplicateCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ImportPrettyLinks_SlugifiesToValidatorSafeAscii()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-import-ascii-slug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var csvPath = Path.Combine(root, "pretty-links.csv");
            var outPath = Path.Combine(root, "shortlinks.json");
            File.WriteAllText(csvPath,
                """
                id,name,slug,url,clicks
                9,Cafe,café-2026,https://cafe.example.test,5
                """);

            WebLinkService.ImportPrettyLinks(new WebLinkShortlinkImportOptions
            {
                SourcePath = csvPath,
                OutputPath = outPath,
                Host = "evo.yt",
                ShortHost = "evo.yt",
                Owner = "evotec"
            });

            var loaded = WebLinkService.Load(new WebLinkLoadOptions
            {
                ShortlinksPath = outPath,
                Hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["short"] = "evo.yt"
                }
            });

            var shortlink = Assert.Single(loaded.Shortlinks);
            Assert.Equal("caf-2026", shortlink.Slug);
            Assert.True(WebLinkService.Validate(loaded).Success);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate404Report_SuggestsGeneratedRoutesFromApacheLog()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_site", "docs", "install"));
            File.WriteAllText(Path.Combine(root, "_site", "docs", "install", "index.html"), "<html>install</html>");
            File.WriteAllText(Path.Combine(root, "_site", "404.html"), "<html>404</html>");
            var logPath = Path.Combine(root, "access.log");
            File.WriteAllText(logPath,
                """
                127.0.0.1 - - [01/Jan/2026:00:00:00 +0000] "GET /docs/instal HTTP/1.1" 404 123 "-" "test"
                127.0.0.1 - - [01/Jan/2026:00:00:01 +0000] "GET /assets/missing.png HTTP/1.1" 404 123 "-" "test"
                """);

            var result = WebLinkService.Generate404Report(new WebLink404ReportOptions
            {
                SiteRoot = Path.Combine(root, "_site"),
                SourcePath = logPath
            });

            Assert.Equal(1, result.ObservationCount);
            var suggestion = Assert.Single(result.Suggestions);
            Assert.Equal("/docs/instal", suggestion.Path);
            Assert.Contains(suggestion.Suggestions, item => item.TargetPath == "/docs/install/");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate404Report_DoesNotSuggestRootForUnrelatedMissingPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-404-root-score-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_site"));
            File.WriteAllText(Path.Combine(root, "_site", "index.html"), "<html>home</html>");
            var logPath = Path.Combine(root, "404.csv");
            File.WriteAllText(logPath,
                """
                path,count,status
                /wp-login.php,4,404
                """);

            var result = WebLinkService.Generate404Report(new WebLink404ReportOptions
            {
                SiteRoot = Path.Combine(root, "_site"),
                SourcePath = logPath,
                MinimumScore = 0.8
            });

            var suggestion = Assert.Single(result.Suggestions);
            Assert.Empty(suggestion.Suggestions);
            Assert.Equal(0, result.SuggestedObservationCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate404Report_HonorsIgnored404Rules()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-404-ignore-filter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_site", "docs", "install"));
            File.WriteAllText(Path.Combine(root, "_site", "docs", "install", "index.html"), "<html>install</html>");
            var logPath = Path.Combine(root, "access.log");
            File.WriteAllText(logPath,
                """
                127.0.0.1 - - [01/Jan/2026:00:00:00 +0000] "GET /docs/instal HTTP/1.1" 404 123 "-" "test"
                127.0.0.1 - - [01/Jan/2026:00:00:01 +0000] "GET /scanner/probe HTTP/1.1" 404 123 "-" "test"
                """);
            var ignoredPath = Path.Combine(root, "ignored-404.json");
            File.WriteAllText(ignoredPath,
                """
                {
                  "ignored404": [
                    { "path": "/scanner/*", "reason": "scanner noise" }
                  ]
                }
                """);

            var result = WebLinkService.Generate404Report(new WebLink404ReportOptions
            {
                SiteRoot = Path.Combine(root, "_site"),
                SourcePath = logPath,
                Ignored404Path = ignoredPath
            });

            Assert.Equal(1, result.ObservationCount);
            Assert.Equal(1, result.IgnoredObservationCount);
            Assert.DoesNotContain(result.Suggestions, item => item.Path == "/scanner/probe");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate404Report_AllowsMissingSourceWhenConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-404-missing-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_site"));
            File.WriteAllText(Path.Combine(root, "_site", "index.html"), "<html>home</html>");

            var result = WebLinkService.Generate404Report(new WebLink404ReportOptions
            {
                SiteRoot = Path.Combine(root, "_site"),
                SourcePath = Path.Combine(root, "missing.log"),
                AllowMissingSource = true
            });

            Assert.Equal(0, result.ObservationCount);
            Assert.Equal(0, result.SuggestedObservationCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Promote404Suggestions_WritesDisabledRedirectCandidates()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-promote-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var reportPath = Path.Combine(root, "404-suggestions.json");
            var redirectsPath = Path.Combine(root, "data", "links", "redirects.json");
            var report = new WebLink404ReportResult
            {
                SourcePath = Path.Combine(root, "access.log"),
                Suggestions = new[]
                {
                    new WebLink404Suggestion
                    {
                        Path = "/docs/instal",
                        Host = "evotec.xyz",
                        Count = 4,
                        Suggestions = new[]
                        {
                            new WebLink404RouteSuggestion
                            {
                                TargetPath = "/docs/install/",
                                Score = 0.9d
                            }
                        }
                    }
                }
            };
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report));

            var result = WebLinkService.Promote404Suggestions(new WebLink404PromoteOptions
            {
                SourcePath = reportPath,
                OutputPath = redirectsPath
            });

            Assert.Equal(1, result.CandidateCount);
            Assert.Equal(1, result.WrittenCount);

            var loaded = WebLinkService.Load(new WebLinkLoadOptions
            {
                RedirectsPath = redirectsPath
            });
            var redirect = Assert.Single(loaded.Redirects);
            Assert.False(redirect.Enabled);
            Assert.Equal("evotec.xyz", redirect.SourceHost);
            Assert.Equal("/docs/instal", redirect.SourcePath);
            Assert.Equal("/docs/install/", redirect.TargetUrl);
            Assert.Equal("404-promoted", redirect.Source);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Ignore404Suggestions_WritesIgnoredRulesForSelectedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-ignore-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var reportPath = Path.Combine(root, "404-suggestions.json");
            var ignoredPath = Path.Combine(root, "data", "links", "ignored-404.json");
            var report = new WebLink404ReportResult
            {
                Suggestions = new[]
                {
                    new WebLink404Suggestion
                    {
                        Path = "/wp-login.php",
                        Count = 5
                    },
                    new WebLink404Suggestion
                    {
                        Path = "/docs/instal",
                        Count = 2,
                        Suggestions = new[]
                        {
                            new WebLink404RouteSuggestion { TargetPath = "/docs/install/", Score = 0.9d }
                        }
                    }
                }
            };
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report));

            var result = WebLinkService.Ignore404Suggestions(new WebLink404IgnoreOptions
            {
                SourcePath = reportPath,
                OutputPath = ignoredPath,
                Paths = new[] { "/wp-login.php" },
                Reason = "scanner noise",
                CreatedBy = "tests"
            });

            Assert.Equal(1, result.CandidateCount);
            Assert.Equal(1, result.WrittenCount);

            var json = File.ReadAllText(ignoredPath);
            Assert.Contains("\"path\": \"/wp-login.php\"", json, StringComparison.Ordinal);
            Assert.Contains("\"reason\": \"scanner noise\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("/docs/instal", json, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ApplyReviewCandidates_MergesReviewedCandidatesWithoutPowerShell()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-apply-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var redirectCandidatesPath = Path.Combine(root, "Build", "link-reports", "404-promoted-candidates.json");
            var ignoredCandidatesPath = Path.Combine(root, "Build", "link-reports", "ignored-404-candidates.json");
            var redirectsPath = Path.Combine(root, "data", "links", "redirects.json");
            var ignoredPath = Path.Combine(root, "data", "links", "ignored-404.json");
            Directory.CreateDirectory(Path.GetDirectoryName(redirectCandidatesPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(redirectsPath)!);

            File.WriteAllText(redirectCandidatesPath,
                """
                {
                  "redirects": [
                    {
                      "id": "reviewed",
                      "sourcePath": "/docs/instal",
                      "targetUrl": "/docs/install/",
                      "enabled": false,
                      "source": "404-promoted"
                    }
                  ]
                }
                """);
            File.WriteAllText(ignoredCandidatesPath,
                """
                {
                  "ignored404": [
                    {
                      "path": "/wp-login.php",
                      "reason": "scanner noise"
                    }
                  ]
                }
                """);
            File.WriteAllText(redirectsPath, "{ \"redirects\": [] }");
            File.WriteAllText(ignoredPath, "{ \"ignored404\": [] }");

            var result = WebLinkService.ApplyReviewCandidates(new WebLinkReviewApplyOptions
            {
                ApplyRedirects = true,
                ApplyIgnored404 = true,
                RedirectCandidatesPath = redirectCandidatesPath,
                RedirectsPath = redirectsPath,
                Ignored404CandidatesPath = ignoredCandidatesPath,
                Ignored404Path = ignoredPath
            });

            Assert.False(result.DryRun);
            Assert.NotNull(result.Redirects);
            Assert.NotNull(result.Ignored404);
            Assert.Equal(1, result.Redirects.CandidateCount);
            Assert.Equal(1, result.Redirects.WrittenCount);
            Assert.Equal(1, result.Ignored404.CandidateCount);
            Assert.Equal(1, result.Ignored404.WrittenCount);

            var redirectJson = File.ReadAllText(redirectsPath);
            var ignoredJson = File.ReadAllText(ignoredPath);
            Assert.Contains("\"sourcePath\": \"/docs/instal\"", redirectJson, StringComparison.Ordinal);
            Assert.Contains("\"enabled\": false", redirectJson, StringComparison.Ordinal);
            Assert.Contains("\"path\": \"/wp-login.php\"", ignoredJson, StringComparison.Ordinal);

            var duplicateResult = WebLinkService.ApplyReviewCandidates(new WebLinkReviewApplyOptions
            {
                ApplyRedirects = true,
                ApplyIgnored404 = true,
                RedirectCandidatesPath = redirectCandidatesPath,
                RedirectsPath = redirectsPath,
                Ignored404CandidatesPath = ignoredCandidatesPath,
                Ignored404Path = ignoredPath
            });

            Assert.Equal(1, duplicateResult.Redirects!.SkippedDuplicateCount);
            Assert.Equal(1, duplicateResult.Ignored404!.SkippedDuplicateCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Validate_LanguageRootHostTreatsPrefixedAndRootTargetsAsSame()
    {
        var dataSet = new WebLinkDataSet
        {
            LanguageRootHosts = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["evotec.pl"] = "pl"
            },
            Redirects = new[]
            {
                new LinkRedirectRule
                {
                    Id = "localized",
                    SourceHost = "evotec.pl",
                    SourcePath = "/stary/",
                    TargetUrl = "/pl/blog/current/",
                    Status = 301
                },
                new LinkRedirectRule
                {
                    Id = "rooted",
                    SourceHost = "evotec.pl",
                    SourcePath = "/stary",
                    TargetUrl = "https://evotec.pl/blog/current/",
                    Status = 301,
                    AllowExternal = true
                }
            }
        };

        var result = WebLinkService.Validate(dataSet);

        Assert.True(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE_SAME_TARGET");
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE");
    }

    [Fact]
    public void ExportApache_StripsLanguagePrefixForLanguageRootHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-export-language-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outPath = Path.Combine(root, "links.conf");
            var dataSet = new WebLinkDataSet
            {
                Redirects = new[]
                {
                    new LinkRedirectRule
                    {
                        Id = "legacy-pl",
                        SourceHost = "evotec.pl",
                        SourcePath = "/stary/",
                        TargetUrl = "/pl/blog/current/",
                        Status = 301
                    }
                }
            };

            WebLinkService.ExportApache(dataSet, new WebLinkApacheExportOptions
            {
                OutputPath = outPath,
                LanguageRootHosts = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evotec.pl"] = "pl"
                }
            });

            var apache = File.ReadAllText(outPath);
            Assert.Contains("RewriteRule ^stary/?$ /blog/current/ [R=301,L,QSD]", apache, StringComparison.Ordinal);
            Assert.DoesNotContain("/pl/blog/current/", apache, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_ReadsJsonAndCompatibilityCsv()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var redirectsPath = Path.Combine(root, "redirects.json");
            var csvPath = Path.Combine(root, "legacy.csv");
            File.WriteAllText(redirectsPath,
                """
                {
                  "redirects": [
                    {
                      "id": "manual",
                      "sourcePath": "/manual/",
                      "targetUrl": "/target/",
                      "status": 301
                    }
                  ]
                }
                """);
            File.WriteAllText(csvPath,
                """
                legacy_url,target_url,status,language
                /?page_id=40,/contact/,301,pl
                """);

            var dataSet = WebLinkService.Load(new WebLinkLoadOptions
            {
                RedirectsPath = redirectsPath,
                RedirectCsvPaths = new[] { csvPath },
                Hosts = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["pl"] = "evotec.pl"
                }
            });

            Assert.Equal(2, dataSet.Redirects.Length);
            Assert.Equal("evotec.pl", dataSet.Redirects.Single(rule => rule.SourceQuery == "page_id=40").SourceHost);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_ReadsWrappedRedirectsCaseInsensitively()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-links-load-wrapper-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var redirectsPath = Path.Combine(root, "redirects.json");
            File.WriteAllText(redirectsPath,
                """
                {
                  "Redirects": [
                    {
                      "id": "manual",
                      "sourcePath": "/manual/",
                      "targetUrl": "/target/",
                      "status": 301
                    }
                  ]
                }
                """);

            var dataSet = WebLinkService.Load(new WebLinkLoadOptions
            {
                RedirectsPath = redirectsPath
            });

            var redirect = Assert.Single(dataSet.Redirects);
            Assert.Equal("manual", redirect.Id);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
