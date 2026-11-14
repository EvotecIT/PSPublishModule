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
            var markdown = File.ReadAllText(Path.Combine(directory, "index" + extension)).Replace("\r\n", "\n", StringComparison.Ordinal);
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

    [Theory]
    [InlineData("Guide", true)]
    [InlineData("C#", true)]
    [InlineData("Guide", false)]
    public void BuildAndPrepare_PreserveSourceMarkdownAndThemeFacts_ThenRefreshOnBuild(string title, bool sourceHeading)
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

                LeadingExample();
                    IndentedContinuation();

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
        markdown = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("title: Guide", "title: " + title, StringComparison.Ordinal)
            .Replace("# Guide", "# " + title, StringComparison.Ordinal);
        if (!sourceHeading) markdown = markdown.Replace("# " + title + "\n\n", string.Empty, StringComparison.Ordinal);
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
            var htmlPath = Path.Combine(site.Output, "docs", "guide", "index.html");
            File.WriteAllText(htmlPath, File.ReadAllText(htmlPath).Replace("</head>", "<script>const optimized = true;</script></head>", StringComparison.Ordinal));
            for (var prepare = 0; prepare < 2; prepare++) WebAgentReadiness.Prepare(site.Options(".md"));
            var output = File.ReadAllText(Path.Combine(site.Output, "docs", "guide", "index.md")).Replace("\r\n", "\n");
            Assert.Single(Regex.Matches(output, "(?m)^# " + Regex.Escape(title) + "$"));
            Assert.Contains("\n\n    LeadingExample();\n        IndentedContinuation();", output);
            Assert.Contains("Source description", output);
            Assert.Contains("Theme package fact", output);
            Assert.Contains("| :--- | ---: |", output); // Authored alignment proves the source path was used.
            Assert.Contains("```csharp\nif (ready)\n{\n    // Preserve four spaces and blank lines.\n\n\n    ", output);
            Assert.Contains(build == 0 ? "Save();" : "SaveUpdated();", output);
            Assert.DoesNotContain("Sidebar", output);
        }

        // A later API/document generator may replace a scaffold page after the site build.
        File.WriteAllText(Path.Combine(site.Output, "docs", "guide", "index.html"),
            "<html><head><title>API suite</title></head><body><main><h1>API suite</h1><p>Generated API members</p></main></body></html>");
        WebAgentReadiness.Prepare(site.Options(".md"));
        var refreshed = File.ReadAllText(Path.Combine(site.Output, "docs", "guide", "index.md"));
        Assert.Contains("Generated API members", refreshed);
        Assert.DoesNotContain("LeadingExample", refreshed);
        Assert.DoesNotContain("Theme package fact", refreshed);
    }

    [Fact]
    public void Prepare_PreservesAllEntriesInArticleListings()
    {
        using var site = new DocumentationSite();
        File.WriteAllText(Path.Combine(site.Output, "index.html"), """
            <html><head><title>Examples</title></head><body><main><h1>Examples</h1><aside>Sidebar</aside>
            <section><article><h2>First example</h2><p>First details</p></article>
            <article><h2>Second example</h2><p>Second details</p></article></section></main></body></html>
            """);
        WebAgentReadiness.Prepare(site.Options(".md"));
        var markdown = File.ReadAllText(Path.Combine(site.Output, "index.md"));
        Assert.Contains("# Examples", markdown);
        Assert.Contains("## First example", markdown);
        Assert.Contains("First details", markdown);
        Assert.Contains("## Second example", markdown);
        Assert.Contains("Second details", markdown);
        Assert.DoesNotContain("Sidebar", markdown);
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

    [Theory]
    [InlineData("<aside><article><h1>Promo</h1></article></aside>")]
    [InlineData("<article data-pf-markdown='exclude'><h1>Promo</h1></article>")]
    public void Prepare_SelectsArticleRootAfterRemovingExcludedChrome(string chrome)
    {
        using var site = new DocumentationSite();
        File.WriteAllText(Path.Combine(site.Output, "index.html"),
            "<html><head><title>Guide | Docs</title></head><body><main>" + chrome +
            "<section><h1>Real guide</h1><p>Actual guide content</p></section></main></body></html>");
        WebAgentReadiness.Prepare(site.Options(".md"));
        var markdown = File.ReadAllText(Path.Combine(site.Output, "index.md"));
        Assert.StartsWith("# Real guide", markdown);
        Assert.Contains("Actual guide content", markdown);
        Assert.DoesNotContain("Promo", markdown);
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
