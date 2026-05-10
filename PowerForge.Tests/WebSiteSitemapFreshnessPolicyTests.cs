using System.Diagnostics;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteSitemapFreshnessPolicyTests
{
    [Fact]
    public void CollectionSpec_SitemapLastmodPolicy_DeserializesCamelCaseValue()
    {
        var spec = JsonSerializer.Deserialize<SiteSpec>(
            """
            {
              "name": "Freshness Test",
              "baseUrl": "https://example.test",
              "collections": [
                {
                  "name": "docs",
                  "input": "content/docs",
                  "output": "/docs",
                  "sitemapLastModified": "explicitOnly"
                }
              ]
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(spec);
        Assert.Equal(SitemapLastModifiedPolicy.ExplicitOnly, spec!.Collections[0].SitemapLastModified);

        var json = JsonSerializer.Serialize(spec.Collections[0], new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Contains("\"sitemapLastModified\":\"explicitOnly\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionSpec_SitemapLastmodPolicy_RejectsNumericEnumValues()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SiteSpec>(
            """
            {
              "name": "Freshness Test",
              "baseUrl": "https://example.test",
              "collections": [
                {
                  "name": "docs",
                  "input": "content/docs",
                  "output": "/docs",
                  "sitemapLastModified": 2
                }
              ]
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
    }

    [Fact]
    public void Build_AutoSitemapLastmod_PreservesEditorialPublishDate()
    {
        var root = CreateTempRoot("pf-web-sitemap-editorial-freshness-");
        try
        {
            WriteMarkdown(root, "content/blog/ship-log.md",
                """
                ---
                title: Ship Log
                slug: ship-log
                date: 2020-01-02
                ---

                Editorial content.
                """);
            CommitAll(root, "2026-01-15T12:34:56Z");

            Build(root, new[]
            {
                new CollectionSpec
                {
                    Name = "blog",
                    Preset = "blog",
                    Input = "content/blog",
                    Output = "/blog"
                }
            });

            var lastmod = ReadSitemapMetadataLastModified(root, "/blog/ship-log/");
            Assert.Equal("2020-01-02T00:00:00.000Z", lastmod);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_AutoSitemapLastmod_UsesSourceDateForReferencePages()
    {
        var root = CreateTempRoot("pf-web-sitemap-source-freshness-");
        try
        {
            WriteMarkdown(root, "content/pages/about.md",
                """
                ---
                title: About
                slug: about
                date: 2020-01-02
                ---

                Reference content.
                """);
            CommitAll(root, "2026-01-15T12:34:56Z");

            Build(root, new[]
            {
                new CollectionSpec
                {
                    Name = "pages",
                    Preset = "pages",
                    Input = "content/pages",
                    Output = "/"
                }
            });

            var lastmod = ReadSitemapMetadataLastModified(root, "/about/");
            Assert.Equal("2026-01-15T12:34:56.000Z", lastmod);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_AutoSitemapLastmod_DoesNotTreatBlogPrefixedReferenceRoutesAsEditorial()
    {
        var root = CreateTempRoot("pf-web-sitemap-blog-tools-freshness-");
        try
        {
            WriteMarkdown(root, "content/blog-tools/reference.md",
                """
                ---
                title: Reference Tooling
                slug: reference
                date: 2020-01-02
                ---

                Reference content.
                """);
            CommitAll(root, "2026-01-15T12:34:56Z");

            Build(root, new[]
            {
                new CollectionSpec
                {
                    Name = "tools",
                    Input = "content/blog-tools",
                    Output = "/blog-tools"
                }
            });

            Assert.Equal("2026-01-15T12:34:56.000Z", ReadSitemapMetadataLastModified(root, "/blog-tools/reference/"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_ExplicitOnlySitemapLastmod_LeavesLastmodUnsetWithoutExplicitMetadata()
    {
        var root = CreateTempRoot("pf-web-sitemap-explicit-only-");
        try
        {
            WriteMarkdown(root, "content/pages/about.md",
                """
                ---
                title: About
                slug: about
                date: 2020-01-02
                ---

                Reference content.
                """);
            CommitAll(root, "2026-01-15T12:34:56Z");

            Build(root, new[]
            {
                new CollectionSpec
                {
                    Name = "pages",
                    Preset = "pages",
                    Input = "content/pages",
                    Output = "/",
                    SitemapLastModified = SitemapLastModifiedPolicy.ExplicitOnly
                }
            });

            Assert.Null(ReadSitemapMetadataLastModified(root, "/about/"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_SourceDateSitemapLastmod_UsesGitDateForEditorialCollections()
    {
        var root = CreateTempRoot("pf-web-sitemap-explicit-source-");
        try
        {
            WriteMarkdown(root, "content/blog/ship-log.md",
                """
                ---
                title: Ship Log
                slug: ship-log
                date: 2020-01-02
                ---

                Editorial content.
                """);
            CommitAll(root, "2026-01-15T12:34:56Z");

            Build(root, new[]
            {
                new CollectionSpec
                {
                    Name = "blog",
                    Preset = "blog",
                    Input = "content/blog",
                    Output = "/blog",
                    SitemapLastModified = SitemapLastModifiedPolicy.SourceDate
                }
            });

            Assert.Equal("2026-01-15T12:34:56.000Z", ReadSitemapMetadataLastModified(root, "/blog/ship-log/"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_PublishedDateSitemapLastmod_FallsBackToGitWhenDateMissing()
    {
        var root = CreateTempRoot("pf-web-sitemap-published-fallback-");
        try
        {
            WriteMarkdown(root, "content/blog/undated.md",
                """
                ---
                title: Undated Post
                slug: undated
                ---

                Editorial content without a publish date.
                """);
            CommitAll(root, "2026-01-15T12:34:56Z");

            Build(root, new[]
            {
                new CollectionSpec
                {
                    Name = "blog",
                    Preset = "blog",
                    Input = "content/blog",
                    Output = "/blog",
                    SitemapLastModified = SitemapLastModifiedPolicy.PublishedDate
                }
            });

            Assert.Equal("2026-01-15T12:34:56.000Z", ReadSitemapMetadataLastModified(root, "/blog/undated/"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_ExplicitLastmod_DrivesSitemapAndArticleStructuredData()
    {
        var root = CreateTempRoot("pf-web-sitemap-explicit-freshness-");
        try
        {
            WriteMarkdown(root, "content/blog/updated.md",
                """
                ---
                title: Updated Post
                slug: updated
                date: 2020-01-02
                lastmod: 2026-02-03T04:05:06Z
                ---

                Updated editorial content.
                """);

            var spec = new SiteSpec
            {
                Name = "Freshness Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Preset = "blog",
                        Input = "content/blog",
                        Output = "/blog"
                    }
                },
                StructuredData = new StructuredDataSpec
                {
                    Enabled = true,
                    Article = true
                },
                Social = new SocialSpec
                {
                    Enabled = true
                }
            };

            Build(root, spec);

            Assert.Equal("2026-02-03T04:05:06.000Z", ReadSitemapMetadataLastModified(root, "/blog/updated/"));

            var html = File.ReadAllText(Path.Combine(root, "_site", "blog", "updated", "index.html"));
            Assert.Contains("\"datePublished\":\"2020-01-02T00:00:00.0000000Z\"", html, StringComparison.Ordinal);
            // Offset formatting can differ between date parsers; this assertion is about the chosen instant.
            Assert.Contains("\"dateModified\":\"2026-02-03T04:05:06", html, StringComparison.Ordinal);
            Assert.Contains("property=\"article:published_time\" content=\"2020-01-02T00:00:00.0000000Z\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"article:modified_time\" content=\"2026-02-03T04:05:06", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static void Build(string root, CollectionSpec[] collections)
    {
        Build(root, new SiteSpec
        {
            Name = "Freshness Test",
            BaseUrl = "https://example.test",
            ContentRoot = "content",
            Collections = collections
        });
    }

    private static void Build(string root, SiteSpec spec)
    {
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        var plan = WebSitePlanner.Plan(spec, configPath);
        WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));
    }

    private static string? ReadSitemapMetadataLastModified(string root, string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "_site", "_powerforge", "sitemap-entries.json")));
        var normalizedPath = path.TrimEnd('/');
        foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            var entryPath = entry.GetProperty("path").GetString();
            if (entryPath?.TrimEnd('/').Equals(normalizedPath, StringComparison.OrdinalIgnoreCase) == true)
                return entry.TryGetProperty("lastModified", out var lastModified)
                    ? lastModified.GetString()
                    : null;
        }

        return null;
    }

    private static string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteMarkdown(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void CommitAll(string root, string isoDate)
    {
        RunGit(root, "init");
        RunGit(root, "config", "user.email", "tests@example.test");
        RunGit(root, "config", "user.name", "PowerForge Tests");
        RunGit(root, "add", ".");
        RunGitWithDate(root, isoDate, "commit", "-m", "initial");
    }

    private static void RunGit(string root, params string[] args) =>
        RunGitCore(root, null, args);

    private static void RunGitWithDate(string root, string isoDate, params string[] args) =>
        RunGitCore(root, isoDate, args);

    private static void RunGitCore(string root, string? isoDate, params string[] args)
    {
        using var process = new Process();
        process.StartInfo.FileName = "git";
        process.StartInfo.WorkingDirectory = root;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        if (!string.IsNullOrWhiteSpace(isoDate))
        {
            process.StartInfo.Environment["GIT_AUTHOR_DATE"] = isoDate;
            process.StartInfo.Environment["GIT_COMMITTER_DATE"] = isoDate;
        }

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(" ", args)} failed with exit {process.ExitCode}\n{stdout}\n{stderr}");
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
