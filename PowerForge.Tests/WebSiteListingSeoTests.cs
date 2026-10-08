using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteListingSeoTests
{
    private const string CustomSeoTitle = "Custom <journal> & \"quoted\"";
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Build_PaginationDistinguishesTitlesAndPreservesLandingTitle(bool template, bool titleOverride)
    {
        WithSite(template, titleOverride, output =>
        {
            var title = titleOverride ? CustomSeoTitle : template ? "Engineering journal | Example Site" : "Engineering journal";
            Assert.Equal(title, ReadTitle(output, "blog/index.html"));
            Assert.Equal(title + " (2/2)", ReadTitle(output, "blog/page/2/index.html"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_MatchingCategoryAndTagTermsHaveDistinctTitles(bool template)
    {
        WithSite(template, false, output =>
        {
            var suffix = template ? " | Example Site" : string.Empty;
            Assert.Equal("Security | Tags" + suffix, ReadTitle(output, "tags/security/index.html"));
            Assert.Equal("Security | Categories" + suffix, ReadTitle(output, "categories/security/index.html"));
            using var preview = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "_powerforge/seo-preview.json")));
            var tag = preview.RootElement.GetProperty("pages").EnumerateArray()
                .Single(page => page.GetProperty("outputPath").GetString()?.TrimEnd('/') == "/tags/security");
            Assert.Equal("Security", tag.GetProperty("title").GetString());
        });
    }

    [Theory]
    [InlineData("scriban")]
    [InlineData("simple")]
    public void Build_CustomThemeUsesEscapedResolvedTitleAndKeepsVisibleHeading(string engine)
    {
        WithSite(true, true, output =>
        {
            Assert.Equal(CustomSeoTitle + " (2/2)", ReadTitle(output, "blog/page/2/index.html"));
            var html = File.ReadAllText(Path.Combine(output, "blog/page/2/index.html"));
            Assert.Contains("<h1>Engineering journal</h1>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<journal>", html, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(html, "<title>"));
        }, engine);
    }

    private static void WithSite(bool template, bool titleOverride, Action<string> assertOutput, string? engine = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-listing-seo-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content/blog"));
            File.WriteAllText(Path.Combine(root, "content/blog/_index.md"),
                "---\ntitle: Engineering journal\n" + (titleOverride ? $"meta.seo_title: '{CustomSeoTitle}'\n" : string.Empty) + "---\nJournal.");
            for (var index = 1; index <= 2; index++)
                File.WriteAllText(Path.Combine(root, $"content/blog/post-{index}.md"),
                    $"---\ntitle: Post {index}\ndate: 2026-01-0{index}\ntags: [Security]\ncategories: [Security]\n---\nPost body.");
            var spec = new SiteSpec
            {
                Name = "Example Site",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = engine is null ? null : "listing",
                ThemesRoot = engine is null ? null : "themes",
                Seo = template ? new SeoSpec { Templates = new SeoTemplatesSpec { Title = "{title} | {site}" } } : null,
                Collections = new[] { new CollectionSpec { Name = "blog", Input = "content/blog", Output = "/blog", PageSize = 1, DefaultLayout = "page", ListLayout = "page" } },
                Taxonomies = new[] { new TaxonomySpec { Name = "tags", BasePath = "/tags" }, new TaxonomySpec { Name = "categories", BasePath = "/categories" } }
            };
            if (engine is not null)
            {
                var themeRoot = Path.Combine(root, "themes/listing");
                Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
                File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                    JsonSerializer.Serialize(new { name = "listing", engine, defaultLayout = "page" }));
                var heading = engine == "simple" ? "{{TITLE}}" : "{{ page.title }}";
                File.WriteAllText(Path.Combine(themeRoot, "layouts/page.html"),
                    "<!doctype html><html><head><title>{{ title_html }}</title></head><body><h1>" + heading + "</h1>{{CONTENT}}</body></html>");
            }
            var config = Path.Combine(root, "site.json");
            File.WriteAllText(config, "{}");
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, config), output);
            assertOutput(output);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string ReadTitle(string output, string route)
    {
        var html = File.ReadAllText(Path.Combine(output, route));
        return WebUtility.HtmlDecode(Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value);
    }
}
