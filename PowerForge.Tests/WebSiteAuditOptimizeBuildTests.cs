using PowerForge.Web;
using ImageMagick;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void Build_ExposesResolvedVersioningToScribanTemplates()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-versioning-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var themeRoot = Path.Combine(root, "themes", "versioning-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
                """
                <!doctype html>
                <html>
                <body>
                  <div id="current">{{ versioning.current.name }}</div>
                  <div id="latest">{{ versioning.latest.name }}</div>
                  <div id="lts">{{ versioning.lts.name }}</div>
                  <div id="versions">{{ for v in versioning.versions }}{{ v.name }}{{ if v.is_current }}*{{ end }};{{ end }}</div>
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "versioning-test",
                  "engine": "scriban",
                  "defaultLayout": "home"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Versioning Runtime Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "versioning-test",
                ThemesRoot = "themes",
                Versioning = new VersioningSpec
                {
                    Enabled = true,
                    BasePath = "/docs",
                    Current = "v1",
                    Versions = new[]
                    {
                        new VersionSpec { Name = "v2", Label = "v2", Url = "/docs/v2/", Latest = true },
                        new VersionSpec { Name = "v1", Label = "v1", Url = "/docs/v1/", Default = true, Lts = true }
                    }
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var output = File.ReadAllText(Path.Combine(result.OutputPath, "index.html"));
            Assert.Contains("<div id=\"current\">v1</div>", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<div id=\"latest\">v2</div>", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<div id=\"lts\">v1</div>", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("v1*;", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_AppliesImplicitRssOutputsAndExposesFeedUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-rss-defaults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"),
                """
                ---
                title: Blog
                ---

                Blog home
                """);
            File.WriteAllText(Path.Combine(blogPath, "first-post.md"),
                """
                ---
                title: First Post
                tags: [release]
                ---

                Hello
                """);

            var themeRoot = Path.Combine(root, "themes", "rss-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "blog.html"),
                """
                <!doctype html>
                <html>
                <head>{{ head_html }}</head>
                <body>
                  <div id="feed">{{ feed_url }}</div>
                  {{ for out in outputs }}<span class="out">{{ out.name }}={{ out.url }}</span>{{ end }}
                  {{ content }}
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "term.html"),
                """
                <!doctype html>
                <html><head>{{ head_html }}</head><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "rss-test",
                  "engine": "scriban",
                  "defaultLayout": "blog"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "RSS Defaults Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "rss-test",
                ThemesRoot = "themes",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        ListLayout = "blog"
                    }
                },
                Taxonomies = new[]
                {
                    new TaxonomySpec { Name = "tags", BasePath = "/tags", TermLayout = "term" }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var blogHtml = File.ReadAllText(Path.Combine(result.OutputPath, "blog", "index.html"));
            Assert.Contains("<div id=\"feed\">https://example.test/blog/index.xml</div>", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rel=\"alternate\"", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("application/rss+xml", blogHtml, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(Path.Combine(result.OutputPath, "blog", "index.xml")));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "tags", "release", "index.xml")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_FeedOptions_LimitItems_IncludeContent_AndRespectTaxonomyOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-feed-options-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"),
                """
                ---
                title: Blog
                ---

                Blog home
                """);
            File.WriteAllText(Path.Combine(blogPath, "first-post.md"),
                """
                ---
                title: First Post
                date: 2026-01-01
                tags: [release]
                ---

                First
                """);
            File.WriteAllText(Path.Combine(blogPath, "second-post.md"),
                """
                ---
                title: Second Post
                date: 2026-01-02
                tags: [release]
                ---

                Second
                """);
            File.WriteAllText(Path.Combine(blogPath, "third-post.md"),
                """
                ---
                title: Third Post
                date: 2026-01-03
                tags: [release]
                ---

                Third
                """);

            var themeRoot = Path.Combine(root, "themes", "feed-options-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "blog.html"),
                """
                <!doctype html>
                <html><head>{{ head_html }}</head><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "term.html"),
                """
                <!doctype html>
                <html><head>{{ head_html }}</head><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "feed-options-test",
                  "engine": "scriban",
                  "defaultLayout": "blog"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Feed Options Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "feed-options-test",
                ThemesRoot = "themes",
                Feed = new FeedSpec
                {
                    MaxItems = 1,
                    IncludeContent = true
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        ListLayout = "blog"
                    }
                },
                Taxonomies = new[]
                {
                    new TaxonomySpec { Name = "tags", BasePath = "/tags", TermLayout = "term", Outputs = new[] { "html" } }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var blogFeedPath = Path.Combine(result.OutputPath, "blog", "index.xml");
            Assert.True(File.Exists(blogFeedPath));

            var doc = XDocument.Load(blogFeedPath);
            var items = doc.Descendants("item").ToArray();
            Assert.Single(items);
            Assert.Equal("Third Post", items[0].Element("title")?.Value);
            Assert.Contains(items[0].Elements(), element =>
                element.Name.LocalName.Equals("encoded", StringComparison.OrdinalIgnoreCase) &&
                element.Name.NamespaceName.Equals("http://purl.org/rss/1.0/modules/content/", StringComparison.OrdinalIgnoreCase));

            Assert.True(File.Exists(Path.Combine(result.OutputPath, "tags", "release", "index.html")));
            Assert.False(File.Exists(Path.Combine(result.OutputPath, "tags", "release", "index.xml")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_FeedDisabled_DisablesImplicitRssOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-feed-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"),
                """
                ---
                title: Blog
                ---

                Blog home
                """);
            File.WriteAllText(Path.Combine(blogPath, "first-post.md"),
                """
                ---
                title: First Post
                tags: [release]
                ---

                First
                """);

            var themeRoot = Path.Combine(root, "themes", "feed-disabled-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "blog.html"),
                """
                <!doctype html>
                <html><head>{{ head_html }}</head><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "term.html"),
                """
                <!doctype html>
                <html><head>{{ head_html }}</head><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "feed-disabled-test",
                  "engine": "scriban",
                  "defaultLayout": "blog"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Feed Disabled Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "feed-disabled-test",
                ThemesRoot = "themes",
                Feed = new FeedSpec
                {
                    Enabled = false
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        ListLayout = "blog"
                    }
                },
                Taxonomies = new[]
                {
                    new TaxonomySpec { Name = "tags", BasePath = "/tags", TermLayout = "term" }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            Assert.True(File.Exists(Path.Combine(result.OutputPath, "blog", "index.html")));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "tags", "release", "index.html")));

            Assert.False(File.Exists(Path.Combine(result.OutputPath, "blog", "index.xml")));
            Assert.False(File.Exists(Path.Combine(result.OutputPath, "tags", "release", "index.xml")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_ImplicitFeedOptions_AddAtomAndJsonFeedOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-feed-parity-implicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"),
                """
                ---
                title: Blog
                ---

                Blog home
                """);
            File.WriteAllText(Path.Combine(blogPath, "first-post.md"),
                """
                ---
                title: First Post
                date: 2026-01-01
                tags: [release]
                ---

                Hello
                """);

            var themeRoot = Path.Combine(root, "themes", "feed-parity-implicit");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "blog.html"),
                """
                <!doctype html>
                <html>
                <head>{{ head_html }}</head>
                <body>
                  <div id="feed">{{ feed_url }}</div>
                  {{ for out in outputs }}<span class="out">{{ out.name }}={{ out.url }}</span>{{ end }}
                  {{ content }}
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "term.html"),
                """
                <!doctype html>
                <html><head>{{ head_html }}</head><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "feed-parity-implicit",
                  "engine": "scriban",
                  "defaultLayout": "blog"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Feed Parity Implicit Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "feed-parity-implicit",
                ThemesRoot = "themes",
                Feed = new FeedSpec
                {
                    IncludeAtom = true,
                    IncludeJsonFeed = true
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        ListLayout = "blog"
                    }
                },
                Taxonomies = new[]
                {
                    new TaxonomySpec { Name = "tags", BasePath = "/tags", TermLayout = "term" }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var blogHtml = File.ReadAllText(Path.Combine(result.OutputPath, "blog", "index.html"));
            Assert.Contains("<div id=\"feed\">https://example.test/blog/index.xml</div>", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("application/atom+xml", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("application/feed+json", blogHtml, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(Path.Combine(result.OutputPath, "blog", "index.xml")));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "blog", "index.atom.xml")));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "blog", "index.feed.json")));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "tags", "release", "index.atom.xml")));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "tags", "release", "index.feed.json")));

            var atom = XDocument.Load(Path.Combine(result.OutputPath, "blog", "index.atom.xml"));
            Assert.Equal("feed", atom.Root?.Name.LocalName);
            Assert.Equal("http://www.w3.org/2005/Atom", atom.Root?.Name.NamespaceName);

            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.OutputPath, "blog", "index.feed.json")));
            Assert.Equal("https://jsonfeed.org/version/1.1", json.RootElement.GetProperty("version").GetString());
            Assert.True(json.RootElement.GetProperty("items").GetArrayLength() > 0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_ExplicitAtomAndJsonFeedOutputs_WorkWithoutRss()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-feed-parity-explicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"),
                """
                ---
                title: Blog
                ---

                Blog home
                """);
            File.WriteAllText(Path.Combine(blogPath, "post.md"),
                """
                ---
                title: Post
                date: 2026-01-05
                tags: [release]
                ---

                <strong>Body</strong>
                """);

            var themeRoot = Path.Combine(root, "themes", "feed-parity-explicit");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "blog.html"),
                """
                <!doctype html>
                <html>
                <head>{{ head_html }}</head>
                <body>
                  <div id="feed">{{ feed_url }}</div>
                  {{ for out in outputs }}<span class="out">{{ out.name }}={{ out.url }}</span>{{ end }}
                  {{ content }}
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "feed-parity-explicit",
                  "engine": "scriban",
                  "defaultLayout": "blog"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Feed Parity Explicit Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "feed-parity-explicit",
                ThemesRoot = "themes",
                Feed = new FeedSpec
                {
                    IncludeContent = true,
                    IncludeCategories = false
                },
                Outputs = new OutputsSpec
                {
                    Rules = new[]
                    {
                        new OutputRuleSpec { Kind = "section", Formats = new[] { "html", "atom", "jsonfeed" } }
                    }
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        ListLayout = "blog"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var blogHtml = File.ReadAllText(Path.Combine(result.OutputPath, "blog", "index.html"));
            Assert.Contains("<div id=\"feed\">https://example.test/blog/index.atom.xml</div>", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("application/atom+xml", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("application/feed+json", blogHtml, StringComparison.OrdinalIgnoreCase);

            Assert.False(File.Exists(Path.Combine(result.OutputPath, "blog", "index.xml")));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "blog", "index.atom.xml")));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "blog", "index.feed.json")));

            var atom = XDocument.Load(Path.Combine(result.OutputPath, "blog", "index.atom.xml"));
            var atomContent = atom.Descendants().FirstOrDefault(element =>
                element.Name.LocalName.Equals("content", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(atomContent);

            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.OutputPath, "blog", "index.feed.json")));
            var item = json.RootElement.GetProperty("items").EnumerateArray().First();
            Assert.True(item.TryGetProperty("content_html", out _));
            Assert.False(item.TryGetProperty("tags", out _));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_BlogPagination_GeneratesPagedRoutesAndContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-blog-pagination-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"),
                """
                ---
                title: Blog
                ---

                Blog index
                """);
            File.WriteAllText(Path.Combine(blogPath, "post-1.md"), "---\ntitle: Post 1\norder: 1\n---\n\nOne");
            File.WriteAllText(Path.Combine(blogPath, "post-2.md"), "---\ntitle: Post 2\norder: 2\n---\n\nTwo");
            File.WriteAllText(Path.Combine(blogPath, "post-3.md"), "---\ntitle: Post 3\norder: 3\n---\n\nThree");
            File.WriteAllText(Path.Combine(blogPath, "post-4.md"), "---\ntitle: Post 4\norder: 4\n---\n\nFour");
            File.WriteAllText(Path.Combine(blogPath, "post-5.md"), "---\ntitle: Post 5\norder: 5\n---\n\nFive");

            var themeRoot = Path.Combine(root, "themes", "pagination-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "blog.html"),
                """
                <!doctype html>
                <html>
                <body>
                  <div id="page">{{ pagination.page }}</div>
                  <div id="total">{{ pagination.total_pages }}</div>
                  <div id="prev">{{ pagination.previous_url }}</div>
                  <div id="next">{{ pagination.next_url }}</div>
                  <ul>{{ for i in items }}<li>{{ i.title }}</li>{{ end }}</ul>
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "pagination-test",
                  "engine": "scriban",
                  "defaultLayout": "blog"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Pagination Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "pagination-test",
                ThemesRoot = "themes",
                TrailingSlash = TrailingSlashMode.Always,
                Pagination = new PaginationSpec
                {
                    Enabled = true,
                    PathSegment = "page"
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        ListLayout = "blog",
                        PageSize = 2
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var page1 = File.ReadAllText(Path.Combine(result.OutputPath, "blog", "index.html"));
            var page2 = File.ReadAllText(Path.Combine(result.OutputPath, "blog", "page", "2", "index.html"));
            var page3 = File.ReadAllText(Path.Combine(result.OutputPath, "blog", "page", "3", "index.html"));

            Assert.Contains("<div id=\"page\">1</div>", page1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<div id=\"total\">3</div>", page1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<div id=\"next\">/blog/page/2/</div>", page1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<li>Post 1</li>", page1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<li>Post 2</li>", page1, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<li>Post 3</li>", page1, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("<div id=\"page\">2</div>", page2, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<div id=\"prev\">/blog/</div>", page2, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<li>Post 3</li>", page2, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<li>Post 4</li>", page2, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<li>Post 5</li>", page2, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("<div id=\"page\">3</div>", page3, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<li>Post 5</li>", page3, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_TaxonomyPagination_AndIndexMetadata_AreExposedToTemplates()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-taxonomy-pagination-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"), "---\ntitle: Blog\n---\n\nBlog");
            File.WriteAllText(Path.Combine(blogPath, "a.md"), "---\ntitle: A\ndate: 2026-01-01\ntags: [release]\n---\n\nA");
            File.WriteAllText(Path.Combine(blogPath, "b.md"), "---\ntitle: B\ndate: 2026-01-02\ntags: [release]\n---\n\nB");
            File.WriteAllText(Path.Combine(blogPath, "c.md"), "---\ntitle: C\ndate: 2026-01-03\ntags: [docs]\n---\n\nC");

            var themeRoot = Path.Combine(root, "themes", "taxonomy-pagination-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "blog.html"),
                """
                <!doctype html>
                <html><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "taxonomy.html"),
                """
                <!doctype html>
                <html>
                <body>
                  <div id="terms-total">{{ taxonomy_index.total_terms }}</div>
                  <div id="items-total">{{ taxonomy_index.total_items }}</div>
                  <div id="page">{{ pagination.page }}</div>
                  <ul>{{ for t in taxonomy_terms }}<li>{{ t.name }}={{ t.count }}</li>{{ end }}</ul>
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "term.html"),
                """
                <!doctype html>
                <html>
                <body>
                  <div id="term">{{ taxonomy_term_summary.name }}</div>
                  <div id="count">{{ taxonomy_term_summary.count }}</div>
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "taxonomy-pagination-test",
                  "engine": "scriban",
                  "defaultLayout": "blog"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Taxonomy Pagination Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "taxonomy-pagination-test",
                ThemesRoot = "themes",
                TrailingSlash = TrailingSlashMode.Always,
                Pagination = new PaginationSpec
                {
                    PathSegment = "page"
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        ListLayout = "blog"
                    }
                },
                Taxonomies = new[]
                {
                    new TaxonomySpec
                    {
                        Name = "tags",
                        BasePath = "/tags",
                        ListLayout = "taxonomy",
                        TermLayout = "term",
                        PageSize = 1
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var tagsPage1 = File.ReadAllText(Path.Combine(result.OutputPath, "tags", "index.html"));
            var tagsPage2 = File.ReadAllText(Path.Combine(result.OutputPath, "tags", "page", "2", "index.html"));
            var releaseTerm = File.ReadAllText(Path.Combine(result.OutputPath, "tags", "release", "index.html"));

            Assert.Contains("<div id=\"terms-total\">2</div>", tagsPage1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<div id=\"items-total\">3</div>", tagsPage1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<div id=\"page\">1</div>", tagsPage1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<li>release=2</li>", tagsPage1, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("<div id=\"page\">2</div>", tagsPage2, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<li>docs=1</li>", tagsPage2, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("<div id=\"term\">release</div>", releaseTerm, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<div id=\"count\">2</div>", releaseTerm, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

}
