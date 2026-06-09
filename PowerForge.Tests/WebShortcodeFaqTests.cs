using PowerForge.Web;

namespace PowerForge.Tests;

public class WebShortcodeFaqTests
{
    [Fact]
    public void Build_RendersFaqMarkdownFromPlainAnswerField()
    {
        var html = BuildSinglePageSite(
            """
            {{< faq data="faq" >}}
            """,
            root =>
            {
                var dataDir = Path.Combine(root, "data");
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(Path.Combine(dataDir, "faq.json"),
                    """
                    [
                      {
                        "title": "FAQ",
                        "items": [
                          {
                            "id": "auth",
                            "question": "Auth",
                            "answer": "**Q** auth login opens a URL but nothing happens.\n\n**A** Ensure the callback URL is reachable."
                          }
                        ]
                      }
                    ]
                    """);
            });

        Assert.Contains("<strong>Q</strong>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<strong>A</strong>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("**Q**", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RendersFaqMarkdownFromAnswerMdField()
    {
        var html = BuildSinglePageSite(
            """
            {{< faq data="faq" >}}
            """,
            root =>
            {
                var dataDir = Path.Combine(root, "data");
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(Path.Combine(dataDir, "faq.json"),
                    """
                    [
                      {
                        "title": "FAQ",
                        "items": [
                          {
                            "id": "steps",
                            "question": "Steps",
                            "answer_md": "- first\n- second"
                          }
                        ]
                      }
                    ]
                    """);
            });

        Assert.Contains("<ul>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<li>first</li>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<li>second</li>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_EncodesFaqPlainTextAnswer()
    {
        var html = BuildSinglePageSite(
            """
            {{< faq data="faq" >}}
            """,
            root =>
            {
                var dataDir = Path.Combine(root, "data");
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(Path.Combine(dataDir, "faq.json"),
                    """
                    [
                      {
                        "title": "FAQ",
                        "items": [
                          {
                            "id": "plain",
                            "question": "Plain",
                            "answer": "Use 2 < 3 check."
                          }
                        ]
                      }
                    ]
                    """);
            });

        Assert.Contains("Use 2 &lt; 3 check.", html, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSinglePageSite(string markdown, Action<string>? setup = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-shortcode-faq-" + Guid.NewGuid().ToString("N"));
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
                Name = "Shortcode FAQ Test",
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
}
