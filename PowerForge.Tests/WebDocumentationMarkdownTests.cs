using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebDocumentationMarkdownTests
{
    [Theory]
    [InlineData(".md")]
    [InlineData(".markdown")]
    public void Prepare_ExtractsArticleAndAdvertisesEachCopyAcrossReruns(string extension)
    {
        using var site = new DocumentationSite();
        var directory = Path.Combine(site.Output, "docs", "guide");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "index.html"), """
            <html><head><title>Guide | Library Docs</title><meta name="description" content="Guide description"></head>
            <body><main><aside>Sidebar navigation</aside><article>
            <header><h1>Guide</h1><p>Guide description</p><a href="https://github.com/example/docs/edit/guide.md">Edit on GitHub</a>
            <section class="docs-facts"><dl><dt>Package</dt><dd>Example.Library</dd></dl></section></header>
            <table><thead><tr><th>Class</th><th>Description</th></tr></thead><tbody><tr><td><code>Document</code></td><td>Root class</td></tr></tbody></table>
            <pre><code class="language-csharp">if (ready)
            {
                Write();


                Save();
            }</code></pre></article></main><footer>Footer</footer></body></html>
            """);
        var options = site.Options(extension);
        for (var run = 0; run < 2; run++)
        {
            WebAgentReadiness.Prepare(options);
            var markdown = File.ReadAllText(Path.Combine(directory, "index" + extension));
            Assert.StartsWith("# Guide\n\nGuide description", markdown.Replace("\r\n", "\n"));
            Assert.Single(Regex.Matches(markdown, "(?m)^# Guide$"));
            Assert.Contains("**Package**", markdown);
            Assert.Contains("Example.Library", markdown);
            Assert.Contains("| Class | Description |", markdown);
            Assert.Contains("| --- | --- |", markdown);
            Assert.Contains("| `Document` | Root class |", markdown);
            Assert.Contains("```csharp\nif (ready)\n{\n    Write();\n\n\n    Save();\n}\n```", markdown.Replace("\r\n", "\n"));
            Assert.DoesNotContain("Sidebar navigation", markdown);
            Assert.DoesNotContain("Edit on GitHub", markdown);
            Assert.DoesNotContain("Footer", markdown);
            var html = File.ReadAllText(Path.Combine(directory, "index.html"));
            Assert.Single(Regex.Matches(html, "rel=\"alternate\" type=\"text/markdown\""));
            Assert.Contains("href=\"/docs/guide/index" + extension + "\"", html);
            var headers = File.ReadAllText(Path.Combine(site.Output, "_headers")).Replace("\r\n", "\n");
            Assert.Contains("/docs/guide/\n  Link: </docs/guide/index" + extension + ">; rel=\"alternate\"; type=\"text/markdown\"", headers);
            Assert.Contains("/docs/guide/index.html\n  Link:", headers);
        }
    }

    [Fact]
    public void BuildAndPrepare_PreserveSourceMarkdownAndThemeFacts_ThenRefreshOnBuild()
    {
        using var site = new DocumentationSite();
        var pages = Path.Combine(site.Root, "content", "pages");
        var layouts = Path.Combine(site.Root, "themes", "docs", "layouts");
        Directory.CreateDirectory(pages);
        Directory.CreateDirectory(layouts);
        File.WriteAllText(Path.Combine(layouts, "base.html"), """
            <html><head>{{ title_html }}{{ description_meta_html }}</head><body><main><aside>Sidebar</aside><article>
            <header><h1>{{ page.title }}</h1><p>{{ page.description }}</p><section class="docs-facts"><p>Theme package fact</p></section></header>
            {{ content }}
            </article></main></body></html>
            """);
        var markdown = """
            ---
            title: Guide
            description: Source description
            slug: guide
            ---
            # Guide

            | Class | Description |
            | :--- | ---: |
            | `Document` | Root class |

            ```csharp
            if (ready)
            {
                // Preserve four spaces and blank lines.


                Save();
            }
            ```
            """;
        var sourcePath = Path.Combine(pages, "guide.md");
        File.WriteAllText(sourcePath, markdown);
        var spec = new SiteSpec
        {
            Name = "Docs", BaseUrl = "https://example.test", DefaultTheme = "docs", ThemeEngine = "scriban",
            ContentRoot = "content", Collections = [new CollectionSpec { Name = "pages", Input = "content/pages", Output = "/docs/" }],
            AgentReadiness = site.Options(".md").AgentReadiness
        };
        var config = Path.Combine(site.Root, "site.json");
        File.WriteAllText(config, "{}");
        for (var build = 0; build < 2; build++)
        {
            if (build == 1) File.WriteAllText(sourcePath, markdown.Replace("Save();", "SaveUpdated();", StringComparison.Ordinal));
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, config), site.Output);
            for (var prepare = 0; prepare < 2; prepare++) WebAgentReadiness.Prepare(site.Options(".md"));
            var output = File.ReadAllText(Path.Combine(site.Output, "docs", "guide", "index.md")).Replace("\r\n", "\n");
            Assert.Single(Regex.Matches(output, "(?m)^# Guide$"));
            Assert.Contains("Source description", output);
            Assert.Contains("Theme package fact", output);
            Assert.Contains("| :--- | ---: |", output); // Authored alignment proves the source path was used.
            Assert.Contains("```csharp\nif (ready)\n{\n    // Preserve four spaces and blank lines.\n\n\n    ", output);
            Assert.Contains(build == 0 ? "Save();" : "SaveUpdated();", output);
            Assert.DoesNotContain("Sidebar", output);
        }
    }

    [Fact]
    public void Prepare_AdvertisesOnlyArtifactsSelectedByLimit_AndHonorsLinkHeadersSwitch()
    {
        using var site = new DocumentationSite();
        foreach (var name in new[] { "first", "second" })
            File.WriteAllText(Path.Combine(site.Output, name + ".html"), "<html><head><title>Guide</title></head><body><article><p>Content</p></article></body></html>");
        var options = site.Options(".md");
        options.AgentReadiness!.MarkdownArtifacts!.MaxPages = 1;
        options.AgentReadiness.LinkHeaders = false;
        WebAgentReadiness.Prepare(options);
        Assert.True(File.Exists(Path.Combine(site.Output, "first.md")));
        Assert.False(File.Exists(Path.Combine(site.Output, "second.md")));
        Assert.Contains("text/markdown", File.ReadAllText(Path.Combine(site.Output, "first.html")));
        Assert.DoesNotContain("text/markdown", File.ReadAllText(Path.Combine(site.Output, "second.html")));
        Assert.DoesNotContain("Link:", File.ReadAllText(Path.Combine(site.Output, "_headers")));
    }

    private sealed class DocumentationSite : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "pf-doc-markdown-" + Guid.NewGuid().ToString("N"));
        internal string Output => Path.Combine(Root, "_site");
        internal DocumentationSite() => Directory.CreateDirectory(Output);
        internal WebAgentReadinessPrepareOptions Options(string extension) => new()
        {
            SiteRoot = Output, BaseUrl = "https://example.test",
            AgentReadiness = new AgentReadinessSpec { Enabled = true, MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = true, Extension = extension } }
        };
        public void Dispose() => Directory.Delete(Root, true);
    }
}
