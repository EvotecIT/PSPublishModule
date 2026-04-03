using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteSocialCardsTests
{
    [Fact]
    public void Build_EmitsSocialMetaByDefault_WhenSiteSocialEnabled()
    {
        var root = CreateTempRoot("pf-web-social-default-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                description: Home page description.
                slug: index
                ---

                Welcome to our site.
                """);

            var spec = BuildPagesSpec();
            spec.Social = new SocialSpec
            {
                Enabled = true,
                SiteName = "Example Site",
                Image = "/assets/social/card.png",
                ImageWidth = 1200,
                ImageHeight = 630,
                TwitterCard = "summary_large_image",
                TwitterSite = "evotecit",
                TwitterCreator = "@exampleAuthor"
            };

            var html = BuildAndRead(root, spec, "index.html");
            Assert.Contains("property=\"og:title\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:description\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:type\" content=\"website\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:image\" content=\"https://example.test/assets/social/card.png\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:image:alt\" content=\"Home\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:image:width\" content=\"1200\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:image:height\" content=\"630\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"twitter:card\" content=\"summary_large_image\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"twitter:site\" content=\"@evotecit\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"twitter:creator\" content=\"@exampleAuthor\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"twitter:image\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"twitter:image:alt\" content=\"Home\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_DoesNotEmitSocialMeta_WhenPageOptsOut()
    {
        var root = CreateTempRoot("pf-web-social-optout-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                meta.social: false
                ---

                Welcome to our site.
                """);

            var spec = BuildPagesSpec();
            spec.Social = new SocialSpec
            {
                Enabled = true,
                SiteName = "Example Site",
                Image = "/assets/social/card.png"
            };

            var html = BuildAndRead(root, spec, "index.html");
            Assert.DoesNotContain("property=\"og:title\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"twitter:card\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_EmitsArticleMeta_ForBlogPages()
    {
        var root = CreateTempRoot("pf-web-social-blog-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "release-notes.md"),
                """
                ---
                title: Release Notes
                slug: release-notes
                date: 2026-02-17
                tags:
                  - Release
                  - Platform
                ---

                Ship notes and changes.
                """);

            var spec = BuildPagesSpec();
            spec.Social = new SocialSpec
            {
                Enabled = true,
                SiteName = "Example Site",
                Image = "/assets/social/card.png"
            };
            spec.Collections = new[]
            {
                new CollectionSpec
                {
                    Name = "pages",
                    Input = "content/pages",
                    Output = "/"
                },
                new CollectionSpec
                {
                    Name = "blog",
                    Input = "content/blog",
                    Output = "/blog"
                }
            };

            var html = BuildAndRead(root, spec, Path.Combine("blog", "release-notes", "index.html"));
            Assert.Contains("property=\"og:type\" content=\"article\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"article:published_time\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"article:tag\" content=\"Release\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_GeneratesSocialCardImage_WhenAutoGenerateCardsEnabled()
    {
        var root = CreateTempRoot("pf-web-social-generated-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                description: Social card generation test.
                slug: index
                ---

                Welcome to our site.
                """);

            var spec = BuildPagesSpec();
            spec.Social = new SocialSpec
            {
                Enabled = true,
                SiteName = "Example Site",
                AutoGenerateCards = true,
                GeneratedCardsPath = "/assets/social/generated",
                GeneratedCardWidth = 1200,
                GeneratedCardHeight = 630
            };

            var html = BuildAndRead(root, spec, "index.html");
            const string marker = "property=\"og:image\" content=\"";
            var start = html.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, "Expected og:image meta tag.");

            var valueStart = start + marker.Length;
            var valueEnd = html.IndexOf('"', valueStart);
            Assert.True(valueEnd > valueStart, "Expected og:image content value.");

            var imageUrl = html[valueStart..valueEnd];
            Assert.Contains("/assets/social/generated/", imageUrl, StringComparison.Ordinal);
            Assert.StartsWith("https://example.test/", imageUrl, StringComparison.Ordinal);

            var relativePath = imageUrl.Replace("https://example.test/", string.Empty, StringComparison.Ordinal);
            var generatedPath = Path.Combine(root, "_site", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(generatedPath), $"Generated social card missing: {generatedPath}");
            Assert.Contains("property=\"og:image:width\" content=\"1200\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:image:height\" content=\"630\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_GeneratesSocialCardImage_ForLongCommandLikeContent_WithinSafeLayout()
    {
        var root = CreateTempRoot("pf-web-social-generated-long-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Connect-IntelligenceX -Transport Native|AppServer|CompatibleHttp|CopilotCli -WorkingDirectory C:\very\long\workspace\path\that\keeps\going\on\and\on
                description: Invoke-IntelligenceXChat -Text "List risks and suggest remediations for this giant pull request with architecture notes and deployment caveats" -Stream -WaitSeconds 10 -AllowNetwork -Workspace C:\repo\workspace\with\very\long\name\and\subfolders.
                slug: index
                ---

                Long card text stress test.
                """);

            var spec = BuildPagesSpec();
            spec.Social = new SocialSpec
            {
                Enabled = true,
                SiteName = "Example Site",
                AutoGenerateCards = true,
                GeneratedCardsPath = "/assets/social/generated",
                GeneratedCardWidth = 1200,
                GeneratedCardHeight = 630
            };

            var html = BuildAndRead(root, spec, "index.html");
            const string marker = "property=\"og:image\" content=\"";
            var start = html.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, "Expected og:image meta tag.");

            var valueStart = start + marker.Length;
            var valueEnd = html.IndexOf('"', valueStart);
            Assert.True(valueEnd > valueStart, "Expected og:image content value.");

            var imageUrl = html[valueStart..valueEnd];
            Assert.Contains("/assets/social/generated/", imageUrl, StringComparison.Ordinal);

            var relativePath = imageUrl.Replace("https://example.test/", string.Empty, StringComparison.Ordinal);
            var generatedPath = Path.Combine(root, "_site", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(generatedPath), $"Generated social card missing: {generatedPath}");
            Assert.Contains("property=\"og:image:width\" content=\"1200\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:image:height\" content=\"630\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void ResolveSocialCardAssetDataUri_PreservesRemoteUrls()
    {
        var spec = BuildPagesSpec();
        var item = new ContentItem
        {
            SourcePath = Path.Combine(Path.GetTempPath(), "index.md"),
            Collection = "pages"
        };

        var resolved = WebSiteBuilder.ResolveSocialCardAssetDataUri(
            spec,
            item,
            "https://cdn.example.test/logo.svg");

        Assert.Equal("https://cdn.example.test/logo.svg", resolved);
    }

    [Fact]
    public void TryResolveSocialCardAssetPath_RejectsTraversalOutsideAllowedRoots()
    {
        var root = CreateTempRoot("pf-web-social-asset-root-");
        try
        {
            var pagesDir = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesDir);
            var pagePath = Path.Combine(pagesDir, "index.md");
            File.WriteAllText(pagePath, "# Home");

            var outsidePath = Path.Combine(root, "secret.png");
            File.WriteAllBytes(outsidePath, new byte[] { 1, 2, 3, 4 });

            var spec = BuildPagesSpec();
            var item = new ContentItem
            {
                SourcePath = pagePath,
                Collection = "pages"
            };

            var resolved = WebSiteBuilder.TryResolveSocialCardAssetPath(spec, item, "../../../secret.png");

            Assert.Equal(string.Empty, resolved);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_DoesNotAutoGenerateCards_ForDocsPages_UnlessOverridden()
    {
        var root = CreateTempRoot("pf-web-social-generated-scope-");
        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(docsPath, "guide.md"),
                """
                ---
                title: Guide
                description: Docs guide page.
                slug: guide
                ---

                Guide content.
                """);

            var spec = BuildPagesSpec();
            spec.Social = new SocialSpec
            {
                Enabled = true,
                SiteName = "Example Site",
                Image = "/assets/social/default.png",
                AutoGenerateCards = true,
                GeneratedCardsPath = "/assets/social/generated"
            };
            spec.Collections = new[]
            {
                new CollectionSpec
                {
                    Name = "docs",
                    Input = "content/docs",
                    Output = "/docs"
                }
            };

            var html = BuildAndRead(root, spec, Path.Combine("docs", "guide", "index.html"));
            Assert.Contains("property=\"og:image\" content=\"https://example.test/assets/social/default.png\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("/assets/social/generated/", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_UsesFirstMarkdownImage_ForBlogSocialPreview_WhenNoExplicitSocialImage()
    {
        var root = CreateTempRoot("pf-web-social-blog-first-image-");
        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "multilang.md"),
                """
                ---
                title: Multilanguage Support in Action
                description: Speak in any language you want.
                slug: multilanguage-support-in-action
                ---

                Intro text.

                ![Primary screenshot](/assets/screenshots/multilang-01.png)

                More text.
                """);

            var spec = BuildPagesSpec();
            spec.Social = new SocialSpec
            {
                Enabled = true,
                SiteName = "Example Site",
                Image = "/assets/social/default.png",
                AutoGenerateCards = true,
                GeneratedCardsPath = "/assets/social/generated"
            };
            spec.Collections = new[]
            {
                new CollectionSpec
                {
                    Name = "blog",
                    Input = "content/blog",
                    Output = "/blog"
                }
            };

            var html = BuildAndRead(root, spec, Path.Combine("blog", "multilanguage-support-in-action", "index.html"));
            Assert.Contains("property=\"og:image\" content=\"https://example.test/assets/screenshots/multilang-01.png\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"twitter:image\" content=\"https://example.test/assets/screenshots/multilang-01.png\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("/assets/social/generated/", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_EmitsStructuredDataByDefault_WhenEnabled()
    {
        var root = CreateTempRoot("pf-web-structured-default-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                ---

                Home body.
                """);

            var spec = BuildPagesSpec();
            spec.StructuredData = new StructuredDataSpec
            {
                Enabled = true,
                Breadcrumbs = true
            };

            var html = BuildAndRead(root, spec, "index.html");
            Assert.Contains("\"@type\":\"BreadcrumbList\"", html, StringComparison.Ordinal);
            Assert.Contains("\"@type\":\"WebSite\"", html, StringComparison.Ordinal);
            Assert.Contains("\"@type\":\"Organization\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_EmitsExtendedOrganizationStructuredData_WhenConfigured()
    {
        var root = CreateTempRoot("pf-web-structured-org-extended-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                ---

                Home body.
                """);

            var spec = BuildPagesSpec();
            spec.Social = new SocialSpec
            {
                Enabled = true,
                Image = "/assets/social/default.png"
            };
            spec.StructuredData = new StructuredDataSpec
            {
                Enabled = true,
                Breadcrumbs = false,
                Website = false,
                Organization = true,
                OrganizationLegalName = "Evotec Services sp. z o.o.",
                OrganizationDescription = "AI-powered developer tooling.",
                OrganizationLogo = "/assets/brand/logo.png",
                OrganizationSameAs = new[]
                {
                    "https://github.com/EvotecIT/IntelligenceX",
                    "https://x.com/evotecit"
                },
                OrganizationEmail = "support@example.test",
                OrganizationTelephone = "+1-555-0100",
                OrganizationContactUrl = "/contact/"
            };

            var html = BuildAndRead(root, spec, "index.html");
            Assert.Contains("\"@type\":\"Organization\"", html, StringComparison.Ordinal);
            Assert.Contains("\"legalName\":\"Evotec Services sp. z o.o.\"", html, StringComparison.Ordinal);
            Assert.Contains("\"description\":\"AI-powered developer tooling.\"", html, StringComparison.Ordinal);
            Assert.Contains("\"logo\":\"https://example.test/assets/brand/logo.png\"", html, StringComparison.Ordinal);
            Assert.Contains("\"sameAs\":[\"https://github.com/EvotecIT/IntelligenceX\",\"https://x.com/evotecit\"]", html, StringComparison.Ordinal);
            Assert.Contains("\"contactPoint\":[{\"@type\":\"ContactPoint\"", html, StringComparison.Ordinal);
            Assert.Contains("\"email\":\"support@example.test\"", html, StringComparison.Ordinal);
            Assert.Contains("\"telephone\":\"\\u002B1-555-0100\"", html, StringComparison.Ordinal);
            Assert.Contains("\"url\":\"https://example.test/contact/\"", html, StringComparison.Ordinal);
            Assert.Contains("\"contactType\":\"customer support\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_DoesNotEmitStructuredData_WhenPageOptsOut()
    {
        var root = CreateTempRoot("pf-web-structured-optout-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                meta.structured_data: false
                ---

                Home body.
                """);

            var spec = BuildPagesSpec();
            spec.StructuredData = new StructuredDataSpec
            {
                Enabled = true,
                Breadcrumbs = true
            };

            var html = BuildAndRead(root, spec, "index.html");
            Assert.DoesNotContain("\"@type\":\"BreadcrumbList\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("\"@type\":\"WebSite\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("\"@type\":\"Organization\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_EmitsArticleStructuredData_ForBlogPages()
    {
        var root = CreateTempRoot("pf-web-structured-article-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "ship-log.md"),
                """
                ---
                title: Ship Log
                slug: ship-log
                date: 2026-02-17
                ---

                Shipping updates.
                """);

            var spec = BuildPagesSpec();
            spec.Collections = new[]
            {
                new CollectionSpec
                {
                    Name = "pages",
                    Input = "content/pages",
                    Output = "/"
                },
                new CollectionSpec
                {
                    Name = "blog",
                    Input = "content/blog",
                    Output = "/blog"
                }
            };
            spec.StructuredData = new StructuredDataSpec
            {
                Enabled = true,
                Breadcrumbs = true,
                Article = true
            };

            var html = BuildAndRead(root, spec, Path.Combine("blog", "ship-log", "index.html"));
            Assert.Contains("\"@type\":\"Article\"", html, StringComparison.Ordinal);
            Assert.Contains("\"headline\":\"Ship Log\"", html, StringComparison.Ordinal);
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

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
