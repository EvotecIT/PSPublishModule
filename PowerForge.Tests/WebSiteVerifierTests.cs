using PowerForge.Web;

public class WebSiteVerifierTests
{
    [Fact]
    public void Verify_WarnsWhenVersioningIsMisconfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-versioning-" + Guid.NewGuid().ToString("N"));
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

            var spec = new SiteSpec
            {
                Name = "Verifier Versioning Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Versioning = new VersioningSpec
                {
                    Enabled = true,
                    BasePath = "/docs",
                    Current = "v3",
                    Versions = new[]
                    {
                        new VersionSpec { Name = "v2", Url = "/docs/v2/", Latest = true, Default = true },
                        new VersionSpec { Name = "v2", Url = "/docs/v2-duplicate/" },
                        new VersionSpec { Name = "v1", Url = "docs/v1/" }
                    }
                },
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
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("Current 'v3' does not match any configured version", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("duplicate version 'v2'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("url 'docs/v1/' should be root-relative", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotFlagTaxonomyRoutesAsMissingForNavigationLint()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-taxonomy-nav-" + Guid.NewGuid().ToString("N"));
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
                tags: [release]
                ---

                Home
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Taxonomy Nav Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Taxonomies = new[]
                {
                    new TaxonomySpec { Name = "tags", BasePath = "/tags" }
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
                                new MenuItemSpec { Title = "Tags", Url = "/tags/" }
                            }
                        }
                    }
                },
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
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/tags/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenBlogCollectionHasNoLandingPage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-blog-landing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "first-post.md"),
                """
                ---
                title: First Post
                ---

                Hello
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Blog Landing Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("looks like a blog but has no landing page", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotFlagLocalizedTaxonomyRoutesAsMissingForNavigationLint()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-localized-taxonomy-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var enPath = Path.Combine(root, "content", "pages", "en");
            var plPath = Path.Combine(root, "content", "pages", "pl");
            Directory.CreateDirectory(enPath);
            Directory.CreateDirectory(plPath);
            File.WriteAllText(Path.Combine(enPath, "index.md"),
                """
                ---
                title: Home
                tags: [release]
                ---

                Home
                """);
            File.WriteAllText(Path.Combine(plPath, "index.md"),
                """
                ---
                title: Start
                tags: [release]
                ---

                Start
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Localized Taxonomy Nav Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    PrefixDefaultLanguage = false,
                    DetectFromPath = true,
                    Languages = new[]
                    {
                        new LanguageSpec { Code = "en", Default = true },
                        new LanguageSpec { Code = "pl" }
                    }
                },
                Taxonomies = new[]
                {
                    new TaxonomySpec { Name = "tags", BasePath = "/tags" }
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
                                new MenuItemSpec { Title = "Tagi", Url = "/pl/tags/" }
                            }
                        }
                    }
                },
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

            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/pl/tags/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenLocalizationContainsDuplicates()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-localization-duplicates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                ---

                Home
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Localization Duplicate Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    Languages = new[]
                    {
                        new LanguageSpec { Code = "en", Prefix = "en", Default = true },
                        new LanguageSpec { Code = "EN", Prefix = "english" },
                        new LanguageSpec { Code = "pl", Prefix = "en" }
                    }
                },
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

            Assert.Contains(result.Warnings, warning => warning.Contains("duplicate language code 'en'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("duplicate language prefix 'en'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenThemeManifestUsesNonPortablePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-contract-" + Guid.NewGuid().ToString("N"));
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

            var themeRoot = Path.Combine(root, "themes", "portable-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "portable-test",
                  "engine": "scriban",
                  "layouts": { "home": "C:\\layouts\\home.html" },
                  "assets": {
                    "bundles": [
                      { "name": "global", "css": ["file://portable-test/assets/app.css"] }
                    ]
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme Contract Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "portable-test",
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
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("C:\\layouts\\home.html", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("file://portable-test/assets/app.css", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenThemeDefinesTokensWithoutThemeTokensPartial()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-tokens-" + Guid.NewGuid().ToString("N"));
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

            var themeRoot = Path.Combine(root, "themes", "tokens-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "tokens-test",
                  "engine": "scriban",
                  "defaultLayout": "home",
                  "tokens": {
                    "color": { "bg": "#0b0b12" }
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme Token Contract Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "tokens-test",
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
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("does not provide partial 'theme-tokens'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenThemeContractV2SlotsAreInvalid()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-contract-v2-" + Guid.NewGuid().ToString("N"));
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

            var themeRoot = Path.Combine(root, "themes", "contract-v2-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "contract-v2-test",
                  "contractVersion": 2,
                  "engine": "scriban",
                  "slots": {
                    "hero": "partials/missing-slot"
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme Contract v2 Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "contract-v2-test",
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
            Assert.Contains(result.Warnings, warning => warning.Contains("schemaVersion 2 should set 'defaultLayout'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("schemaVersion 2 should set 'scriptsPath'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("slot 'hero' maps to missing partial", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

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
