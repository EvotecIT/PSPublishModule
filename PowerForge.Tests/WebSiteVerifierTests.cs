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
            var spec = new SiteSpec
            {
                Name = "Static-only Navigation Test",
                BaseUrl = "https://example.test",
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
                                new MenuItemSpec { Title = "Apps", Url = "/apps.html" }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("points to '/'", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("/apps.html", StringComparison.OrdinalIgnoreCase) &&
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
                Pagination = new PaginationSpec { Enabled = true, PathSegment = "page" },
                Collections =
                [
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content",
                        Output = "/",
                        PageSize = 2
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
                            Items = [new MenuItemSpec { Title = "Search", Url = "/search/" }]
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
                warning.Contains("/search/", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
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
}
