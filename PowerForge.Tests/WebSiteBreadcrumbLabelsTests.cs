using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSiteBreadcrumbLabelsTests
{
    [Theory]
    [InlineData("/#free-plus", true, null, "Example App")]
    [InlineData("/?plan=plus", true, null, "Example App")]
    [InlineData("/#free-plus", false, null, "Home")]
    [InlineData("/#free-plus", true, "Strona główna", "Strona główna")]
    public void Build_SectionAndQueryLinksDoNotNameTheirAncestorPage(
        string sectionUrl, bool homeInBrand, string? explicitHomeLabel, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-breadcrumb-label-" + Guid.NewGuid().ToString("N"));
        try
        {
            var content = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "support.md"), """
                ---
                title: Pomoc
                slug: support
                language: pl
                ---

                Pomoc dla użytkowników.
                """);
            var links = new List<MenuItemSpec> { new() { Title = "Free & Plus", Url = sectionUrl } };
            if (explicitHomeLabel is not null)
                links.Add(new MenuItemSpec { Title = explicitHomeLabel, Url = "/" });
            var site = new SiteSpec
            {
                Name = "Example App", BaseUrl = "https://product.example", ContentRoot = "content",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [new CollectionSpec { Name = "pages", Input = "content/pages", Output = "/" }],
                Localization = new LocalizationSpec { Enabled = true, DefaultLanguage = "en", Languages = [
                    new LanguageSpec { Code = "en", Default = true }, new LanguageSpec { Code = "pl" }
                ] },
                Navigation = new NavigationSpec { AutoDefaults = false, HomeLinkInBrand = homeInBrand,
                    Menus = [new MenuSpec { Name = "main", Items = links.ToArray() }] },
                StructuredData = new StructuredDataSpec { Enabled = true, Breadcrumbs = true }
            };
            var config = Path.Combine(root, "site.json");
            File.WriteAllText(config, "{}");
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(site, WebSitePlanner.Plan(site, config), output);
            var html = File.ReadAllText(Path.Combine(output, "pl", "support", "index.html"));
            var models = Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)
                .Select(match => JsonDocument.Parse(match.Groups[1].Value)).ToArray();
            try
            {
                var trail = Assert.Single(models, m => m.RootElement.GetProperty("@type").GetString() == "BreadcrumbList")
                    .RootElement.GetProperty("itemListElement");
                Assert.Equal(expected, trail[0].GetProperty("name").GetString());
                Assert.Equal("https://product.example/pl/", trail[0].GetProperty("item").GetString());
                Assert.Equal("Pomoc", trail[1].GetProperty("name").GetString());
                Assert.Equal("https://product.example/pl/support/", trail[1].GetProperty("item").GetString());
            }
            finally
            {
                foreach (var model in models) model.Dispose();
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
