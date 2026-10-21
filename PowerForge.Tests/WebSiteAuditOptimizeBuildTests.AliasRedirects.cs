using PowerForge.Web;
using System.Text.Json;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void Build_ExpandsAliasRedirects_ForSlashVariants()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-alias-redirect-variants-" + Guid.NewGuid().ToString("N"));
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

                Blog
                """);
            File.WriteAllText(Path.Combine(blogPath, "new-post.md"),
                """
                ---
                title: New Post
                aliases: [blog/legacy-post]
                ---

                Content
                """);

            var themeRoot = Path.Combine(root, "themes", "alias-redirect-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
                """
                <!doctype html>
                <html><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "alias-redirect-test",
                  "engine": "scriban",
                  "defaultLayout": "home"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Alias Redirect Variant Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "alias-redirect-test",
                ThemesRoot = "themes",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        DefaultLayout = "home"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var metadataPath = Path.Combine(result.OutputPath, "_powerforge", "redirects.json");
            var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var redirects = metadata.RootElement.GetProperty("redirects")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("from").GetString())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.Contains("/blog/legacy-post", redirects, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("/blog/legacy-post/", redirects, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_SkipsAliasRedirects_ThatResolveToCanonicalRoute()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-self-alias-redirects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "allianz.md"),
                """
                ---
                title: Allianz
                slug: allianz
                aliases:
                  - /allianz/
                  - /allianz/#overview
                  - /?page_id=8328
                ---

                Allianz
                """);

            var themeRoot = Path.Combine(root, "themes", "self-alias-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "page.html"),
                """
                <!doctype html>
                <html><body>{{ content }}</body></html>
                """);

            var spec = new SiteSpec
            {
                Name = "Self Alias Redirect Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "self-alias-test",
                ThemesRoot = "themes",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/",
                        DefaultLayout = "page"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var metadataPath = Path.Combine(result.OutputPath, "_powerforge", "redirects.json");
            using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var redirects = metadata.RootElement.GetProperty("redirects")
                .EnumerateArray()
                .Select(static entry => new
                {
                    From = entry.GetProperty("from").GetString(),
                    To = entry.GetProperty("to").GetString()
                })
                .ToArray();

            Assert.Single(redirects);
            Assert.DoesNotContain(redirects, static entry =>
                string.Equals(entry.From, "/allianz", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.From, "/allianz/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(redirects, static entry =>
                string.Equals(entry.From, "/?page_id=8328", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.To, "/allianz", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("/legacy-post/?id=8328&tag=a%2Fb%23c#overview")]
    [InlineData("http://legacy.example.test/legacy-post/?id=8328&tag=a%2Fb%23c#overview")]
    [InlineData("https://legacy.example.test/legacy-post/?id=8328&tag=a%2Fb%23c#overview")]
    [InlineData("//legacy.example.test/legacy-post/?id=8328&tag=a%2Fb%23c#overview")]
    public void Build_AliasRedirects_PreserveQueryAndExcludeSourceFragment(string alias)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-query-alias-redirects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "current.md"),
                $"""
                ---
                title: Current
                slug: current
                aliases: ["{alias}"]
                ---

                Current
                """);

            var themeRoot = Path.Combine(root, "themes", "query-alias-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "page.html"),
                "<!doctype html><html><body>{{ content }}</body></html>");

            var spec = new SiteSpec
            {
                Name = "Query Alias Redirect Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "query-alias-test",
                ThemesRoot = "themes",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/",
                        DefaultLayout = "page"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var result = WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, configPath), Path.Combine(root, "_site"));

            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.OutputPath, "_powerforge", "redirects.json")));
            var redirect = Assert.Single(metadata.RootElement.GetProperty("redirects").EnumerateArray());
            Assert.Equal("/legacy-post/?id=8328&tag=a%2Fb%23c", redirect.GetProperty("from").GetString());
            Assert.Equal("/current", redirect.GetProperty("to").GetString());
            Assert.Contains("/legacy-post/?id=8328&tag=a%2Fb%23c /current 301", File.ReadAllText(Path.Combine(result.OutputPath, "_redirects")), StringComparison.Ordinal);

            var apache = File.ReadAllText(Path.Combine(result.OutputPath, ".htaccess"));
            Assert.Contains("RewriteCond %{QUERY_STRING} ^id=8328&tag=a%2Fb%23c$", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^legacy-post/?$ /current [R=301,L,QSD]", apache, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
