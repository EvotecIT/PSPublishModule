using PowerForge.Web;

public partial class WebSiteVerifierTests
{
    [Fact]
    public void Verify_UsesMappedStaticHtmlFilesAsNavigationRoutes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-static-routes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<h1>Home</h1>");
            File.WriteAllText(Path.Combine(root, "apps.html"), "<h1>Apps</h1>");
            File.WriteAllText(Path.Combine(root, "About # Żółć.htm"), "<h1>About</h1>");
            var content = Path.Combine(root, "content");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "pipeline.md"), "---\ntitle: Pipeline\nslug: pipeline\n---\n\nPipeline");
            var staticDocs = Path.Combine(root, "static-docs");
            Directory.CreateDirectory(staticDocs);
            File.WriteAllText(Path.Combine(staticDocs, "index.html"), "<h1>Docs</h1>");
            File.WriteAllText(Path.Combine(staticDocs, "manual.pdf"), "PDF");
            var conventional = Path.Combine(root, "static", "ingestion", "conventional");
            Directory.CreateDirectory(conventional);
            File.WriteAllText(Path.Combine(conventional, "index.html"), "<h1>Conventional</h1>");

            var spec = new SiteSpec
            {
                Name = "Static Navigation Test",
                BaseUrl = "https://example.test",
                Collections =
                [
                    new CollectionSpec { Name = "pages", Input = "content", Output = "/ingestion" }
                ],
                Features = ["search"],
                StaticAssets =
                [
                    new StaticAssetSpec { Source = "index.html", Destination = "./" },
                    new StaticAssetSpec { Source = "apps.html", Destination = "ingestion/apps.html" },
                    new StaticAssetSpec { Source = "About # Żółć.htm", Destination = "nested\\../" },
                    new StaticAssetSpec { Source = "static-docs", Destination = "ingestion/docs" }
                ],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items =
                            [
                                new MenuItemSpec { Title = "Home", Url = "/" },
                                new MenuItemSpec { Title = "Apps", Url = "/ingestion/apps.html" },
                                new MenuItemSpec { Title = "About", Url = "/About%20%23%20%C5%BB%C3%B3%C5%82%C4%87.htm" },
                                new MenuItemSpec { Title = "Docs", Url = "/ingestion/docs/" },
                                new MenuItemSpec { Title = "Manual", Url = "/ingestion/docs/manual.pdf" },
                                new MenuItemSpec { Title = "Conventional", Url = "/ingestion/conventional/" },
                                new MenuItemSpec { Title = "Search", Url = "/search/" }
                            ]
                        }
                    ]
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("points to '/'", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/ingestion/apps.html", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/About%20%23%20%C5%BB%C3%B3%C5%82%C4%87.htm", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/ingestion/docs/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/ingestion/docs/manual.pdf", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/ingestion/conventional/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/search/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));

            var allMappings = spec.StaticAssets;
            spec.StaticAssets = allMappings.Where(mapping => mapping.Source != "apps.html").ToArray();
            var resultWithoutApps = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.Contains(resultWithoutApps.Warnings, warning =>
                warning.Contains("/ingestion/apps.html", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));

            spec.StaticAssets = allMappings.Where(mapping => mapping.Source != "About # Żółć.htm").ToArray();
            var resultWithoutAbout = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.Contains(resultWithoutAbout.Warnings, warning =>
                warning.Contains("/About%20%23%20%C5%BB%C3%B3%C5%82%C4%87.htm", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));

            spec.StaticAssets = allMappings.Where(mapping => mapping.Source != "static-docs").ToArray();
            var resultWithoutDocs = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.Contains(resultWithoutDocs.Warnings, warning =>
                warning.Contains("/ingestion/docs/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));

            File.Delete(Path.Combine(conventional, "index.html"));
            spec.StaticAssets = allMappings;
            var resultWithoutConventional = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.Contains(resultWithoutConventional.Warnings, warning =>
                warning.Contains("/ingestion/conventional/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));

            File.WriteAllText(Path.Combine(conventional, "index.html"), "<h1>Conventional</h1>");
            spec.Features = [];
            spec.StaticAssets = allMappings;
            var resultWithoutSearch = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.Contains(resultWithoutSearch.Warnings, warning =>
                warning.Contains("/search/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));

            spec.Features = ["search"];
            spec.StaticAssets = spec.StaticAssets.Where(mapping => mapping.Source != "index.html").ToArray();
            var resultWithoutStaticHome = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.Contains(resultWithoutStaticHome.Warnings, warning =>
                warning.Contains("points to '/'", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_UsesStaticHtmlRoutesWhenNoCollectionsAreConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-static-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<h1>Home</h1>");
            File.WriteAllText(Path.Combine(root, "apps.html"), "<h1>Apps</h1>");
            var theme = Path.Combine(root, "themes", "static-theme");
            Directory.CreateDirectory(Path.Combine(theme, "assets"));
            File.WriteAllText(Path.Combine(theme, "theme.json"), "{\"name\":\"static-theme\",\"assetsPath\":\"./assets\"}");
            File.WriteAllText(Path.Combine(theme, "assets", "brand.svg"), "<svg/>");
            var spec = new SiteSpec
            {
                Name = "Static-only Navigation Test",
                BaseUrl = "https://example.test",
                DefaultTheme = "static-theme",
                ThemesRoot = "themes",
                DataRoot = "./payload",
                StaticAssets =
                [
                    new StaticAssetSpec { Source = "index.html", Destination = "./" },
                    new StaticAssetSpec { Source = "apps.html", Destination = "apps.html" }
                ],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items =
                            [
                                new MenuItemSpec { Title = "Home", Url = "/" },
                                new MenuItemSpec { Title = "Apps", Url = "/apps.html" },
                                new MenuItemSpec { Title = "Theme", Url = "/themes/static-theme/assets/brand.svg" },
                                new MenuItemSpec { Title = "Navigation data", Url = "/payload/site-nav.json" }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);
            var outputRoot = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outputRoot);
            Assert.True(File.Exists(Path.Combine(outputRoot, "themes", "static-theme", "assets", "brand.svg")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "payload", "site-nav.json")));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("points to '/'", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/apps.html", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/themes/static-theme/assets/brand.svg", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/payload/site-nav.json", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));

            spec.StaticAssets = spec.StaticAssets.Where(mapping => mapping.Source != "index.html").ToArray();
            var resultWithoutHome = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.Contains(resultWithoutHome.Warnings, warning =>
                warning.Contains("points to '/'", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));

            spec.StaticAssets =
            [
                new StaticAssetSpec { Source = "index.html", Destination = "index.html" }
            ];
            var resultWithoutApps = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.Contains(resultWithoutApps.Warnings, warning =>
                warning.Contains("/apps.html", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersFilesButNotUnsupportedDirectoryAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-static-aliases-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var assets = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.Combine(assets, "uppercase"));
            Directory.CreateDirectory(Path.Combine(assets, "legacy"));
            File.WriteAllText(Path.Combine(assets, "uppercase", "INDEX.HTML"), "<h1>Uppercase</h1>");
            File.WriteAllText(Path.Combine(assets, "legacy", "index.htm"), "<h1>Legacy</h1>");

            var spec = new SiteSpec
            {
                Name = "Static alias contract",
                BaseUrl = "https://example.test",
                StaticAssets = [new StaticAssetSpec { Source = "assets", Destination = "assets" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items =
                            [
                                new MenuItemSpec { Title = "Uppercase file", Url = "/assets/uppercase/INDEX.HTML" },
                                new MenuItemSpec { Title = "Legacy file", Url = "/assets/legacy/index.htm" },
                                new MenuItemSpec { Title = "Unsupported uppercase alias", Url = "/assets/uppercase/" },
                                new MenuItemSpec { Title = "Unsupported legacy alias", Url = "/assets/legacy/" }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/assets/uppercase/INDEX.HTML", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/assets/legacy/index.htm", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("'/assets/uppercase/'", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("'/assets/legacy/'", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_MatchesStaticRoutesWithHostAccuratePathSemantics()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-static-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var assets = Path.Combine(root, "assets");
            Directory.CreateDirectory(assets);
            Directory.CreateDirectory(Path.Combine(assets, "docs"));
            File.WriteAllText(Path.Combine(assets, "Guide.html"), "<h1>Guide</h1>");
            File.WriteAllText(Path.Combine(assets, "Uppercase.HTML"), "<h1>Uppercase</h1>");
            File.WriteAllText(Path.Combine(assets, "legacy.htm"), "<h1>Legacy</h1>");
            File.WriteAllText(Path.Combine(assets, "terms+conditions.html"), "<h1>Terms</h1>");
            File.WriteAllText(Path.Combine(assets, "manual.pdf"), "PDF");
            File.WriteAllText(Path.Combine(assets, "docs", "index.html"), "<h1>Docs</h1>");

            var spec = new SiteSpec
            {
                Name = "Static path semantics",
                BaseUrl = "https://example.test",
                StaticAssets = [new StaticAssetSpec { Source = "assets", Destination = "assets" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items =
                            [
                                new MenuItemSpec { Title = "Exact case", Url = "/assets/Guide.html" },
                                new MenuItemSpec { Title = "Wrong case", Url = "/assets/guide.html" },
                                new MenuItemSpec { Title = "Exact PDF", Url = "/assets/manual.pdf" },
                                new MenuItemSpec { Title = "PDF with slash", Url = "/assets/manual.pdf/" },
                                new MenuItemSpec { Title = "Literal reserved character", Url = "/assets/terms+conditions.html" },
                                new MenuItemSpec { Title = "Encoded reserved character", Url = "/assets/terms%2Bconditions.html" },
                                new MenuItemSpec { Title = "HTML extensionless", Url = "/assets/Guide" },
                                new MenuItemSpec { Title = "Uppercase HTML exact", Url = "/assets/Uppercase.HTML" },
                                new MenuItemSpec { Title = "Uppercase HTML extensionless", Url = "/assets/Uppercase" },
                                new MenuItemSpec { Title = "HTM extensionless", Url = "/assets/legacy" },
                                new MenuItemSpec { Title = "Directory alias without slash", Url = "/assets/docs" },
                                new MenuItemSpec { Title = "Wrong-case directory alias", Url = "/assets/Docs/" }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("points to '/assets/guide.html'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("points to '/assets/manual.pdf/'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("points to '/assets/Docs/'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("points to '/assets/Uppercase'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            foreach (var validUrl in new[]
                     {
                         "/assets/Guide.html",
                         "/assets/manual.pdf",
                         "/assets/terms+conditions.html",
                         "/assets/terms%2Bconditions.html",
                         "/assets/Guide",
                         "/assets/Uppercase.HTML",
                         "/assets/legacy",
                         "/assets/docs"
                     })
            {
                Assert.DoesNotContain(result.Warnings, warning =>
                    warning.Contains($"points to '{validUrl}'", StringComparison.Ordinal) &&
                    warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_PrefersGeneratedContentWhenStaticExtensionlessAliasCollides()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-static-content-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var content = Path.Combine(root, "content");
            var assets = Path.Combine(root, "assets");
            Directory.CreateDirectory(content);
            Directory.CreateDirectory(assets);
            File.WriteAllText(Path.Combine(content, "about.md"), "---\ntitle: About\n---\n\nAbout");
            File.WriteAllText(Path.Combine(assets, "about.html"), "<h1>Static about</h1>");

            var spec = new SiteSpec
            {
                Name = "Static/content collision",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                StaticAssets = [new StaticAssetSpec { Source = "assets", Destination = "." }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items = [new MenuItemSpec { Title = "About", Url = "/about/" }]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("points to '/about/'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersGeneratedRootPaginationRoutesWhenStaticAssetsEnableFullCoverage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-root-pagination-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var content = Path.Combine(root, "content");
            var assets = Path.Combine(root, "assets");
            Directory.CreateDirectory(content);
            Directory.CreateDirectory(assets);
            File.WriteAllText(Path.Combine(content, "_index.md"), "---\ntitle: Home\n---\n\nHome");
            File.WriteAllText(Path.Combine(content, "one.md"), "---\ntitle: One\n---\n\nOne");
            File.WriteAllText(Path.Combine(content, "two.md"), "---\ntitle: Two\n---\n\nTwo");
            File.WriteAllText(Path.Combine(content, "three.md"), "---\ntitle: Three\n---\n\nThree");
            File.WriteAllText(Path.Combine(assets, "site.css"), "body{}");

            var spec = new SiteSpec
            {
                Name = "Root pagination",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Pagination = new PaginationSpec { Enabled = true, PathSegment = "page", DefaultPageSize = 2 },
                Collections =
                [
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content",
                        Output = "/",
                        PageSize = 0
                    }
                ],
                StaticAssets = [new StaticAssetSpec { Source = "assets", Destination = "assets" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items = [new MenuItemSpec { Title = "Page 2", Url = "/page/2/" }]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("points to '/page/2/'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotRegisterPaginationRouteOccupiedByDraftContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-pagination-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content", "page", "2"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "_index.md"), "---\ntitle: Home\n---\nHome");
            File.WriteAllText(Path.Combine(root, "content", "one.md"), "---\ntitle: One\n---\nOne");
            File.WriteAllText(Path.Combine(root, "content", "two.md"), "---\ntitle: Two\n---\nTwo");
            File.WriteAllText(Path.Combine(root, "content", "page", "2", "index.md"), "---\ntitle: Reserved\ndraft: true\n---\nReserved");
            File.WriteAllText(Path.Combine(root, "assets", "site.css"), "body{}");

            var spec = new SiteSpec
            {
                Name = "Pagination collision",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Pagination = new PaginationSpec { Enabled = true, PathSegment = "page", DefaultPageSize = 1 },
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                StaticAssets = [new StaticAssetSpec { Source = "assets", Destination = "assets" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec { Name = "main", Items = [new MenuItemSpec { Title = "Page 2", Url = "/page/2/" }] }]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.Contains(result.Warnings, warning =>
                warning.Contains("points to '/page/2/'", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersSearchOnlyForSearchableContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-search-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<h1>Home</h1>");
            var content = Path.Combine(root, "content");
            Directory.CreateDirectory(content);
            File.WriteAllText(
                Path.Combine(content, "draft.md"),
                "---\ntitle: Draft\nslug: draft\ndraft: true\n---\n\nDraft");
            File.WriteAllText(
                Path.Combine(content, "404.md"),
                "---\ntitle: Not found\nslug: 404\n---\n\nNot found");

            var spec = new SiteSpec
            {
                Name = "Search route contract",
                BaseUrl = "https://example.test",
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                Features = ["search"],
                StaticAssets = [new StaticAssetSpec { Source = "index.html", Destination = "./" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items =
                            [
                                new MenuItemSpec { Title = "Search", Url = "/search/" },
                                new MenuItemSpec { Title = "Search index", Url = "/search/index" },
                                new MenuItemSpec { Title = "Search file", Url = "/search/index.html" },
                                new MenuItemSpec { Title = "Invalid search index directory", Url = "/search/index/" },
                                new MenuItemSpec { Title = "Invalid search file directory", Url = "/search/index.html/" }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var withoutSearchableContent = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.Contains(withoutSearchableContent.Warnings, warning =>
                warning.Contains("/search/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));

            File.WriteAllText(
                Path.Combine(content, "public.md"),
                "---\ntitle: Public\nslug: public\n---\n\nPublic");
            var withSearchableContent = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.DoesNotContain(withSearchableContent.Warnings, warning =>
                (warning.Contains("points to '/search/'", StringComparison.OrdinalIgnoreCase) ||
                 warning.Contains("points to '/search/index'", StringComparison.OrdinalIgnoreCase) ||
                 warning.Contains("points to '/search/index.html'", StringComparison.OrdinalIgnoreCase)) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(withSearchableContent.Warnings, warning =>
                warning.Contains("/search/index/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(withSearchableContent.Warnings, warning =>
                warning.Contains("/search/index.html/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_Registers_All_BuilderGenerated_Navigation_Routes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-generated-routes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blog = Path.Combine(root, "content", "blog");
            var pages = Path.Combine(root, "content", "pages", "404");
            var feeds = Path.Combine(root, "content", "feeds");
            var theme = Path.Combine(root, "themes", "route-theme");
            Directory.CreateDirectory(blog);
            Directory.CreateDirectory(pages);
            Directory.CreateDirectory(feeds);
            Directory.CreateDirectory(Path.Combine(theme, "assets"));
            File.WriteAllText(Path.Combine(blog, "post.md"), "---\ntitle: Post\n---\n\nSearchable post");
            File.WriteAllText(Path.Combine(pages, "index.md"), "---\ntitle: Not found\nslug: 404\n---\n\nNot found");
            File.WriteAllText(Path.Combine(pages, "manual.pdf"), "not found help");
            File.WriteAllText(Path.Combine(pages, "legacy.html"), "<h1>Legacy help</h1>");
            File.WriteAllText(Path.Combine(pages, "About Us.html"), "<h1>About help</h1>");
            File.WriteAllText(Path.Combine(feeds, "entry.md"), "---\ntitle: Feed entry\n---\n\nFeed entry");
            File.WriteAllText(Path.Combine(theme, "theme.json"), "{\"name\":\"route-theme\",\"assetsPath\":\"./assets\"}");
            File.WriteAllText(Path.Combine(theme, "assets", "brand.svg"), "<svg/>");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");

            var expectedRoutes = new[]
            {
                "/blog/",
                "/blog/index.json",
                "/404.html",
                "/manual.pdf",
                "/legacy.html",
                "/legacy",
                "/About%20Us.html",
                "/About%20Us",
                "/search/index.json",
                "/search/manifest.json",
                "/search/en/index.json",
                "/search/collections/blog/index.json",
                "/themes/route-theme/assets/brand.svg",
                "/payload/site-nav.json",
                "/feed-only/entry/index.json"
            };
            const string missingHtmlRoute = "/feed-only/entry/";
            var spec = new SiteSpec
            {
                Name = "Generated routes",
                BaseUrl = "https://example.test",
                DefaultTheme = "route-theme",
                ThemesRoot = "themes",
                DataRoot = "./payload",
                Features = ["search"],
                Collections =
                [
                    new CollectionSpec { Name = "blog", Input = "content/blog", Output = "/blog", AutoGenerateSectionIndex = true, Outputs = ["html", "json"] },
                    new CollectionSpec { Name = "pages", Input = "content/pages", Output = "/" },
                    new CollectionSpec { Name = "feeds", Input = "content/feeds", Output = "/feed-only", Outputs = ["json"] }
                ],
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items = expectedRoutes
                                .Append(missingHtmlRoute)
                                .Select(route => new MenuItemSpec { Title = route, Url = route })
                                .ToArray()
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);
            var outputRoot = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outputRoot);

            Assert.True(result.Success);
            foreach (var route in expectedRoutes)
            {
                Assert.DoesNotContain(result.Warnings, warning =>
                    warning.Contains($"points to '{route}'", StringComparison.Ordinal) &&
                    warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            }
            Assert.Contains(result.Warnings, warning =>
                warning.Contains($"points to '{missingHtmlRoute}'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            foreach (var relativePath in new[]
                     {
                         Path.Combine("blog", "index.html"),
                         Path.Combine("blog", "index.json"),
                         "404.html",
                         "manual.pdf",
                         Path.Combine("search", "index.json"),
                         Path.Combine("search", "manifest.json"),
                         Path.Combine("search", "en", "index.json"),
                         Path.Combine("search", "collections", "blog", "index.json"),
                         Path.Combine("themes", "route-theme", "assets", "brand.svg"),
                         Path.Combine("payload", "site-nav.json"),
                         Path.Combine("feed-only", "entry", "index.json")
                     })
            {
                Assert.True(File.Exists(Path.Combine(outputRoot, relativePath)), $"Expected generated file '{relativePath}'.");
            }
            Assert.False(File.Exists(Path.Combine(outputRoot, "feed-only", "entry", "index.html")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RejectsLinkedDirectoriesInStaticAssetMappings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-static-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var assets = Path.Combine(root, "assets");
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(assets);
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "index.html"), "<h1>Outside</h1>");
            Directory.CreateSymbolicLink(Path.Combine(assets, "linked"), outside);

            var spec = new SiteSpec
            {
                Name = "Linked Static Asset Test",
                BaseUrl = "https://example.test",
                StaticAssets = [new StaticAssetSpec { Source = "assets" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items = [new MenuItemSpec { Title = "Home", Url = "/" }]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath)));

            Assert.Contains("symbolic link", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_FailsWhenFrontMatterIsCollapsedOntoSingleLine()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-collapsed-frontmatter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "contact.md"),
                """
                --- title: Contact description: Broken localized page slug: contact language: fr layout: contact meta.raw_html: true ---
                <div class="ev-contact-info"><h2>Informations de contact</h2></div>
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Collapsed Front Matter Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/",
                        DefaultLayout = "page"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error =>
                error.Contains("Collapsed front matter detected", StringComparison.OrdinalIgnoreCase) &&
                error.Contains("content/pages/contact.md", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_FailsWhenRawHtmlPageStillContainsFrontMatterAndMarkdownSyntax()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-raw-html-markdown-leak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "contact.md"),
                """
                ---
                title: Contact
                slug: contact
                language: fr
                layout: contact
                meta.raw_html: true
                ---
                <div class="ev-contact-info">
                translation_key: contact
                # Contact
                - Office
                [Write to us](/contact/)
                </div>
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Raw Html Hygiene Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/",
                        DefaultLayout = "page"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error =>
                error.Contains("meta.raw_html=true", StringComparison.OrdinalIgnoreCase) &&
                error.Contains("front matter-like lines", StringComparison.OrdinalIgnoreCase) &&
                error.Contains("Markdown block syntax", StringComparison.OrdinalIgnoreCase) &&
                error.Contains("Markdown links or images", StringComparison.OrdinalIgnoreCase) &&
                error.Contains("content/pages/contact.md", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_AllowsRawHtmlPageWhenBodyIsActualHtml()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-raw-html-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "contact.md"),
                """
                ---
                title: Contact
                slug: contact
                layout: contact
                meta.raw_html: true
                ---
                <div class="ev-contact-info">
                  <h2>Contact</h2>
                  <p>Office location</p>
                  <a href="/contact/">Write to us</a>
                </div>
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Raw Html Safe Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/",
                        DefaultLayout = "page"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Errors, error =>
                error.Contains("meta.raw_html=true", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

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
    public void Verify_PreservesScalarCustomTaxonomyValuesContainingSeparators()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-taxonomy-scalar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "index.md"),
                "---\ntitle: Home\nslug: index\nseries: \"Alpha, Beta\"\n---\n\nHome");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");

            var spec = new SiteSpec
            {
                Name = "Scalar custom taxonomy",
                BaseUrl = "https://example.test",
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                Taxonomies = [new TaxonomySpec { Name = "series", BasePath = "/series" }],
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items =
                            [
                                new MenuItemSpec { Title = "Combined series", Url = "/series/alpha-beta/" },
                                new MenuItemSpec { Title = "Incorrect split alpha", Url = "/series/alpha/" },
                                new MenuItemSpec { Title = "Incorrect split beta", Url = "/series/beta/" }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("points to '/series/alpha-beta/'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("points to '/series/alpha/'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("points to '/series/beta/'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenVersioningAliasRedirectsAreMisconfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-versioning-aliases-" + Guid.NewGuid().ToString("N"));
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
                Name = "Verifier Version Alias Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Versioning = new VersioningSpec
                {
                    Enabled = true,
                    BasePath = "/docs",
                    GenerateAliasRedirects = true,
                    LtsAliasPath = "/docs/lts/",
                    Versions = new[]
                    {
                        new VersionSpec { Name = "v2", Url = "/docs/v2/", Latest = true, Aliases = new[] { "stable" } },
                        new VersionSpec { Name = "v1", Url = "/docs/v1/", Aliases = new[] { "/docs/stable/" } }
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
                warning.Contains("duplicate source '/docs/stable/'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("LtsAliasPath is set but no version is marked as Lts", StringComparison.OrdinalIgnoreCase));
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
                warning.Contains("looks like an editorial stream", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("has no landing page", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenEditorialPostHasNoDate()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-blog-date-missing-" + Guid.NewGuid().ToString("N"));
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

                Index
                """);
            File.WriteAllText(Path.Combine(blogPath, "first-post.md"),
                """
                ---
                title: First Post
                ---

                Hello
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Blog Date Test",
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

            Assert.Contains(result.Warnings, warning =>
                warning.Contains("missing front matter 'date'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotWarnWhenBlogCollectionAutoGeneratesLandingPage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-blog-landing-auto-" + Guid.NewGuid().ToString("N"));
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
                Name = "Verifier Blog Landing Auto Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        AutoGenerateSectionIndex = true
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("looks like an editorial stream", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("has no landing page", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotWarnWhenCustomCollectionUsesBlogPreset()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-blog-preset-custom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var updatesPath = Path.Combine(root, "content", "updates");
            Directory.CreateDirectory(updatesPath);
            File.WriteAllText(Path.Combine(updatesPath, "first-post.md"),
                """
                ---
                title: First Post
                ---

                Hello
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Blog Preset Custom Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "updates",
                        Preset = "blog",
                        Input = "content/updates",
                        Output = "/updates"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("looks like an editorial stream", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("has no landing page", StringComparison.OrdinalIgnoreCase));
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
    public void Verify_WarnsWhenRootRenderedLanguageMenuUsesPrefixedLocalRoute()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-root-rendered-nav-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var plPath = Path.Combine(root, "content", "pages", "pl");
            Directory.CreateDirectory(plPath);
            File.WriteAllText(Path.Combine(plPath, "contact.md"),
                """
                ---
                title: Kontakt
                slug: contact
                ---

                Kontakt
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Root Rendered Navigation Route Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    PrefixDefaultLanguage = true,
                    DetectFromPath = true,
                    Languages = new[]
                    {
                        new LanguageSpec { Code = "en", Prefix = "en", Default = true },
                        new LanguageSpec { Code = "pl", Prefix = "pl", RenderAtRoot = true }
                    }
                },
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main-pl",
                            Items = new[]
                            {
                                new MenuItemSpec { Title = "Kontakt", Url = "/pl/contact/" }
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
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("renderAtRoot=true", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("/pl/contact/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("public route '/contact/'", StringComparison.OrdinalIgnoreCase));
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
                warning.Contains("api-header/api-footer", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("header/footer", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void Verify_WarnsBestPracticeWhenThemeFallsBackToHeaderFooterForApiDocs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-theme-features-apidocs-fallback-" + Guid.NewGuid().ToString("N"));
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

            var themeRoot = Path.Combine(root, "themes", "feature-test-fallback");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "header.html"), "<header>Header</header>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "footer.html"), "<footer>Footer</footer>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "feature-test-fallback",
                  "engine": "scriban",
                  "defaultLayout": "home"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Theme Features Test Fallback",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "feature-test-fallback",
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
                warning.Contains("Best practice:", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("fall back to header/footer", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenApiHeaderDoesNotUseNavLinksToken()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-apidocs-header-navlinks-" + Guid.NewGuid().ToString("N"));
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

            var themeRoot = Path.Combine(root, "themes", "feature-test-navlinks");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "api-header.html"), "<header><a href=\"/\">Home</a></header>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "api-footer.html"), "<footer>{{YEAR}}</footer>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "feature-test-navlinks",
                  "engine": "scriban",
                  "defaultLayout": "home"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier API Header NavLinks Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "feature-test-navlinks",
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
                warning.Contains("does not contain '{{NAV_LINKS}}'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenApiHeaderMissingNavActionsTokenButActionsConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-apidocs-header-actions-" + Guid.NewGuid().ToString("N"));
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

            var themeRoot = Path.Combine(root, "themes", "feature-test-actions");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "api-header.html"), "<header>{{NAV_LINKS}}</header>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "api-footer.html"), "<footer>{{YEAR}}</footer>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "feature-test-actions",
                  "engine": "scriban",
                  "defaultLayout": "home"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier API Header Actions Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "feature-test-actions",
                ThemesRoot = "themes",
                Features = new[] { "apiDocs" },
                Navigation = new NavigationSpec
                {
                    Actions = new[]
                    {
                        new MenuItemSpec
                        {
                            Title = "Install",
                            Url = "https://example.test/install",
                            External = true
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
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("does not contain '{{NAV_ACTIONS}}'", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenApiDocsFallbackHeaderContainsScribanNavExpressions()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-apidocs-fallback-scriban-" + Guid.NewGuid().ToString("N"));
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

            var themeRoot = Path.Combine(root, "themes", "feature-test-fallback-scriban");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"), "<html>{{ content }}</html>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "header.html"), "<header>{{ pf.nav_links \"main\" }}</header>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "footer.html"), "<footer>{{ site.name }}</footer>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "feature-test-fallback-scriban",
                  "engine": "scriban",
                  "defaultLayout": "home"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier API Header Fallback Scriban Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "feature-test-fallback-scriban",
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
                warning.Contains("contains Scriban navigation expressions", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
    [Fact]
    public void Verify_UsesStaticRoutesForNavigationPatterns()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-static-patterns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "static", "docs"));
        try
        {
            File.WriteAllText(Path.Combine(root, "static", "docs", "index.html"), "<h1>Docs</h1>");
            File.WriteAllText(Path.Combine(root, "static", "About Us.html"), "<h1>About</h1>");
            File.WriteAllText(Path.Combine(root, "static", "guide#old.html"), "<h1>Guide</h1>");
            var spec = new SiteSpec
            {
                Name = "Static patterns",
                BaseUrl = "https://example.test",
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec
                    {
                        Name = "main",
                        Visibility = new NavigationVisibilitySpec { Paths = ["/docs/**", "/About%20Us.html", "/guide%23old.html"] },
                        Items =
                        [
                            new MenuItemSpec { Title = "Docs", Url = "/docs/", Match = "/docs/**" },
                            new MenuItemSpec { Title = "About", Url = "/About%20Us.html", Match = "/About%20Us.html" },
                            new MenuItemSpec { Title = "Guide", Url = "/guide%23old.html", Match = "/guide%23old.html" }
                        ]
                    }],
                    Profiles =
                    [
                        new NavigationProfileSpec { Name = "docs", Paths = ["/docs/**"] },
                        new NavigationProfileSpec { Name = "about", Paths = ["/About%20Us.html"] },
                        new NavigationProfileSpec { Name = "guide", Paths = ["/guide%23old.html"] }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("do not match any generated route", StringComparison.OrdinalIgnoreCase) ||
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersTaxonomyPaginationRoutes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-taxonomy-pagination-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        try
        {
            File.WriteAllText(Path.Combine(root, "content", "one.md"), "---\ntitle: One\ntags: [release, security]\n---\nOne");
            File.WriteAllText(Path.Combine(root, "content", "two.md"), "---\ntitle: Two\ntags: [release]\n---\nTwo");
            File.WriteAllText(Path.Combine(root, "assets", "site.css"), "body{}");
            var spec = new SiteSpec
            {
                Name = "Taxonomy pagination",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                Taxonomies = [new TaxonomySpec { Name = "tags", BasePath = "/tags", PageSize = 1 }],
                StaticAssets = [new StaticAssetSpec { Source = "assets", Destination = "assets" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec
                    {
                        Name = "main",
                        Items =
                        [
                            new MenuItemSpec { Title = "Tag page 2", Url = "/tags/page/2/" },
                            new MenuItemSpec { Title = "Release page 2", Url = "/tags/release/page/2/" }
                        ]
                    }]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("page/2", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersMaterializedFallbackAndAlternateOutputRoutesButNotDrafts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-materialized-routes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content", "en"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        try
        {
            File.WriteAllText(Path.Combine(root, "content", "en", "_index.md"), "---\ntitle: Blog\ntranslation_key: blog\n---\nBlog");
            File.WriteAllText(Path.Combine(root, "content", "en", "one.md"), "---\ntitle: One\ntags: [release]\n---\nOne");
            File.WriteAllText(Path.Combine(root, "content", "en", "two.md"), "---\ntitle: Two\ntags: [release]\n---\nTwo");
            File.WriteAllText(Path.Combine(root, "content", "en", "manual.pdf"), "manual");
            File.WriteAllText(Path.Combine(root, "content", "en", "draft.md"), "---\ntitle: Draft\ndraft: true\n---\nDraft");
            File.WriteAllText(Path.Combine(root, "assets", "site.css"), "body{}");
            var spec = new SiteSpec
            {
                Name = "Materialized routes",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    DetectFromPath = true,
                    FallbackToDefaultLanguage = true,
                    MaterializeFallbackPages = true,
                    Languages = [new LanguageSpec { Code = "en", Default = true }, new LanguageSpec { Code = "pl" }]
                },
                Collections = [new CollectionSpec { Name = "blog", Preset = "blog", Input = "content", Output = "/blog", PageSize = 1 }],
                Taxonomies = [new TaxonomySpec { Name = "tags", BasePath = "/tags", Outputs = ["html", "rss"] }],
                StaticAssets = [new StaticAssetSpec { Source = "assets", Destination = "assets" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec
                    {
                        Name = "main",
                        Items =
                        [
                            new MenuItemSpec { Title = "Polish fallback", Url = "/pl/blog/" },
                            new MenuItemSpec { Title = "Polish fallback page 2", Url = "/pl/blog/page/2/" },
                            new MenuItemSpec { Title = "Blog feed", Url = "/blog/index.xml" },
                            new MenuItemSpec { Title = "Invalid feed directory", Url = "/blog/index.xml/" },
                            new MenuItemSpec { Title = "Bundle resource", Url = "/pl/blog/manual.pdf" },
                            new MenuItemSpec { Title = "Paginated bundle resource", Url = "/pl/blog/page/2/manual.pdf" },
                            new MenuItemSpec { Title = "Polish fallback taxonomy", Url = "/pl/tags/" },
                            new MenuItemSpec { Title = "Polish fallback taxonomy feed", Url = "/pl/tags/index.xml" },
                            new MenuItemSpec { Title = "Draft", Url = "/blog/draft/" }
                        ]
                    }]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.DoesNotContain(result.Warnings, warning =>
                (warning.Contains("/pl/blog/", StringComparison.OrdinalIgnoreCase) ||
                 warning.Contains("/blog/index.xml'", StringComparison.OrdinalIgnoreCase) ||
                 warning.Contains("/pl/tags/", StringComparison.OrdinalIgnoreCase)) &&
                 warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("/blog/index.xml/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("/blog/draft/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotRegisterLocalizedTaxonomyOutputsWithoutLocalizedTerms()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-localized-taxonomy-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content", "en"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        try
        {
            File.WriteAllText(Path.Combine(root, "content", "en", "one.md"), "---\ntitle: One\ntags: [release]\n---\nOne");
            File.WriteAllText(Path.Combine(root, "assets", "site.css"), "body{}");
            var spec = new SiteSpec
            {
                Name = "Localized taxonomy without terms",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    DetectFromPath = true,
                    FallbackToDefaultLanguage = false,
                    MaterializeFallbackPages = false,
                    Languages = [new LanguageSpec { Code = "en", Default = true }, new LanguageSpec { Code = "pl" }]
                },
                Collections = [new CollectionSpec { Name = "blog", Input = "content", Output = "/blog" }],
                Taxonomies = [new TaxonomySpec { Name = "tags", BasePath = "/tags", Outputs = ["html", "rss"] }],
                StaticAssets = [new StaticAssetSpec { Source = "assets", Destination = "assets" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec
                    {
                        Name = "main",
                        Items =
                        [
                            new MenuItemSpec { Title = "English feed", Url = "/tags/index.xml" },
                            new MenuItemSpec { Title = "Missing Polish feed", Url = "/pl/tags/index.xml" }
                        ]
                    }]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("points to '/tags/index.xml'", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("/pl/tags/index.xml", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersMaterializedFallbackForMultiSegmentLanguagePrefixes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-multi-prefix-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "_index.md"), "---\ntitle: Docs\ntranslation_key: docs\n---\nDocs");
            File.WriteAllText(Path.Combine(root, "assets", "site.css"), "body{}");
            var spec = new SiteSpec
            {
                Name = "Multi-prefix fallback",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    PrefixDefaultLanguage = true,
                    DetectFromPath = true,
                    FallbackToDefaultLanguage = true,
                    MaterializeFallbackPages = true,
                    Languages =
                    [
                        new LanguageSpec { Code = "en", Prefix = "en/us", Default = true },
                        new LanguageSpec { Code = "pl", Prefix = "pl/pl" }
                    ]
                },
                Collections = [new CollectionSpec { Name = "docs", Input = "content", Output = "/docs" }],
                StaticAssets = [new StaticAssetSpec { Source = "assets", Destination = "assets" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec { Name = "main", Items = [new MenuItemSpec { Title = "Polish docs", Url = "/pl/pl/docs/" }] }]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("points to '/pl/pl/docs/'", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersDeterministicGeneratedSocialCardRoute()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-social-card-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "index.md"), "---\ntitle: Home\ndescription: Generated card route.\nslug: index\n---\nHome");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            var spec = new SiteSpec
            {
                Name = "Social route",
                BaseUrl = "https://example.test",
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Social = new SocialSpec
                {
                    Enabled = true,
                    SiteName = "Social route",
                    AutoGenerateCards = true,
                    GeneratedCardsPath = "/assets/social/generated"
                },
                Navigation = new NavigationSpec { AutoDefaults = false }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outputRoot);
            var (matter, _) = FrontMatterParser.Parse(File.ReadAllText(Path.Combine(root, "content", "index.md")));
            var cardRoute = WebSiteBuilder.ResolveGeneratedSocialCardRoute(spec, new ContentItem
            {
                SourcePath = Path.Combine(root, "content", "index.md"),
                Collection = "pages",
                OutputPath = "/",
                Title = "Home",
                Description = "Generated card route.",
                Slug = "index",
                Kind = PageKind.Home,
                HtmlContent = "<p>Home</p>",
                Meta = matter?.Meta ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            });
            Assert.StartsWith("/assets/social/generated/", cardRoute, StringComparison.Ordinal);

            string html = File.ReadAllText(Path.Combine(outputRoot, "index.html"));
            const string marker = "property=\"og:image\" content=\"https://example.test";
            int start = html.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                start += marker.Length;
                int end = html.IndexOf('"', start);
                string renderedCardRoute = html[start..end];
                Assert.Equal(cardRoute, renderedCardRoute);
                Assert.True(File.Exists(Path.Combine(outputRoot, renderedCardRoute.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
            }

            spec.Navigation.Menus = [new MenuSpec { Name = "main", Items = [new MenuItemSpec { Title = "Card", Url = cardRoute }] }];
            WebVerifyResult result = WebSiteVerifier.Verify(spec, plan);

            var missingRouteWarnings = result.Warnings.Where(warning =>
                warning.Contains($"points to '{cardRoute}'", StringComparison.Ordinal) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.True(missingRouteWarnings.Length == 0, string.Join(Environment.NewLine, missingRouteWarnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersGeneratedSectionAndPaginationSocialCardRoutes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-generated-social-routes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content", "blog"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "blog", "first.md"),
                "---\ntitle: First\ndate: 2026-01-02\n---\n\nFirst");
            File.WriteAllText(Path.Combine(root, "content", "blog", "second.md"),
                "---\ntitle: Second\ndate: 2026-01-01\n---\n\nSecond");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");

            var collection = new CollectionSpec
            {
                Name = "blog",
                Input = "content/blog",
                Output = "/blog",
                AutoGenerateSectionIndex = true,
                AutoSectionTitle = "News",
                AutoSectionDescription = "Generated listing.",
                PageSize = 1
            };
            var spec = new SiteSpec
            {
                Name = "Generated social routes",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [collection],
                Pagination = new PaginationSpec { Enabled = true, PathSegment = "page", DefaultPageSize = 1 },
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Social = new SocialSpec
                {
                    Enabled = true,
                    SiteName = "Generated social routes",
                    AutoGenerateCards = true,
                    GeneratedCardsPath = "/assets/social/generated"
                },
                Navigation = new NavigationSpec { AutoDefaults = false }
            };
            var sectionItem = WebSiteBuilder.CreateAutoGeneratedSectionIndexItem(
                collection,
                "/blog/",
                "en",
                projectSlug: null);
            var paginatedItem = WebSiteBuilder.CloneContentItem(sectionItem);
            paginatedItem.OutputPath = "/blog/page/2/";
            paginatedItem.Outputs = ["html"];
            paginatedItem.TranslationKey = "blog:_index:page:2";
            var sectionCardRoute = WebSiteBuilder.ResolveGeneratedSocialCardRoute(spec, sectionItem);
            var paginationCardRoute = WebSiteBuilder.ResolveGeneratedSocialCardRoute(spec, paginatedItem);
            Assert.NotEqual(sectionCardRoute, paginationCardRoute);
            spec.Navigation.Menus =
            [
                new MenuSpec
                {
                    Name = "main",
                    Items =
                    [
                        new MenuItemSpec { Title = "Section card", Url = sectionCardRoute },
                        new MenuItemSpec { Title = "Page 2 card", Url = paginationCardRoute }
                    ]
                }
            ];
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            foreach (var route in new[] { sectionCardRoute, paginationCardRoute })
            {
                Assert.StartsWith("/assets/social/generated/", route, StringComparison.Ordinal);
                Assert.DoesNotContain(result.Warnings, warning =>
                    warning.Contains($"points to '{route}'", StringComparison.Ordinal) &&
                    warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
