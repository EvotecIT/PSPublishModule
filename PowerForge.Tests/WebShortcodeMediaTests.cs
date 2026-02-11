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

    private static string BuildSinglePageSite(string markdown, Action<string>? setup = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-shortcode-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            setup?.Invoke(root);

            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                $$"""
                ---
                title: Home
                slug: index
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

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);

            var outPath = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outPath);

            var indexHtml = Path.Combine(outPath, "index.html");
            Assert.True(File.Exists(indexHtml), "Expected index.html to be generated.");
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
