using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSiteTemplateExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-template-export-" + Guid.NewGuid().ToString("N"));
    private readonly SiteSpec _spec;
    private readonly string _partial;
    private string Output => Path.Combine(_root, "_site");
    private string Fragment => Path.Combine(Output, "_powerforge", "fragments", "docs-shell.html");

    public WebSiteTemplateExportTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "pages"));
        File.WriteAllText(Path.Combine(_root, "content", "pages", "guide.md"), "---\ntitle: Guide\nlayout: page\n---\n\nGuide content.");
        var theme = Path.Combine(_root, "themes", "test");
        Directory.CreateDirectory(Path.Combine(theme, "layouts"));
        Directory.CreateDirectory(Path.Combine(theme, "partials"));
        File.WriteAllText(Path.Combine(theme, "theme.json"), """{"name":"test","engine":"scriban","defaultLayout":"page"}""");
        File.WriteAllText(Path.Combine(theme, "layouts", "page.html"), """{{ include "shared" }}<main>{{ content }}</main>""");
        File.WriteAllText(Path.Combine(theme, "layouts", "shell.html"), """{{ include "shared" }}<aside>{{ page.title }} {{ current_path }}</aside>""");
        _partial = Path.Combine(theme, "partials", "shared.html");
        File.WriteAllText(_partial, "<header>Shared navigation</header>");
        File.WriteAllText(Path.Combine(_root, "site.json"), "{}");
        _spec = new SiteSpec
        {
            Name = "Export test", BaseUrl = "https://example.test", DefaultTheme = "test", ThemesRoot = "themes",
            Collections = [new CollectionSpec { Name = "pages", Input = "content/pages", Output = "/" }],
            TemplateExports = [new TemplateExportSpec { Name = "docs-shell", SourceRoute = "/guide/", Layout = "shell" }]
        };
    }

    [Fact]
    public void Build_ExportsTheSharedThemeWithoutCreatingSearchOrSitemapPages()
    {
        Build();
        Assert.Equal("<header>Shared navigation</header><aside>Guide /guide</aside>", File.ReadAllText(Fragment).Trim());
        var pageHtml = File.ReadAllText(Path.Combine(Output, "guide", "index.html"));
        Assert.Contains("<main>", pageHtml);
        Assert.Contains("Guide content.</p>", pageHtml);
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(Output, "search", "index.json")));
        Assert.Single(index.RootElement.EnumerateArray());
        Assert.DoesNotContain("docs-shell", File.ReadAllText(Path.Combine(Output, "_powerforge", "sitemap-entries.json")));

        var unchanged = Build();
        Assert.DoesNotContain("_powerforge/fragments/docs-shell.html", unchanged.UpdatedFiles);
        File.WriteAllText(_partial, "<header>Updated navigation</header>");
        Build();
        Assert.Contains("Updated navigation", File.ReadAllText(Fragment));
        Assert.Contains("Updated navigation", File.ReadAllText(Path.Combine(Output, "guide", "index.html")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("nested/file")]
    [InlineData("UpperCase")]
    [InlineData("")]
    public void Build_RejectsNamesThatAreNotPortableArtifactNames(string name)
    {
        _spec.TemplateExports[0].Name = name;
        Assert.Throws<InvalidOperationException>(() => Build());
    }

    [Fact]
    public void Build_RejectsUnknownSourceAndMissingLayoutInsteadOfRenderingFallbackContent()
    {
        _spec.TemplateExports[0].SourceRoute = "/missing/";
        Assert.Throws<InvalidOperationException>(() => Build());
        _spec.TemplateExports[0].SourceRoute = "/guide/";
        _spec.TemplateExports[0].Layout = "missing-layout";
        Assert.Throws<InvalidOperationException>(() => Build());
    }

    [Fact]
    public void ExportedFragments_AreNotPublicPagesInSitemapsOrAudits()
    {
        Build();
        var sitemap = WebSitemapGenerator.Generate(new WebSitemapOptions {
            SiteRoot = Output, BaseUrl = _spec.BaseUrl, IncludeTextFiles = false
        });
        var xml = System.Xml.Linq.XDocument.Load(sitemap.OutputPath);
        var routes = xml.Descendants(System.Xml.Linq.XName.Get("loc", "http://www.sitemaps.org/schemas/sitemap/0.9"))
            .Select(element => element.Value).ToArray();
        Assert.Contains("https://example.test/guide/", routes);
        Assert.DoesNotContain(routes, route => route.Contains("/fragments/", StringComparison.Ordinal));
        Assert.Equal(1, WebSeoDoctor.Analyze(new WebSeoDoctorOptions { SiteRoot = Output }).PageCount);
        Assert.Equal(1, WebSiteAuditor.Audit(new WebAuditOptions { SiteRoot = Output }).PageCount);
    }

    private WebBuildResult Build() => WebSiteBuilder.Build(_spec, WebSitePlanner.Plan(_spec, Path.Combine(_root, "site.json")), Output);
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
