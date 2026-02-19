using PowerForge.Web;

public partial class WebSiteVerifierTests
{
    [Fact]
    public void Verify_WarnsWhenThemeUsesLegacyContractVersionField()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-legacy-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var themeRoot = Path.Combine(root, "themes", "legacy-version-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "theme-tokens.html"), "<style></style>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "name": "legacy-version-test",
                  "contractVersion": 2,
                  "engine": "scriban",
                  "defaultLayout": "home",
                  "scriptsPath": "assets",
                  "slots": {
                    "hero": "theme-tokens"
                  },
                  "tokens": {
                    "colorBg": "#000"
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme Legacy Version Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "legacy-version-test",
                ThemesRoot = "themes",
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
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("uses legacy 'contractVersion'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenThemeSchemaVersionConflictsWithContractVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-version-mismatch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var themeRoot = Path.Combine(root, "themes", "version-mismatch-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "name": "version-mismatch-test",
                  "schemaVersion": 2,
                  "contractVersion": 1,
                  "engine": "scriban",
                  "defaultLayout": "home",
                  "scriptsPath": "assets",
                  "slots": {
                    "hero": "partials/hero"
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme Version Mismatch Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "version-mismatch-test",
                ThemesRoot = "themes",
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
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("defines schemaVersion=2 and contractVersion=1", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsOnNavigationLintFindings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-nav-lint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(pagesPath);
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);
            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs
                slug: index
                ---

                Docs
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Navigation Lint Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/"
                    },
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "content/docs",
                        Output = "/docs"
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
                                new MenuItemSpec { Id = "dup-id", Title = "Home", Url = "/" },
                                new MenuItemSpec { Title = "Docs", Url = "/docs/" }
                            }
                        },
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[] { new MenuItemSpec { Title = "Pricing", Url = "/pricing/" } }
                        }
                    },
                    Regions = new[]
                    {
                        new NavigationRegionSpec
                        {
                            Name = "header.right",
                            Menus = new[] { "missing-menu" }
                        }
                    },
                    Actions = new[]
                    {
                        new MenuItemSpec { Id = "dup-id", Title = "Start", Url = "/start/" }
                    },
                    Profiles = new[]
                    {
                        new NavigationProfileSpec
                        {
                            Name = "api",
                            Paths = new[] { "/docs/private/**" }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("Navigation lint: duplicate menu name 'main'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("Navigation lint: duplicate item id 'dup-id'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("references unknown menu 'missing-menu'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("Paths do not match any generated routes", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_ThemeFeatureContracts_WarnWhenRequiredPartialIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-feature-contracts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var themeRoot = Path.Combine(root, "themes", "contract-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "name": "contract-test",
                  "schemaVersion": 2,
                  "engine": "scriban",
                  "defaultLayout": "home",
                  "features": ["apiDocs"],
                  "featureContracts": {
                    "apiDocs": {
                      "requiredPartials": ["api-header", "api-footer"]
                    }
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme Feature Contracts Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "contract-test",
                ThemesRoot = "themes",
                Features = new[] { "apiDocs" },
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
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, w => w.Contains("feature 'apidocs' requires partial 'api-header'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, w => w.Contains("feature 'apidocs' requires partial 'api-footer'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenEditorialListLayoutDoesNotRenderItemsOrEditorialHelper()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-editorial-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"),
                """
                ---
                title: Blog
                description: Updates
                ---

                # Blog
                """);
            File.WriteAllText(Path.Combine(blogPath, "post-1.md"),
                """
                ---
                title: Post 1
                date: 2026-01-01
                ---

                Post content
                """);

            var themeRoot = Path.Combine(root, "themes", "editorial-warning");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "list.html"),
                """
                <!doctype html><html><body><h1>{{ page.title }}</h1></body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "post.html"),
                """
                <!doctype html><html><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "partials", "theme-tokens.html"), "<style></style>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "name": "editorial-warning",
                  "schemaVersion": 2,
                  "engine": "scriban",
                  "defaultLayout": "list",
                  "features": ["blog"]
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Editorial layout test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "editorial-warning",
                ThemesRoot = "themes",
                Features = new[] { "blog" },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        DefaultLayout = "post",
                        ListLayout = "list",
                        PageSize = 5
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.Contains(result.Warnings, warning =>
                warning.Contains("used for editorial collection", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("paginated editorial collection", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenEditorialVariantContractSelectorsAreNotDeclared()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-editorial-variant-contract-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"),
                """
                ---
                title: Blog
                ---

                # Blog
                """);
            File.WriteAllText(Path.Combine(blogPath, "post-1.md"),
                """
                ---
                title: Post 1
                date: 2026-01-01
                ---

                Post content
                """);

            var themeRoot = Path.Combine(root, "themes", "editorial-contract-warning");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "list.html"),
                """
                <!doctype html><html><body>{{ pf.editorial_cards 0 160 true true true true "16/9" "" "hero" "news-grid" "news-card" }}{{ pf.editorial_pager }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "post.html"),
                """
                <!doctype html><html><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "partials", "theme-tokens.html"), "<style></style>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "name": "editorial-contract-warning",
                  "schemaVersion": 2,
                  "engine": "scriban",
                  "defaultLayout": "list",
                  "features": ["blog"],
                  "featureContracts": {
                    "blog": {
                      "requiredCssSelectors": []
                    }
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Editorial selector contract warning test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "editorial-contract-warning",
                ThemesRoot = "themes",
                Features = new[] { "blog" },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        DefaultLayout = "post",
                        ListLayout = "list",
                        PageSize = 5
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.Contains(result.Warnings, warning =>
                warning.Contains("featureContracts.blog.requiredCssSelectors is empty", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotWarnWhenEditorialVariantContractSelectorsAreDeclared()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-editorial-variant-contract-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"),
                """
                ---
                title: Blog
                ---

                # Blog
                """);
            File.WriteAllText(Path.Combine(blogPath, "post-1.md"),
                """
                ---
                title: Post 1
                date: 2026-01-01
                ---

                Post content
                """);

            var themeRoot = Path.Combine(root, "themes", "editorial-contract-ok");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "styles"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "list.html"),
                """
                <!doctype html><html><body>{{ pf.editorial_cards 0 160 true true true true "16/9" "" "hero" "news-grid" "news-card" }}{{ pf.editorial_pager }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "post.html"),
                """
                <!doctype html><html><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "partials", "theme-tokens.html"), "<style></style>");
            File.WriteAllText(Path.Combine(themeRoot, "styles", "editorial.css"),
                """
                .pf-editorial-grid--hero {}
                .pf-editorial-card--hero {}
                .news-grid {}
                .news-card {}
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "name": "editorial-contract-ok",
                  "schemaVersion": 2,
                  "engine": "scriban",
                  "defaultLayout": "list",
                  "features": ["blog"],
                  "featureContracts": {
                    "blog": {
                      "cssHrefs": ["styles/editorial.css"],
                      "requiredCssSelectors": [".pf-editorial-grid--hero", ".pf-editorial-card--hero", ".news-grid", ".news-card"]
                    }
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Editorial selector contract ok test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "editorial-contract-ok",
                ThemesRoot = "themes",
                Features = new[] { "blog" },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        DefaultLayout = "post",
                        ListLayout = "list",
                        PageSize = 5
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("uses pf.editorial_cards selectors that are not declared", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("featureContracts.blog.requiredCssSelectors is empty", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_ThemeFeatureContracts_WarnWhenRequiredSurfacesMissingAndSiteDoesNotDefineSurfaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-required-surfaces-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var themeRoot = Path.Combine(root, "themes", "required-surfaces-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "name": "required-surfaces-test",
                  "schemaVersion": 2,
                  "engine": "scriban",
                  "defaultLayout": "home",
                  "features": ["apiDocs"],
                  "featureContracts": {
                    "apiDocs": {
                      "requiredSurfaces": ["api"]
                    }
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme RequiredSurfaces Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "required-surfaces-test",
                ThemesRoot = "themes",
                Features = new[] { "apiDocs" },
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
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[] { new MenuItemSpec { Title = "Home", Url = "/" } }
                        }
                    }
                    // Surfaces intentionally omitted.
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, w =>
                w.Contains("Theme contract:", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("requires nav surfaces", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("Navigation.Surfaces", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_ThemeFeatureContracts_RespectsApiSurfaceAliasesWhenSurfacesAreDefined()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-required-surfaces-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var themeRoot = Path.Combine(root, "themes", "required-surfaces-alias-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "name": "required-surfaces-alias-test",
                  "schemaVersion": 2,
                  "engine": "scriban",
                  "defaultLayout": "home",
                  "features": ["apiDocs"],
                  "featureContracts": {
                    "apiDocs": {
                      "requiredSurfaces": ["api"]
                    }
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme RequiredSurfaces Alias Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "required-surfaces-alias-test",
                ThemesRoot = "themes",
                Features = new[] { "apiDocs" },
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
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[] { new MenuItemSpec { Title = "Home", Url = "/" } }
                        }
                    },
                    Surfaces = new[]
                    {
                        // Define the API surface using a different alias to ensure normalization works.
                        new NavigationSurfaceSpec { Name = "apiDocs", Path = "/api/", Layout = "apiDocs", PrimaryMenu = "main" }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, w =>
                w.Contains("requires nav surface", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("api", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("Navigation.Surfaces", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenCustomSiteNavIsMissingProfilesAndSurfaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-site-nav-shape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(Path.Combine(root, "data", "site-nav.json"),
                """
                {
                  "primary": [
                    { "href": "/", "text": "Home" }
                  ]
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier site-nav shape test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DataRoot = "data",
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
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[]
                            {
                                new MenuItemSpec { Title = "Home", Url = "/" }
                            }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, w => w.Contains("site-nav.json does not contain 'profiles'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, w => w.Contains("site-nav.json does not contain 'surfaces'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsGeneratedLegacySiteNavOnceWithoutProfileSurfaceNoise()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-site-nav-generated-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(Path.Combine(root, "data", "site-nav.json"),
                """
                {
                  "generated": true,
                  "primary": [
                    { "href": "/", "text": "Home" }
                  ]
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier generated site-nav shape test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DataRoot = "data",
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
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[]
                            {
                                new MenuItemSpec { Title = "Home", Url = "/" }
                            }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, w => w.Contains("generated site-nav.json uses a legacy contract", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, w => w.Contains("site-nav.json does not contain 'profiles'", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, w => w.Contains("site-nav.json does not contain 'surfaces'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenExpectedNavSurfacesAreNotExplicitlyDefined()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-surfaces-required-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(pagesPath);
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);
            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs
                slug: index
                ---

                Docs
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier nav surfaces explicitness test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Features = new[] { "docs", "apiDocs" },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/"
                    },
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "content/docs",
                        Output = "/docs"
                    }
                },
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[]
                            {
                                new MenuItemSpec { Title = "Home", Url = "/" },
                                new MenuItemSpec { Title = "Docs", Url = "/docs/" },
                                new MenuItemSpec { Title = "API", Url = "/api/" }
                            }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, w =>
                w.Contains("expected nav surfaces", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("Navigation.Surfaces", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenNavSurfaceContextIsMisconfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-surfaces-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(pagesPath);
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);
            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs
                slug: index
                ---

                Docs
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier nav surfaces context test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Features = new[] { "docs", "apiDocs" },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/"
                    },
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "content/docs",
                        Output = "/docs"
                    }
                },
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[]
                            {
                                new MenuItemSpec { Title = "Home", Url = "/" },
                                new MenuItemSpec { Title = "Docs", Url = "/docs/" },
                                new MenuItemSpec { Title = "API", Url = "/api/" }
                            }
                        }
                    },
                    Surfaces = new[]
                    {
                        new NavigationSurfaceSpec
                        {
                            Name = "main",
                            Path = "/marketing/",
                            PrimaryMenu = "main"
                        },
                        new NavigationSurfaceSpec
                        {
                            Name = "docs",
                            Path = "/help/",
                            PrimaryMenu = "main"
                        },
                        new NavigationSurfaceSpec
                        {
                            Name = "api",
                            Path = "/docs/api/",
                            Layout = "docs",
                            PrimaryMenu = "main"
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, w => w.Contains("Surfaces['main'] should target root path '/'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, w => w.Contains("Surfaces['docs'] should target docs context", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, w => w.Contains("Surfaces['api'] should target API docs context", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenCustomSiteNavSurfacesMissExpectedApiSurface()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-site-nav-missing-apidocs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(pagesPath);
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);
            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs
                slug: index
                ---

                Docs
                """);

            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(Path.Combine(root, "data", "site-nav.json"),
                """
                {
                  "primary": [
                    { "href": "/", "text": "Home" },
                    { "href": "/docs/", "text": "Docs" },
                    { "href": "/api/", "text": "API" }
                  ],
                  "menuModels": [],
                  "profiles": [],
                  "surfaces": {
                    "main": {},
                    "docs": {}
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier site-nav expected surface test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DataRoot = "data",
                Features = new[] { "docs", "apiDocs" },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/"
                    },
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "content/docs",
                        Output = "/docs"
                    }
                },
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[]
                            {
                                new MenuItemSpec { Title = "Home", Url = "/" },
                                new MenuItemSpec { Title = "Docs", Url = "/docs/" },
                                new MenuItemSpec { Title = "API", Url = "/api/" }
                            }
                        }
                    },
                    Surfaces = new[]
                    {
                        new NavigationSurfaceSpec { Name = "main", Path = "/", PrimaryMenu = "main" },
                        new NavigationSurfaceSpec { Name = "docs", Path = "/docs/", Collection = "docs", Layout = "docs", PrimaryMenu = "main" },
                        new NavigationSurfaceSpec { Name = "apidocs", Path = "/api/", Layout = "apiDocs", PrimaryMenu = "main" }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, w => w.Contains("site-nav.json surfaces are missing expected 'apidocs' surface", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenCustomSiteNavUsesApiAliasSurfaceKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-site-nav-api-alias-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(pagesPath);
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);
            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Docs
                slug: index
                ---

                Docs
                """);

            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(Path.Combine(root, "data", "site-nav.json"),
                """
                {
                  "schemaVersion": 2,
                  "format": "powerforge.site-nav",
                  "surfaceAliases": {
                    "api": "apidocs"
                  },
                  "primary": [
                    { "href": "/", "text": "Home" },
                    { "href": "/docs/", "text": "Docs" },
                    { "href": "/api/", "text": "API" }
                  ],
                  "menuModels": [],
                  "profiles": [],
                  "surfaces": {
                    "main": {},
                    "docs": {},
                    "api": {}
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier site-nav alias surface key test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DataRoot = "data",
                Features = new[] { "docs", "apiDocs" },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/"
                    },
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "content/docs",
                        Output = "/docs"
                    }
                },
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[]
                            {
                                new MenuItemSpec { Title = "Home", Url = "/" },
                                new MenuItemSpec { Title = "Docs", Url = "/docs/" },
                                new MenuItemSpec { Title = "API", Url = "/api/" }
                            }
                        }
                    },
                    Surfaces = new[]
                    {
                        new NavigationSurfaceSpec { Name = "main", Path = "/", PrimaryMenu = "main" },
                        new NavigationSurfaceSpec { Name = "docs", Path = "/docs/", Collection = "docs", Layout = "docs", PrimaryMenu = "main" },
                        new NavigationSurfaceSpec { Name = "apidocs", Path = "/api/", Layout = "apiDocs", PrimaryMenu = "main" }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, w => w.Contains("surface key 'api' should use canonical key 'apidocs'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsOnMarkdownRawHtmlHygiene()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                <p><strong>Hello</strong> world</p>
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "content/docs",
                        Output = "/docs"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("Markdown hygiene", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhen404RouteHasNoAssetBundleMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);
            File.WriteAllText(Path.Combine(pagesPath, "404.md"),
                """
                ---
                title: Not found
                slug: 404
                ---

                Not found
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Test",
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
                },
                AssetRegistry = new AssetRegistrySpec
                {
                    Bundles = new[]
                    {
                        new AssetBundleSpec
                        {
                            Name = "global",
                            Css = new[] { "/css/app.css" },
                            Js = new[] { "/js/site.js" }
                        }
                    },
                    RouteBundles = new[]
                    {
                        new RouteBundleSpec
                        {
                            Match = "/",
                            Bundles = new[] { "global" }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("Route '/404", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any AssetRegistry.RouteBundles entry", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotWarnWhen404RouteHasFallbackAssetBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-404-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);
            File.WriteAllText(Path.Combine(pagesPath, "404.md"),
                """
                ---
                title: Not found
                slug: 404
                ---

                Not found
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Test",
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
                },
                AssetRegistry = new AssetRegistrySpec
                {
                    Bundles = new[]
                    {
                        new AssetBundleSpec
                        {
                            Name = "global",
                            Css = new[] { "/css/app.css" },
                            Js = new[] { "/js/site.js" }
                        }
                    },
                    RouteBundles = new[]
                    {
                        new RouteBundleSpec
                        {
                            Match = "/**",
                            Bundles = new[] { "global" }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("does not match any AssetRegistry.RouteBundles entry", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotWarnWhen404RouteHasDirectAssetBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-404-direct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);
            File.WriteAllText(Path.Combine(pagesPath, "404.md"),
                """
                ---
                title: Not found
                slug: 404
                ---

                Not found
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Test",
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
                },
                AssetRegistry = new AssetRegistrySpec
                {
                    Bundles = new[]
                    {
                        new AssetBundleSpec
                        {
                            Name = "global",
                            Css = new[] { "/css/app.css" },
                            Js = new[] { "/js/site.js" }
                        }
                    },
                    RouteBundles = new[]
                    {
                        new RouteBundleSpec
                        {
                            Match = "/404",
                            Bundles = new[] { "global" }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("does not match any AssetRegistry.RouteBundles entry", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotThrowWhenAssetBundleNamesDuplicate()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-404-dup-bundles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);
            File.WriteAllText(Path.Combine(pagesPath, "404.md"),
                """
                ---
                title: Not found
                slug: 404
                ---

                Not found
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Test",
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
                },
                AssetRegistry = new AssetRegistrySpec
                {
                    Bundles = new[]
                    {
                        new AssetBundleSpec { Name = "global", Css = new[] { "/css/a.css" }, Js = new[] { "/js/a.js" } },
                        new AssetBundleSpec { Name = "GLOBAL", Css = new[] { "/css/b.css" }, Js = new[] { "/js/b.js" } }
                    },
                    RouteBundles = new[]
                    {
                        new RouteBundleSpec
                        {
                            Match = "/",
                            Bundles = new[] { "global" }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("Route '/404", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any AssetRegistry.RouteBundles entry", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
