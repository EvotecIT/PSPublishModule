using PowerForge.Web;

namespace PowerForge.Tests;

public class WebMediaViewerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_PublishesViewerAssetsOnlyWhenEnabled(bool enabled)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-media-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content"));
            File.WriteAllText(Path.Combine(root, "content", "sample.md"), "---\ntitle: Sample\nslug: gallery/sample\n---\n<a data-pf-media href=\"/sample.png\"><img src=\"/sample.png\" alt=\"Sample\"></a>");
            var spec = new SiteSpec
            {
                Name = "Media", BaseUrl = "https://example.test", ContentRoot = "content",
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                MediaViewer = new MediaViewerSpec { Enabled = enabled, Selector = "a[data-pf-media=\"gallery\"]" }
            };
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, Path.Combine(root, "site.json")), output);
            var html = File.ReadAllText(Path.Combine(output, "gallery", "sample", "index.html"));
            Assert.Equal(enabled, html.Contains("media-viewer.v1.js"));
            Assert.Equal(enabled, File.Exists(Path.Combine(output, "assets", "powerforge", "media-viewer.v1.js")));
            Assert.Equal(enabled, File.Exists(Path.Combine(output, "assets", "powerforge", "media-viewer.v1.css")));
            Assert.Contains("href=\"/sample.png\"", html);
            if (enabled) Assert.Contains("data-pf-media-selector=\"a[data-pf-media=&quot;gallery&quot;]\"", html);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("https://example.test/")]
    [InlineData("https://example.test/repository/")]
    public void Build_ViewerAssetsResolveWithinHostingRoot(string hostingRoot)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-media-route-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content"));
            File.WriteAllText(Path.Combine(root, "content", "index.md"), "---\ntitle: Home\n---\nHome");
            File.WriteAllText(Path.Combine(root, "content", "sample.md"), "---\ntitle: Sample\nslug: gallery/sample\n---\nSample");
            var spec = new SiteSpec
            {
                Name = "Media", BaseUrl = hostingRoot, ContentRoot = "content",
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                MediaViewer = new MediaViewerSpec { Enabled = true }
            };
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, Path.Combine(root, "site.json")), output);
            foreach (var route in new[] { "", "gallery/sample/" })
            {
                var html = File.ReadAllText(Path.Combine(output, route, "index.html"));
                foreach (var extension in new[] { "css", "js" })
                {
                    var match = System.Text.RegularExpressions.Regex.Match(html,
                        "(?:href|src)=\"([^\"]*media-viewer\\.v1\\." + extension + ")\"");
                    Assert.True(match.Success, html);
                    var resolved = new Uri(new Uri(hostingRoot + route), match.Groups[1].Value);
                    Assert.Equal(new Uri(hostingRoot + "assets/powerforge/media-viewer.v1." + extension), resolved);
                    Assert.True(File.Exists(Path.Combine(output, "assets", "powerforge", "media-viewer.v1." + extension)));
                }
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Build_LanguageAtRootRebasesEveryMediaSourceAndPreservesExternalSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-media-language-" + Guid.NewGuid().ToString("N"));
        try
        {
            var content = Path.Combine(root, "content", "docs", "pl");
            Directory.CreateDirectory(content);
            var attributes = new[] { "src", "light", "dark", "mobile", "desktop" };
            var markup = string.Join("\n", attributes.Select(attribute =>
                $"<a data-pf-media-{attribute}=\"/pl/docs/{attribute}.png\" href=\"/pl/docs/image.png\"><img src=\"/pl/docs/image.png\" alt=\"Sample\"></a>"));
            markup += "\n<a data-pf-media-dark=\"https://cdn.test/pl/dark.png\">External</a>";
            markup += "\n<a data-pf-media-mobile=\"../mobile.png\" data-pf-media-desktop=\"//cdn.test/pl/desktop.png\" href=\"?view=1\">Relative</a>";
            foreach (var attribute in attributes.Select(value => "data-pf-media-" + value).Concat(new[] { "href", "src", "data-local-href" }))
                markup += $"\n<a {attribute}=\"/pl/pl/image.png\">Nested language directory</a>";
            File.WriteAllText(Path.Combine(content, "index.md"), "---\ntitle: Media PL\n---\n" + markup);
            var spec = new SiteSpec
            {
                Name = "Media", BaseUrl = "https://example.test", ContentRoot = "content",
                Collections = [new CollectionSpec { Name = "docs", Input = "content/docs", Output = "/docs" }],
                MediaViewer = new MediaViewerSpec { Enabled = true },
                Localization = new LocalizationSpec
                {
                    Enabled = true, DefaultLanguage = "en", DetectFromPath = true,
                    Languages = [new LanguageSpec { Code = "en", Default = true }, new LanguageSpec { Code = "pl", RenderAtRoot = true }]
                }
            };
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, Path.Combine(root, "site.json")), output,
                language: "pl", languageAsRoot: true);
            var html = File.ReadAllText(Path.Combine(output, "docs", "index.html"));
            foreach (var attribute in attributes)
                Assert.Contains($"data-pf-media-{attribute}=\"/docs/{attribute}.png\"", html);
            Assert.Contains("data-pf-media-dark=\"https://cdn.test/pl/dark.png\"", html);
            Assert.Contains("data-pf-media-mobile=\"../mobile.png\"", html);
            Assert.Contains("data-pf-media-desktop=\"//cdn.test/pl/desktop.png\"", html);
            Assert.Contains("href=\"?view=1\"", html);
            foreach (var attribute in attributes.Select(value => "data-pf-media-" + value).Concat(new[] { "href", "src", "data-local-href" }))
                Assert.Contains($"{attribute}=\"/pl/image.png\"", html);
            Assert.Contains("src=\"../assets/powerforge/media-viewer.v1.js\"", html);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
