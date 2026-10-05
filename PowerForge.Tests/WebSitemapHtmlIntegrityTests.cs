using AngleSharp.Html.Parser;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSitemapHtmlIntegrityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenerateHtml_PreservesEveryEntryBeyondTemplateStringLimit(bool customTemplate)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-large-html-sitemap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const int count = 6151;
            var entries = Enumerable.Range(0, count).Select(i => new WebSitemapEntry
            {
                Path = $"/api/type-{i:D5}/",
                Title = $"Reference type {i:D5} with a sufficiently descriptive documentation title",
                Description = new string('x', 120)
            }).ToArray();
            var templatePath = Path.Combine(root, "custom.sbn");
            if (customTemplate)
                File.WriteAllText(templatePath, "{{ capture body }}{{ for entry in entries }}<a href=\"{{ entry.url }}\">{{ entry.title }} {{ entry.description }}</a>{{ end }}{{ end }}{{ body }}");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                Entries = entries,
                IncludeHtmlFiles = false,
                IncludeTextFiles = false,
                GenerateHtml = true,
                HtmlTemplatePath = customTemplate ? templatePath : null
            });
            using var document = new HtmlParser().ParseDocument(File.ReadAllText(result.HtmlOutputPath!));
            var hrefs = document.QuerySelectorAll("a[href]").Select(a => a.GetAttribute("href")).ToArray();
            Assert.Equal(count, hrefs.Length);
            Assert.Equal(entries.Select(e => "https://example.test" + e.Path).Order(), hrefs.Order());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GenerateDefaultHtml_EncodesEntryTextAndAttributes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-html-sitemap-encoding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string title = "API <Generic> & \"reference\"";
            const string description = "<img src=x onerror=alert(1)> & documentation";
            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                Entries = new[] { new WebSitemapEntry { Path = "/api/generic/", Title = title, Section = "API <reference>", Description = description } },
                IncludeHtmlFiles = false,
                IncludeTextFiles = false,
                HtmlTitle = title,
                HtmlCssHref = "/css/site.css?theme=light&revision=2",
                GenerateHtml = true
            });
            using var document = new HtmlParser().ParseDocument(File.ReadAllText(result.HtmlOutputPath!));
            Assert.Equal(title, document.Title);
            Assert.Equal(title, document.QuerySelector("h1")!.TextContent);
            Assert.Equal(title, document.QuerySelector(".pf-sitemap-item a")!.TextContent);
            Assert.Equal("API <reference>", document.QuerySelector("h2")!.TextContent);
            Assert.Equal(description, document.QuerySelector(".pf-sitemap-desc")!.TextContent);
            Assert.Empty(document.QuerySelectorAll("img, script"));
            Assert.Equal("/css/site.css?theme=light&revision=2", document.QuerySelector("link[rel=stylesheet]")!.GetAttribute("href"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("{{ 'x' | string.pad_right 33554433 }}")]
    [InlineData("{{ for i in 1..33 }}{{ 'x' | string.pad_right 1048576 }}{{ end }}")]
    public void GenerateCustomHtml_RejectsExcessiveAllocationAndOutputWithoutReplacingExistingHtml(string template)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-html-sitemap-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var templatePath = Path.Combine(root, "custom.sbn");
            var htmlPath = Path.Combine(root, "sitemap.html");
            File.WriteAllText(templatePath, template);
            File.WriteAllText(htmlPath, "previous complete sitemap");

            Assert.Throws<Scriban.Syntax.ScriptRuntimeException>(() => WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeHtmlFiles = false,
                IncludeTextFiles = false,
                GenerateHtml = true,
                HtmlTemplatePath = templatePath,
                HtmlOutputPath = htmlPath
            }));
            Assert.Equal("previous complete sitemap", File.ReadAllText(htmlPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
