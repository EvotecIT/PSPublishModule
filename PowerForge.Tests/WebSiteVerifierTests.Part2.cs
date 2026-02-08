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
