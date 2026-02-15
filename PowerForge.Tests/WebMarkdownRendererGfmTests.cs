using PowerForge.Web;

namespace PowerForge.Tests;

public class WebMarkdownRendererGfmTests
{
    [Fact]
    public void Build_QAStyleMarkdown_DoesNotRenderDefinitionLists()
    {
        var html = BuildSinglePageSite(
            """
            # FAQ

            ## Auth

            **Q: `auth login` opens a URL but nothing happens.**  
            A: Ensure the browser can reach the callback URL and try `--print` to paste the code manually.
            """);

        Assert.Contains("Q:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A: Ensure", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<dl>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<dt>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<dd>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_FaqQABoldStyle_RendersStrongInsteadOfLiteralAsterisks()
    {
        var html = BuildSinglePageSite(
            """
            # FAQ

            ## Auth

            **Q: `auth login` opens a URL but nothing happens.**  
            A: Ensure the browser can reach the callback URL and try `--print` to paste the code manually.

            **Q: I see `No OpenAI auth bundle found`.**  
            A: Run `intelligencex auth login` or `intelligencex auth export --format store-base64`.
            """);

        Assert.Contains("<strong>Q:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("* *Q", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("**Q:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_HeadingWithInlineCode_RendersCodeInsideHeading()
    {
        var html = BuildSinglePageSite(
            """
            # Tool Catalog

            ## Builtin Packs

            ### Event Log (`eventlog`)

            Representative tools.
            """);

        var headingMatch = System.Text.RegularExpressions.Regex.Match(
            html,
            "<h3[^>]*>(?<text>.*?)</h3>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

        Assert.True(headingMatch.Success, "Expected an <h3> heading in rendered HTML.");
        var headingText = headingMatch.Groups["text"].Value;

        Assert.Contains("<code", headingText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eventlog", headingText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`eventlog`", headingText, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSinglePageSite(string markdown)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-markdown-gfm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
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
                <head><title>{{TITLE}}</title></head>
                <body>{{CONTENT}}</body>
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
                Name = "Markdown GFM Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
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
