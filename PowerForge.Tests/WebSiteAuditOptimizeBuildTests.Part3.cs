using PowerForge.Web;
using ImageMagick;
using System.Xml.Linq;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void Build_WritesNoJekyllMarkerAtSiteRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-build-nojekyll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "index.md"),
                """
                ---
                title: Home
                slug: /
                ---

                # Home
                """);

            var spec = new SiteSpec
            {
                Name = "Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                TrailingSlash = TrailingSlashMode.Always,
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

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);

            Assert.True(File.Exists(Path.Combine(outputRoot, ".nojekyll")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_NotFoundBundleResources_AreCopiedNextToRoot404Page()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-build-404-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content", "pages", "404");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "index.md"),
                """
                ---
                title: Page not found
                ---

                ![Missing page](./notfound.png)
                """);
            File.WriteAllBytes(Path.Combine(contentRoot, "notfound.png"), new byte[] { 1, 2, 3, 4 });

            var spec = new SiteSpec
            {
                Name = "Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                TrailingSlash = TrailingSlashMode.Always,
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

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);

            Assert.True(File.Exists(Path.Combine(outputRoot, "404.html")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "notfound.png")));
            Assert.False(File.Exists(Path.Combine(outputRoot, "404", "notfound.png")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_CanUseExternalContentRoots()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "pf-web-build-content-roots-" + Guid.NewGuid().ToString("N"));
        var siteRoot = Path.Combine(workspaceRoot, "website");
        var docsRoot = Path.Combine(workspaceRoot, "docs");
        Directory.CreateDirectory(siteRoot);
        Directory.CreateDirectory(docsRoot);

        try
        {
            File.WriteAllText(Path.Combine(docsRoot, "index.md"),
                """
                ---
                title: Documentation Home
                ---

                # Docs
                """);

            var spec = new SiteSpec
            {
                Name = "Test",
                BaseUrl = "https://example.test",
                ContentRoots = new[] { "../docs" },
                TrailingSlash = TrailingSlashMode.Always,
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "docs",
                        Output = "/docs"
                    }
                }
            };

            var configPath = Path.Combine(siteRoot, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(siteRoot, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);

            Assert.True(File.Exists(Path.Combine(outputRoot, "docs", "index.html")));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
                Directory.Delete(workspaceRoot, true);
        }
    }

    [Fact]
    public void Build_SiteNavExport_ContainsRegionsFooterProfilesAndSections()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-build-nav-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "index.md"),
                """
                ---
                title: Home
                slug: /
                ---

                # Home
                """);

            var spec = new SiteSpec
            {
                Name = "Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/"
                    }
                },
                Navigation = new NavigationSpec
                {
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[]
                            {
                                new MenuItemSpec
                                {
                                    Title = "Products",
                                    Url = "/products/",
                                    Sections = new[]
                                    {
                                        new MenuSectionSpec
                                        {
                                            Title = "SDK",
                                            Items = new[]
                                            {
                                                new MenuItemSpec { Title = ".NET", Url = "/docs/library/overview/" }
                                            },
                                            Columns = new[]
                                            {
                                                new MenuColumnSpec
                                                {
                                                    Name = "quick",
                                                    Title = "Quick links",
                                                    Items = new[]
                                                    {
                                                        new MenuItemSpec { Title = "CLI", Url = "/docs/cli/overview/" }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        new MenuSpec
                        {
                            Name = "footer-product",
                            Label = "Product",
                            Items = new[] { new MenuItemSpec { Title = "Docs", Url = "/docs/" } }
                        }
                    },
                    Actions = new[]
                    {
                        new MenuItemSpec
                        {
                            Title = "Get Started",
                            Url = "/docs/getting-started/",
                            Kind = "button",
                            Slot = "header.cta"
                        }
                    },
                    Regions = new[]
                    {
                        new NavigationRegionSpec
                        {
                            Name = "header.right",
                            Menus = new[] { "main" },
                            IncludeActions = true,
                            Items = new[] { new MenuItemSpec { Title = "Status", Url = "/status/" } }
                        }
                    },
                    Footer = new NavigationFooterSpec
                    {
                        Label = "default",
                        Menus = new[] { "footer-product" },
                        Columns = new[]
                        {
                            new NavigationFooterColumnSpec
                            {
                                Name = "resources",
                                Title = "Resources",
                                Items = new[] { new MenuItemSpec { Title = "FAQ", Url = "/faq/" } }
                            }
                        },
                        Legal = new[] { new MenuItemSpec { Title = "Privacy", Url = "/privacy/" } }
                    },
                    Profiles = new[]
                    {
                        new NavigationProfileSpec
                        {
                            Name = "docs",
                            Paths = new[] { "/docs/**" },
                            Priority = 20,
                            InheritMenus = true,
                            Menus = new[]
                            {
                                new MenuSpec
                                {
                                    Name = "docs-extra",
                                    Items = new[] { new MenuItemSpec { Title = "API", Url = "/api/" } }
                                }
                            }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);

            var siteNavPath = Path.Combine(outputRoot, "data", "site-nav.json");
            Assert.True(File.Exists(siteNavPath));
            var siteNav = File.ReadAllText(siteNavPath);
            using var doc = System.Text.Json.JsonDocument.Parse(siteNav);
            var rootElement = doc.RootElement;
            Assert.True(rootElement.TryGetProperty("regions", out var regions));
            Assert.True(rootElement.TryGetProperty("footerModel", out var footerModel));
            Assert.True(rootElement.TryGetProperty("profiles", out var profiles));
            Assert.True(rootElement.TryGetProperty("surfaces", out var surfaces));
            Assert.Equal(System.Text.Json.JsonValueKind.Object, surfaces.ValueKind);
            Assert.True(surfaces.TryGetProperty("main", out var mainSurface));
            Assert.True(mainSurface.TryGetProperty("primary", out var mainPrimary));
            Assert.Equal(System.Text.Json.JsonValueKind.Array, mainPrimary.ValueKind);

            var actions = rootElement.GetProperty("actions");
            Assert.Contains(actions.EnumerateArray(), action =>
                action.TryGetProperty("slot", out var slot) &&
                string.Equals(slot.GetString(), "header.cta", StringComparison.Ordinal));

            var mainMenu = rootElement.GetProperty("menus").GetProperty("main");
            Assert.Contains(mainMenu.EnumerateArray(), item =>
                item.TryGetProperty("sections", out var sections) &&
                sections.ValueKind == System.Text.Json.JsonValueKind.Array &&
                sections.GetArrayLength() > 0);

            Assert.Contains(profiles.EnumerateArray(), profile =>
                profile.TryGetProperty("name", out var name) &&
                string.Equals(name.GetString(), "docs", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_NavProfilesAllowDifferentNavScopes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-nav-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <header><nav><a href="/">Home</a><a href="/docs/">Docs</a></nav></header>
                </body>
                </html>
                """);

            var apiRoot = Path.Combine(root, "api");
            Directory.CreateDirectory(apiRoot);
            File.WriteAllText(Path.Combine(apiRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>API</title></head>
                <body>
                  <aside><nav><a href="/api/">API Home</a></nav></aside>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                NavSelector = "header nav",
                IgnoreNavFor = Array.Empty<string>(),
                NavProfiles = new[]
                {
                    new WebAuditNavProfile
                    {
                        Match = "api/**",
                        Selector = "aside nav",
                        RequiredLinks = new[] { "/api/" }
                    }
                },
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.True(result.Success);
            Assert.Equal(2, result.NavCheckedCount);
            Assert.Equal(0, result.NavMismatchCount);
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("nav not found", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_WritesSarifOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-sarif-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><nav><a href="/">Home</a></nav></body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                SarifPath = "audit.sarif.json"
            });

            Assert.True(result.Success);
            Assert.NotNull(result.SarifPath);
            Assert.True(File.Exists(result.SarifPath!));

            var sarif = File.ReadAllText(result.SarifPath!);
            Assert.Contains("\"$schema\"", sarif, StringComparison.Ordinal);
            Assert.Contains("\"runs\"", sarif, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_WarnsWhenExternalOriginMissingNetworkHints()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-network-hints-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Home</title>
                  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;700&display=swap" />
                </head>
                <body><nav><a href="/">Home</a></nav></body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("missing preconnect/dns-prefetch", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Issues, issue => issue.Category.Equals("network-hint", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_WarnsWhenHeadRenderBlockingExceedsLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-render-blocking-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Home</title>
                  <link rel="stylesheet" href="/css/a.css" />
                  <link rel="stylesheet" href="/css/b.css" />
                  <script src="/js/a.js"></script>
                </head>
                <body><nav><a href="/">Home</a></nav></body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                MaxHeadBlockingResources = 2
            });

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("render-blocking resources", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Issues, issue => issue.Category.Equals("render-blocking", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_WarnsWhenMediaEmbedsMissPerformanceHints()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-hints-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <main>
                    <iframe src="https://www.youtube.com/embed/dQw4w9WgXcQ"></iframe>
                    <img src="/images/hero.jpg" />
                    <img src="/images/card.jpg" srcset="/images/card-320.jpg 320w, /images/card-640.jpg 640w" />
                  </main>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("iframe embed(s) missing loading=\"lazy\"", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("image(s) missing width/height", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Issues, issue => issue.Category.Equals("media", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_MediaEmbedChecksCanBeDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <main>
                    <iframe src="https://example.test/embed"></iframe>
                    <img src="/images/hero.jpg" />
                  </main>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                CheckMediaEmbeds = false
            });

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("media", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_DefaultMediaIgnorePatterns_SkipApiPages()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-ignore-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var apiRoot = Path.Combine(root, "api");
            Directory.CreateDirectory(apiRoot);
            File.WriteAllText(Path.Combine(apiRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>API</title></head>
                <body>
                  <iframe src="https://www.youtube.com/embed/dQw4w9WgXcQ"></iframe>
                  <img src="/images/hero.jpg" />
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("media", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_MediaChecksRunForApiPagesWhenDefaultMediaIgnoreIsCleared()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-ignore-cleared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var apiRoot = Path.Combine(root, "api");
            Directory.CreateDirectory(apiRoot);
            File.WriteAllText(Path.Combine(apiRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>API</title></head>
                <body>
                  <iframe src="https://www.youtube.com/embed/dQw4w9WgXcQ"></iframe>
                  <img src="/images/hero.jpg" />
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                IgnoreMediaFor = Array.Empty<string>()
            });

            Assert.True(result.Success);
            Assert.Contains(result.Issues, issue => issue.Category.Equals("media", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_MediaProfileCanAllowStandardYouTubeHost()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-profile-youtube-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var apiRoot = Path.Combine(root, "api");
            Directory.CreateDirectory(apiRoot);
            File.WriteAllText(Path.Combine(apiRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>API</title></head>
                <body>
                  <iframe
                    src="https://www.youtube.com/embed/dQw4w9WgXcQ"
                    loading="lazy"
                    title="Demo video"
                    referrerpolicy="strict-origin-when-cross-origin"></iframe>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                IgnoreMediaFor = Array.Empty<string>(),
                MediaProfiles = new[]
                {
                    new WebAuditMediaProfile
                    {
                        Match = "api/**",
                        AllowYoutubeStandardHost = true
                    }
                }
            });

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("youtube-nocookie", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_MediaProfileCanIgnoreSpecificSurface()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-profile-ignore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsRoot = Path.Combine(root, "docs");
            Directory.CreateDirectory(docsRoot);
            File.WriteAllText(Path.Combine(docsRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Docs</title></head>
                <body>
                  <iframe src="https://example.test/embed"></iframe>
                  <img src="/images/hero.jpg" />
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                IgnoreMediaFor = Array.Empty<string>(),
                MediaProfiles = new[]
                {
                    new WebAuditMediaProfile
                    {
                        Match = "docs/**",
                        Ignore = true
                    }
                }
            });

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("media", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_WarnsWhenHeadingOrderSkipsLevels()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-heading-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <main>
                    <h1>Home</h1>
                    <h3>Documentation</h3>
                  </main>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("heading order skips levels", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Issues, issue => issue.Category.Equals("heading-order", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_IgnoresHeadingOrderSkipsInFooterChrome()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-heading-order-footer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <main>
                    <h1>Home</h1>
                    <h2>Overview</h2>
                  </main>
                  <footer>
                    <h4>Documentation</h4>
                  </footer>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("heading-order", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_WarnsWhenSameLinkLabelPointsToMultipleDestinations()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-link-purpose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <main>
                    <a href="/docs/reviewer/overview/">Learn more</a>
                    <a href="/docs/cli/overview/">Learn more</a>
                  </main>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("points to multiple destinations", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_LinkPurposeCheckCanBeDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-link-purpose-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <a href="/docs/a/">Learn more</a>
                  <a href="/docs/b/">Learn more</a>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                CheckLinkPurposeConsistency = false
            });

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
