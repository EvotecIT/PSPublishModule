using PowerForge.Web;

public class WebSiteVerifierTests
{
    [Fact]
    public void Verify_WarnsOnMarkdownRawHtmlHygiene()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                <p><strong>Hello</strong> world</p>
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "content/docs",
                        Output = "/docs"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("Markdown hygiene", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
