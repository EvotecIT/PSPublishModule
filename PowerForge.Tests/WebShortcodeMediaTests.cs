using ImageMagick;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebShortcodeMediaTests
{
    [Fact]
    public void Build_RendersYouTubeShortcode_WithResponsiveEmbed()
    {
        var html = BuildSinglePageSite(
            """
            {{< youtube id="dQw4w9WgXcQ" start="42" size="md" >}}
            """);

        Assert.Contains("data-pf-youtube-url=\"https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ?start=42", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pf-media-youtube-lite-v1", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pf-media-base-v1", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("start=42", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aspect-ratio:16/9", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersScreenshotShortcode_WithSizeAwareContainer()
    {
        var html = BuildSinglePageSite(
            """
            {{< screenshot src="/images/dashboard.png" alt="Dashboard" caption="Overview" size="sm" srcset="/images/dashboard-640.png 640w, /images/dashboard.png 1200w" sizes="(max-width: 900px) 100vw, 900px" fetchpriority="low" >}}
            """);

        Assert.Contains("class=\"pf-screenshot", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-width:420px", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("loading=\"lazy\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("srcset=\"/images/dashboard-640.png 640w, /images/dashboard.png 1200w\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sizes=\"(max-width: 900px) 100vw, 900px\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fetchpriority=\"low\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<figcaption", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersScreenshotsShortcode_FromData_InGridLayout()
    {
        var html = BuildSinglePageSite(
            """
            {{< screenshots data="media.shots" layout="grid" columns="3" >}}
            """,
            root =>
            {
                var dataDir = Path.Combine(root, "data");
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(Path.Combine(dataDir, "media.json"),
                    """
                    {
                      "shots": [
                        { "src": "/images/one.png", "caption": "One", "size": "xl" },
                        { "src": "/images/two.png", "caption": "Two" }
                      ]
                    }
                    """);
            });

        Assert.Contains("class=\"pf-screenshots pf-screenshots-grid", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grid-template-columns:repeat(3", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/images/one.png", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/images/two.png", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grid-column:span 3", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersXShortcode_WithTwitterWidget()
    {
        var html = BuildSinglePageSite(
            """
            {{< x url="https://x.com/evotecit/status/1234567890" size="md" >}}
            """);

        Assert.Contains("class=\"twitter-tweet\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-pf-x-embed", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("platform.twitter.com/widgets.js", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pf-media-x-embed-v1", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://x.com/evotecit/status/1234567890", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersMediaShortcode_DispatchingToYouTube()
    {
        var html = BuildSinglePageSite(
            """
            {{< media type="youtube" src="https://www.youtube.com/watch?v=dQw4w9WgXcQ" start="9" >}}
            """);

        Assert.Contains("youtube-nocookie.com/embed/dQw4w9WgXcQ", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("start=9", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersMapShortcode_WithQueryEmbed()
    {
        var html = BuildSinglePageSite(
            """
            {{< map query="Evotec Services, Mikolow" title="Office map" caption="Find us here" size="lg" >}}
            """);

        Assert.Contains("class=\"pf-media pf-media-map", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://www.google.com/maps?q=Evotec%20Services%2C%20Mikolow", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("output=embed", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("title=\"Office map\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<figcaption", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersYouTubeLiteScript_OnlyOnce_PerPage()
    {
        var html = BuildSinglePageSite(
            """
            {{< youtube id="dQw4w9WgXcQ" >}}
            {{< youtube id="kXYiU_JCYtU" >}}
            """);

        var occurrences = CountOccurrences(html, "pf-media-youtube-lite-v1");
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void Build_RendersXWidgetScript_OnlyOnce_PerPage()
    {
        var html = BuildSinglePageSite(
            """
            {{< x url="https://x.com/evotecit/status/1234567890" >}}
            {{< x url="https://x.com/evotecit/status/9876543210" >}}
            """);

        var occurrences = CountOccurrences(html, "pf-media-x-embed-v1");
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void Build_RendersMediaBaseCss_OnlyOnce_PerPage()
    {
        var html = BuildSinglePageSite(
            """
            {{< screenshot src="/images/a.png" >}}
            {{< youtube id="dQw4w9WgXcQ" >}}
            """);

        var occurrences = CountOccurrences(html, "pf-media-base-v1");
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void Build_RendersStory_WithAnimatedAndCompletedFallbacks()
    {
        var html = BuildSinglePageSite(
            """
            {{< story manifest="static/stories/chart/visual-story.json" base="/stories/chart demo,1x" transcript="expanded" >}}
            """,
            root =>
            {
                var bundleRoot = Path.Combine(root, "static", "stories", "chart");
                Directory.CreateDirectory(bundleRoot);
                File.WriteAllText(Path.Combine(bundleRoot, "demo.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
                using (var image = new MagickImage(MagickColors.Transparent, 2, 2))
                {
                    image.Write(Path.Combine(bundleRoot, "demo.png"), MagickFormat.Png);
                }
                File.WriteAllText(Path.Combine(bundleRoot, "demo.txt"), "Create chart\nChart is visible");
                File.WriteAllText(Path.Combine(bundleRoot, "visual-story.json"),
                    """
                    {
                      "schemaVersion": 1,
                      "id": "chart-five-lines",
                      "title": "Create a chart in five lines",
                      "alt": "Source code followed by the generated chart.",
                      "outcome": "The chart is visible.",
                      "artifacts": [
                        { "role": "animated", "format": "svg", "path": "demo.svg" },
                        { "role": "completed", "format": "png", "path": "demo.png" },
                        { "role": "transcript", "format": "text", "path": "demo.txt" }
                      ]
                    }
                    """);
            });

        Assert.Contains("data-pf-story=\"chart-five-lines\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prefers-reduced-motion: reduce", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/stories/chart demo,1x/demo.svg", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("srcset=\"/stories/chart%20demo%2C1x/demo.png\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<source media=\"print\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The chart is visible.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<details class=\"pf-story-transcript\" open>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chart is visible", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RendersApngByDefaultAndEncodesNestedArtifactUrls()
    {
        var html = BuildSinglePageSite(
            """
            {{< story manifest="./static/stories/chart demo/visual-story.json" transcript="hidden" >}}
            """,
            root =>
            {
                var bundleRoot = Path.Combine(root, "static", "stories", "chart demo");
                Directory.CreateDirectory(Path.Combine(bundleRoot, "media"));
                WebVisualStoryAnimatedArtifactTests.WriteTinyApng(
                    Path.Combine(bundleRoot, "media", "demo frame.png"));
                using (var completed = new MagickImage(MagickColors.Transparent, 2, 2))
                {
                    completed.Write(Path.Combine(bundleRoot, "demo.png"), MagickFormat.Png);
                }
                File.WriteAllText(Path.Combine(bundleRoot, "visual-story.json"),
                    """
                    {
                      "schemaVersion": 1,
                      "id": "apng-story",
                      "title": "APNG story",
                      "alt": "Animated result.",
                      "outcome": "The result is visible.",
                      "artifacts": [
                        { "role": "animated", "format": "apng", "path": "media/demo frame.png" },
                        { "role": "completed", "format": "png", "path": "demo.png" }
                      ]
                    }
                    """);
            });

        Assert.Contains("/stories/chart%20demo/media/demo%20frame.png", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<source media=\"print\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_DerivesRootUrlForManifestDirectlyUnderStatic()
    {
        var html = BuildSinglePageSite(
            """
            {{< story manifest="static/visual-story.json" transcript="hidden" >}}
            """,
            root =>
            {
                var bundleRoot = Path.Combine(root, "static");
                Directory.CreateDirectory(bundleRoot);
                File.WriteAllText(
                    Path.Combine(bundleRoot, "demo.svg"),
                    "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
                using (var completed = new MagickImage(MagickColors.Transparent, 2, 2))
                {
                    completed.Write(Path.Combine(bundleRoot, "demo.png"), MagickFormat.Png);
                }
                File.WriteAllText(
                    Path.Combine(bundleRoot, "visual-story.json"),
                    """
                    {
                      "schemaVersion": 1,
                      "id": "root-story",
                      "title": "Root story",
                      "alt": "The result appears.",
                      "outcome": "The result is visible.",
                      "artifacts": [
                        { "role": "animated", "format": "svg", "path": "demo.svg" },
                        { "role": "completed", "format": "png", "path": "demo.png" }
                      ]
                    }
                    """);
            },
            assertOutput: output =>
            {
                Assert.True(File.Exists(Path.Combine(output, "visual-story.json")));
                Assert.True(File.Exists(Path.Combine(output, "demo.svg")));
                Assert.True(File.Exists(Path.Combine(output, "demo.png")));
            });

        Assert.Contains("src=\"/demo.svg\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/static/demo.svg", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_PublishesConventionalStaticStoriesAlongsideUnrelatedMappings()
    {
        var html = BuildSinglePageSite(
            "{{< story manifest=\"static/visual-story.json\" transcript=\"hidden\" >}}",
            root =>
            {
                var bundleRoot = Path.Combine(root, "static");
                Directory.CreateDirectory(bundleRoot);
                File.WriteAllText(Path.Combine(root, "favicon.ico"), "icon");
                File.WriteAllText(
                    Path.Combine(bundleRoot, "demo.svg"),
                    "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
                using (var completed = new MagickImage(MagickColors.Transparent, 2, 2))
                {
                    completed.Write(Path.Combine(bundleRoot, "demo.png"), MagickFormat.Png);
                }
                File.WriteAllText(
                    Path.Combine(bundleRoot, "visual-story.json"),
                    """
                    {
                      "schemaVersion": 1,
                      "id": "root-story",
                      "title": "Root story",
                      "alt": "The result appears.",
                      "outcome": "The result is visible.",
                      "artifacts": [
                        { "role": "animated", "format": "svg", "path": "demo.svg" },
                        { "role": "completed", "format": "png", "path": "demo.png" }
                      ]
                    }
                    """);
            },
            spec => spec.StaticAssets =
            [
                new StaticAssetSpec { Source = "favicon.ico", Destination = "favicon.ico" }
            ],
            assertOutput: output =>
            {
                Assert.True(File.Exists(Path.Combine(output, "visual-story.json")));
                Assert.True(File.Exists(Path.Combine(output, "demo.svg")));
                Assert.True(File.Exists(Path.Combine(output, "demo.png")));
                Assert.True(File.Exists(Path.Combine(output, "favicon.ico")));
            });

        Assert.Contains("src=\"/demo.svg\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_DerivesStoryUrlsFromConfiguredStaticAssetMapping()
    {
        var html = BuildSinglePageSite(
            """
            {{< story manifest="generated/story/visual-story.json" transcript="hidden" >}}
            """,
            root =>
            {
                var bundleRoot = Path.Combine(root, "generated", "story");
                Directory.CreateDirectory(bundleRoot);
                File.WriteAllText(
                    Path.Combine(bundleRoot, "demo.svg"),
                    "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
                using (var completed = new MagickImage(MagickColors.Transparent, 2, 2))
                {
                    completed.Write(Path.Combine(bundleRoot, "demo.png"), MagickFormat.Png);
                }
                File.WriteAllText(
                    Path.Combine(bundleRoot, "visual-story.json"),
                    """
                    {
                      "schemaVersion": 1,
                      "id": "mapped-story",
                      "title": "Mapped story",
                      "alt": "The mapped result appears.",
                      "outcome": "The mapped result is visible.",
                      "artifacts": [
                        { "role": "animated", "format": "svg", "path": "demo.svg" },
                        { "role": "completed", "format": "png", "path": "demo.png" }
                      ]
                    }
                    """);
            },
            spec =>
            {
                spec.StaticAssets =
                [
                    new StaticAssetSpec
                    {
                        Source = "generated/story/visual-story.json",
                        Destination = "manifests/story.json"
                    },
                    new StaticAssetSpec
                    {
                        Source = "generated/story",
                        Destination = "stories/demo"
                    }
                ];
            });

        Assert.Contains("src=\"/stories/demo/demo.svg\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("srcset=\"/stories/demo/demo.png\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.True(html.Contains("href=\"/stories/demo/demo.svg\"", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_DerivesStoryUrlsFromPublishedPageBundleResources()
    {
        var html = BuildSinglePageSite(
            """
            {{< story manifest="content/pages/demo/story/visual-story.json" transcript="hidden" >}}
            """,
            root =>
            {
                var bundleRoot = Path.Combine(root, "content", "pages", "demo", "story");
                Directory.CreateDirectory(bundleRoot);
                File.WriteAllText(
                    Path.Combine(bundleRoot, "demo.svg"),
                    "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
                using (var completed = new MagickImage(MagickColors.Transparent, 2, 2))
                {
                    completed.Write(Path.Combine(bundleRoot, "demo.png"), MagickFormat.Png);
                }
                File.WriteAllText(
                    Path.Combine(bundleRoot, "visual-story.json"),
                    """
                    {
                      "schemaVersion": 1,
                      "id": "page-bundle-story",
                      "title": "Page bundle story",
                      "alt": "The page bundle result appears.",
                      "outcome": "The page bundle result is visible.",
                      "artifacts": [
                        { "role": "animated", "format": "svg", "path": "demo.svg" },
                        { "role": "completed", "format": "png", "path": "demo.png" }
                      ]
                    }
                    """);
            },
            spec => spec.Collections[0].Output = "/blog",
            pageRelativePath: Path.Combine("demo", "index.md"),
            outputRelativePath: Path.Combine("blog", "demo", "index.html"),
            includeIndexSlug: false);

        Assert.Contains("src=\"/blog/demo/story/demo.svg\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("srcset=\"/blog/demo/story/demo.png\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/content/pages/demo/story/", html, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSinglePageSite(
        string markdown,
        Action<string>? setup = null,
        Action<SiteSpec>? configure = null,
        string pageRelativePath = "index.md",
        string outputRelativePath = "index.html",
        bool includeIndexSlug = true,
        Action<string>? assertOutput = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-shortcode-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            setup?.Invoke(root);

            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            var pagePath = Path.Combine(pagesPath, pageRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);
            var slugLine = includeIndexSlug ? "slug: index" : string.Empty;
            File.WriteAllText(pagePath,
                $$"""
                ---
                title: Home
                {{slugLine}}
                ---

                {{markdown}}
                """);

            var themeRoot = Path.Combine(root, "themes", "t");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
                """
                <!doctype html>
                <html>
                <head><title>{{TITLE}}</title>{{EXTRA_CSS}}</head>
                <body>{{CONTENT}}{{EXTRA_SCRIPTS}}</body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "t",
                  "engine": "simple",
                  "defaultLayout": "home"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Shortcode Media Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DataRoot = "data",
                DefaultTheme = "t",
                ThemesRoot = "themes",
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
            configure?.Invoke(spec);

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);

            var outPath = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outPath);

            var indexHtml = Path.Combine(outPath, outputRelativePath);
            Assert.True(File.Exists(indexHtml), "Expected index.html to be generated.");
            assertOutput?.Invoke(outPath);
            return File.ReadAllText(indexHtml);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static int CountOccurrences(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
            return 0;

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
