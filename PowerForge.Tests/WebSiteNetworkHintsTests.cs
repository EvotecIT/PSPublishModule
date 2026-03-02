using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteNetworkHintsTests
{
    [Fact]
    public void Build_RemovesUnusedConfiguredNetworkHints()
    {
        var root = CreateTempRoot("pf-web-network-hints-trim-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                ---

                ![Badge](https://img.shields.io/badge/tests-pass-brightgreen)
                """);

            var spec = BuildPagesSpec();
            spec.Head = new HeadSpec
            {
                Links = new[]
                {
                    new HeadLinkSpec { Rel = "preconnect", Href = "https://img.shields.io", Crossorigin = "anonymous" },
                    new HeadLinkSpec { Rel = "dns-prefetch", Href = "https://img.shields.io" },
                    new HeadLinkSpec { Rel = "preconnect", Href = "https://cdn.jsdelivr.net", Crossorigin = "anonymous" },
                    new HeadLinkSpec { Rel = "dns-prefetch", Href = "https://cdn.jsdelivr.net" }
                }
            };

            var html = BuildAndRead(root, spec, "index.html");
            Assert.Contains("<link rel=\"preconnect\" href=\"https://img.shields.io\"", html, StringComparison.Ordinal);
            Assert.Contains("<link rel=\"dns-prefetch\" href=\"https://img.shields.io\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<link rel=\"preconnect\" href=\"https://cdn.jsdelivr.net\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<link rel=\"dns-prefetch\" href=\"https://cdn.jsdelivr.net\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_AddsMissingNetworkHintForExternalAssetsWithoutExistingLinkTags()
    {
        var root = CreateTempRoot("pf-web-network-hints-add-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                meta.head_html: <script src="https://cdn.jsdelivr.net/npm/prismjs@1.29.0/prism.min.js" defer></script>
                ---

                Home body.
                """);

            var spec = BuildPagesSpec();
            var html = BuildAndRead(root, spec, "index.html");
            Assert.Contains("https://cdn.jsdelivr.net/npm/prismjs@1.29.0/prism.min.js", html, StringComparison.Ordinal);
            Assert.Contains("<link rel=\"preconnect\" href=\"https://cdn.jsdelivr.net\" crossorigin=\"anonymous\" />", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_DoesNotAddNetworkHintForAlternateLinksOnOwnOrigin()
    {
        var root = CreateTempRoot("pf-web-network-hints-alternate-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                meta.head_html: <link rel="alternate" type="application/rss+xml" href="https://example.test/index.xml" />
                ---

                Home body.
                """);

            var html = BuildAndRead(root, BuildPagesSpec(), "index.html");
            Assert.Contains("<link rel=\"alternate\" type=\"application/rss+xml\" href=\"https://example.test/index.xml\" />", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<link rel=\"preconnect\" href=\"https://example.test\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_DoesNotInjectHintsWhenNoExternalAssetsExist()
    {
        var root = CreateTempRoot("pf-web-network-hints-noop-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                ---

                Local content only.
                """);

            var html = BuildAndRead(root, BuildPagesSpec(), "index.html");
            Assert.DoesNotContain("rel=\"preconnect\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("rel=\"dns-prefetch\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_AddsGStaticHintWhenGoogleFontsStylesheetIsUsed()
    {
        var root = CreateTempRoot("pf-web-network-hints-fonts-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                meta.head_html: <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Outfit:wght@400;700&display=swap" />
                ---

                Home body.
                """);

            var html = BuildAndRead(root, BuildPagesSpec(), "index.html");
            Assert.Contains("<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\" crossorigin=\"anonymous\" />", html, StringComparison.Ordinal);
            Assert.Contains("<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin=\"anonymous\" />", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_SupportsProtocolRelativeAssetUrls()
    {
        var root = CreateTempRoot("pf-web-network-hints-protocol-relative-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                meta.head_html: <script src="//cdn.jsdelivr.net/npm/prismjs@1.29.0/prism.min.js" defer></script>
                ---

                Home body.
                """);

            var html = BuildAndRead(root, BuildPagesSpec(), "index.html");
            Assert.Contains("<link rel=\"preconnect\" href=\"https://cdn.jsdelivr.net\" crossorigin=\"anonymous\" />", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_RespectsMaxPreconnectHintCap()
    {
        var root = CreateTempRoot("pf-web-network-hints-cap-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                meta.head_html: <script src="https://cdn1.example.test/a.js"></script><script src="https://cdn2.example.test/b.js"></script><script src="https://cdn3.example.test/c.js"></script><script src="https://cdn4.example.test/d.js"></script><script src="https://cdn5.example.test/e.js"></script>
                ---

                Home body.
                """);

            var html = BuildAndRead(root, BuildPagesSpec(), "index.html");
            Assert.Equal(4, CountOccurrences(html, "rel=\"preconnect\""));
            Assert.DoesNotContain("<link rel=\"preconnect\" href=\"https://cdn5.example.test\" crossorigin=\"anonymous\" />", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_CollectsOriginsFromMultipleSrcSetCandidates()
    {
        var root = CreateTempRoot("pf-web-network-hints-srcset-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                ---

                <img src="https://img-one.example.test/hero-320.jpg" srcset="https://img-two.example.test/hero-640.jpg 640w, https://img-three.example.test/hero-1280.jpg 1280w" alt="Hero" />
                """);

            var html = BuildAndRead(root, BuildPagesSpec(), "index.html");
            Assert.Contains("<link rel=\"preconnect\" href=\"https://img-one.example.test\" crossorigin=\"anonymous\" />", html, StringComparison.Ordinal);
            Assert.Contains("<link rel=\"preconnect\" href=\"https://img-two.example.test\" crossorigin=\"anonymous\" />", html, StringComparison.Ordinal);
            Assert.Contains("<link rel=\"preconnect\" href=\"https://img-three.example.test\" crossorigin=\"anonymous\" />", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_IgnoresCommentedOutHintTags()
    {
        var root = CreateTempRoot("pf-web-network-hints-comment-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                meta.head_html: <!-- <link rel="preconnect" href="https://unused.example.test" crossorigin="anonymous" /> --><script src="https://cdn.jsdelivr.net/npm/prismjs@1.29.0/prism.min.js"></script>
                ---

                Home body.
                """);

            var html = BuildAndRead(root, BuildPagesSpec(), "index.html");
            Assert.Contains("<!-- <link rel=\"preconnect\" href=\"https://unused.example.test\" crossorigin=\"anonymous\" /> -->", html, StringComparison.Ordinal);
            Assert.Contains("<link rel=\"preconnect\" href=\"https://cdn.jsdelivr.net\" crossorigin=\"anonymous\" />", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<link rel=\"preconnect\" href=\"https://unused.example.test\" crossorigin=\"anonymous\" /></head>", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static SiteSpec BuildPagesSpec()
    {
        return new SiteSpec
        {
            Name = "Example Site",
            BaseUrl = "https://example.test",
            ContentRoot = "content",
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
    }

    private static string BuildAndRead(string root, SiteSpec spec, string relativeOutputFile)
    {
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        var plan = WebSitePlanner.Plan(spec, configPath);
        var outPath = Path.Combine(root, "_site");
        WebSiteBuilder.Build(spec, plan, outPath);
        return File.ReadAllText(Path.Combine(outPath, relativeOutputFile));
    }

    private static void WritePage(string root, string fileName, string markdown)
    {
        var pagesPath = Path.Combine(root, "content", "pages");
        Directory.CreateDirectory(pagesPath);
        File.WriteAllText(Path.Combine(pagesPath, fileName), markdown);
    }

    private static string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
            return 0;

        var count = 0;
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
                break;
            count++;
            start = index + value.Length;
        }

        return count;
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
