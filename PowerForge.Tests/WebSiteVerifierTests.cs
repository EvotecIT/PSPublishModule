using PowerForge.Web;

public partial class WebSiteVerifierTests
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
    public void Verify_WarnsWhenSiteEnablesApiDocsButThemeMissingApiFragments()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-features-apidocs-" + Guid.NewGuid().ToString("N"));
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

            var themeRoot = Path.Combine(root, "themes", "feature-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "feature-test",
                  "engine": "scriban",
                  "defaultLayout": "home"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme Features Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "feature-test",
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
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("api-header.html", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("api-footer.html", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotWarnWhenThemeProvidesApiFragments()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-features-apidocs-ok-" + Guid.NewGuid().ToString("N"));
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

            var themeRoot = Path.Combine(root, "themes", "feature-test-ok");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "api-header.html"), "<header>{{NAV_LINKS}}</header>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "api-footer.html"), "<footer>{{YEAR}}</footer>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "feature-test-ok",
                  "engine": "scriban",
                  "defaultLayout": "home"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme Features Test OK",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "feature-test-ok",
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
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("site uses feature 'apiDocs'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
