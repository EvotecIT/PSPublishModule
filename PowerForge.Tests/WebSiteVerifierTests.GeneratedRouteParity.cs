using PowerForge.Web;

public partial class WebSiteVerifierTests
{
    [Fact]
    public void Verify_MatchesCustomHtmlSocialCardEmissionAndRendererAvailability()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-social-custom-format-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(
                Path.Combine(root, "content", "index.md"),
                "---\ntitle: Home\nslug: index\n---\nCustom HTML renderer.");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            var spec = new SiteSpec
            {
                Name = "Custom HTML social route",
                BaseUrl = "https://example.test",
                Outputs = new OutputsSpec
                {
                    Formats = [new OutputFormatSpec { Name = "legacy", MediaType = "text/html", Suffix = "htm" }]
                },
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/", Outputs = ["legacy"] }],
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Social = new SocialSpec { Enabled = true, AutoGenerateCards = true },
                Navigation = new NavigationSpec { AutoDefaults = false }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var item = Assert.Single(WebSiteBuilder.BuildContentItemsForVerification(spec, plan));
            var cardRoute = WebSiteBuilder.ResolveGeneratedSocialCardRoute(spec, item, root);
            Assert.StartsWith("/assets/social/generated/", cardRoute, StringComparison.Ordinal);
            spec.Navigation.Menus =
            [
                new MenuSpec
                {
                    Name = "main",
                    Items = [new MenuItemSpec { Title = "Generated card", Url = cardRoute }]
                }
            ];

            var outputRoot = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outputRoot);
            var result = WebSiteVerifier.Verify(spec, plan);
            var cardPath = Path.Combine(outputRoot, cardRoute.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            var missingRouteWarning = HasMissingRouteWarning(result, cardRoute);

            Assert.True(File.Exists(Path.Combine(outputRoot, "index.htm")));
            using (var sitemap = System.Text.Json.JsonDocument.Parse(
                       File.ReadAllText(Path.Combine(outputRoot, "_powerforge", "sitemap-entries.json"))))
            {
                Assert.Contains(
                    sitemap.RootElement.GetProperty("entries").EnumerateArray(),
                    entry => entry.GetProperty("path").GetString() == "/index.htm");
                Assert.DoesNotContain(
                    sitemap.RootElement.GetProperty("entries").EnumerateArray(),
                    entry => entry.GetProperty("path").GetString() == "/");
            }
            var generatedSitemap = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = outputRoot,
                BaseUrl = spec.BaseUrl,
                IncludeHtmlFiles = false,
                IncludeTextFiles = false
            });
            var sitemapXml = File.ReadAllText(generatedSitemap.OutputPath);
            Assert.Contains("<loc>https://example.test/index.htm</loc>", sitemapXml, StringComparison.Ordinal);
            Assert.DoesNotContain("<loc>https://example.test/</loc>", sitemapXml, StringComparison.Ordinal);
            Assert.Equal(File.Exists(cardPath), !missingRouteWarning);
            Assert.Equal(WebSocialCardGenerator.IsPngRenderingAvailable(), File.Exists(cardPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_TrustsRenderedNoIndexForArbitraryHtmlFormatSuffix()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-custom-html-noindex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(
                Path.Combine(root, "content", "guide.md"),
                "---\ntitle: Guide\n---\n<meta name=\"robots\" content=\"noindex\">\nGuide");
            var spec = new SiteSpec
            {
                Name = "Custom HTML noindex",
                BaseUrl = "https://example.test",
                Outputs = new OutputsSpec
                {
                    Formats = [new OutputFormatSpec { Name = "legacy", MediaType = "text/html", Suffix = "xhtml" }]
                },
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/", Outputs = ["legacy"] }]
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);

            var renderedPath = Path.Combine(outputRoot, "guide", "index.xhtml");
            Assert.True(File.Exists(renderedPath));
            using var metadata = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(outputRoot, "_powerforge", "sitemap-entries.json")));
            var entry = Assert.Single(metadata.RootElement.GetProperty("entries").EnumerateArray());
            Assert.Equal("/guide/index.xhtml", entry.GetProperty("path").GetString());
            Assert.True(entry.GetProperty("noIndex").GetBoolean());

            var sitemap = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = outputRoot,
                BaseUrl = spec.BaseUrl,
                IncludeHtmlFiles = false,
                IncludeTextFiles = false
            });
            Assert.DoesNotContain("guide/index.xhtml", File.ReadAllText(sitemap.OutputPath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersEveryExactGeneratedRedirectSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-generated-redirects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "index.md"), "---\ntitle: Home\nslug: index\n---\nHome");
            File.WriteAllText(
                Path.Combine(root, "content", "guide.md"),
                "---\ntitle: Guide\naliases: [\"/legacy-guide/\"]\n---\nGuide");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            var redirectRoutes = new[]
            {
                "/configured-old/",
                "/legacy-file.html",
                "/legacy-guide/",
                "/docs/latest/",
                "/docs/stable/",
                "/guide/amp/"
            };
            const string invalidFileRedirectDirectory = "/legacy-file.html/";
            const string invalidSlashlessRedirect = "/configured-old";
            var spec = new SiteSpec
            {
                Name = "Generated redirect navigation routes",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                EnableLegacyAmpRedirects = true,
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Redirects =
                [
                    new RedirectSpec { From = "/configured-old/", To = "/guide/" },
                    new RedirectSpec { From = "/legacy-file.html", To = "/guide/" }
                ],
                Versioning = new VersioningSpec
                {
                    Enabled = true,
                    BasePath = "/docs",
                    GenerateAliasRedirects = true,
                    Versions =
                    [
                        new VersionSpec
                        {
                            Name = "v2",
                            Url = "/docs/v2/",
                            Latest = true,
                            Aliases = ["stable"]
                        }
                    ]
                },
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus =
                    [
                        new MenuSpec
                        {
                            Name = "main",
                            Items = redirectRoutes
                                .Prepend("/")
                                .Append(invalidFileRedirectDirectory)
                                .Append(invalidSlashlessRedirect)
                                .Select((route, index) => new MenuItemSpec { Title = $"Redirect {index}", Url = route })
                                .ToArray()
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);
            var result = WebSiteVerifier.Verify(spec, plan);
            var netlifyRedirects = File.ReadAllText(Path.Combine(outputRoot, "_redirects"));

            Assert.All(redirectRoutes, route =>
            {
                Assert.Contains(route.TrimEnd('/'), netlifyRedirects, StringComparison.OrdinalIgnoreCase);
                Assert.False(HasMissingRouteWarning(result, route), string.Join(Environment.NewLine, result.Warnings));
            });
            Assert.True(
                HasMissingRouteWarning(result, invalidFileRedirectDirectory),
                string.Join(Environment.NewLine, result.Warnings));
            Assert.True(
                HasMissingRouteWarning(result, invalidSlashlessRedirect),
                string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersExactGeneratedRedirectSourcesWithoutCollections()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-static-only-redirects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var spec = new SiteSpec
            {
                Name = "Static-only redirect navigation routes",
                BaseUrl = "https://example.test",
                Redirects = [new RedirectSpec { From = "/legacy-file.html", To = "/" }],
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
                                new MenuItemSpec { Title = "Legacy", Url = "/legacy-file.html" },
                                new MenuItemSpec { Title = "Invalid directory", Url = "/legacy-file.html/" }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);

            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.False(HasMissingRouteWarning(result, "/legacy-file.html"), string.Join(Environment.NewLine, result.Warnings));
            Assert.True(HasMissingRouteWarning(result, "/legacy-file.html/"), string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_ProjectsPaginationForDistinctTaxonomyTermsWithCollidingSlugs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-taxonomy-slug-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "one.md"), "---\ntitle: One\ntags: [\"C#\"]\n---\nOne");
            File.WriteAllText(Path.Combine(root, "content", "two.md"), "---\ntitle: Two\ntags: [\"C++\"]\n---\nTwo");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            const string pageTwoRoute = "/tags/page/2/";
            var spec = new SiteSpec
            {
                Name = "Taxonomy slug collision pagination",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                Taxonomies = [new TaxonomySpec { Name = "tags", BasePath = "/tags", PageSize = 1 }],
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec { Name = "main", Items = [new MenuItemSpec { Title = "Tag page 2", Url = pageTwoRoute }] }]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(File.Exists(Path.Combine(outputRoot, "tags", "page", "2", "index.html")));
            Assert.False(HasMissingRouteWarning(result, pageTwoRoute), string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_ProjectsTermPaginationForLaterTaxonomyTermWithCollidingSlug()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-taxonomy-term-slug-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "csharp.md"), "---\ntitle: C sharp\ntags: [\"C#\"]\n---\nC sharp");
            File.WriteAllText(Path.Combine(root, "content", "cplusplus-one.md"), "---\ntitle: C plus plus one\ntags: [\"C++\"]\n---\nOne");
            File.WriteAllText(Path.Combine(root, "content", "cplusplus-two.md"), "---\ntitle: C plus plus two\ntags: [\"C++\"]\n---\nTwo");
            File.WriteAllText(Path.Combine(root, "content", "cplusplus-three.md"), "---\ntitle: C plus plus three\ntags: [\"C++\"]\n---\nThree");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            const string pageTwoRoute = "/tags/c/page/2/";
            const string pageThreeRoute = "/tags/c/page/3/";
            var spec = new SiteSpec
            {
                Name = "Taxonomy term slug collision pagination",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                Taxonomies = [new TaxonomySpec { Name = "tags", BasePath = "/tags", PageSize = 1 }],
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
                                new MenuItemSpec { Title = "Tag page 2", Url = pageTwoRoute },
                                new MenuItemSpec { Title = "Tag page 3", Url = pageThreeRoute }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(File.Exists(Path.Combine(outputRoot, "tags", "c", "page", "2", "index.html")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "tags", "c", "page", "3", "index.html")));
            Assert.False(HasMissingRouteWarning(result, pageTwoRoute), string.Join(Environment.NewLine, result.Warnings));
            Assert.False(HasMissingRouteWarning(result, pageThreeRoute), string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_ProjectsEmittedTaxonomySocialCardsIncludingPagination()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-taxonomy-social-cards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(
                Path.Combine(root, "content", "one.md"),
                "---\ntitle: One\nblog: [alpha, shared]\n---\nOne");
            File.WriteAllText(
                Path.Combine(root, "content", "two.md"),
                "---\ntitle: Two\nblog: [shared]\n---\nTwo");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            var spec = new SiteSpec
            {
                Name = "Taxonomy social card projection",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                Taxonomies = [new TaxonomySpec { Name = "blog", BasePath = "/topics", PageSize = 1 }],
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Social = new SocialSpec { Enabled = true, AutoGenerateCards = true },
                Navigation = new NavigationSpec { AutoDefaults = false }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);
            var generatedCardsRoot = Path.Combine(outputRoot, "assets", "social", "generated");
            var taxonomyCards = Directory.Exists(generatedCardsRoot)
                ? Directory.GetFiles(generatedCardsRoot, "topics-*.png", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            Assert.Equal(WebSocialCardGenerator.IsPngRenderingAvailable() ? 5 : 0, taxonomyCards.Length);
            if (taxonomyCards.Length == 0)
                return;

            var cardRoutes = taxonomyCards
                .Select(path => "/assets/social/generated/" + Path.GetFileName(path))
                .OrderBy(static route => route, StringComparer.Ordinal)
                .ToArray();
            spec.Navigation.Menus =
            [
                new MenuSpec
                {
                    Name = "main",
                    Items = cardRoutes
                        .Select((route, index) => new MenuItemSpec { Title = $"Taxonomy card {index + 1}", Url = route })
                        .ToArray()
                }
            ];

            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(File.Exists(Path.Combine(outputRoot, "topics", "index.html")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "topics", "page", "2", "index.html")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "topics", "shared", "page", "2", "index.html")));
            Assert.All(cardRoutes, route =>
                Assert.False(HasMissingRouteWarning(result, route), string.Join(Environment.NewLine, result.Warnings)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_KeepsNotFoundFileOutOfDirectoryRouteMatching()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-not-found-file-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "404.md"), "---\ntitle: Not found\n---\nMissing");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            const string fileRoute = "/404.html";
            const string invalidDirectoryRoute = "/404.html/";
            var spec = new SiteSpec
            {
                Name = "Not found route parity",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
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
                                new MenuItemSpec { Title = "Not found file", Url = fileRoute },
                                new MenuItemSpec { Title = "Invalid not found directory", Url = invalidDirectoryRoute }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(File.Exists(Path.Combine(outputRoot, "404.html")));
            Assert.False(HasMissingRouteWarning(result, fileRoute), string.Join(Environment.NewLine, result.Warnings));
            Assert.True(HasMissingRouteWarning(result, invalidDirectoryRoute), string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_PreservesLiteralPercentEscapesForGeneratedFilesAndResources()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-literal-percent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content", "encoded"));

        try
        {
            File.WriteAllText(
                Path.Combine(root, "content", "encoded", "index.md"),
                "---\ntitle: Encoded\nslug: About%20Us\n---\nLiteral escape.");
            File.WriteAllText(Path.Combine(root, "content", "encoded", "manual.pdf"), "manual");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            const string pageRoute = "/About%2520Us/";
            const string resourceRoute = "/About%2520Us/manual.pdf";
            var spec = new SiteSpec
            {
                Name = "Literal percent route",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
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
                                new MenuItemSpec { Title = "Literal escape page", Url = pageRoute },
                                new MenuItemSpec { Title = "Literal escape resource", Url = resourceRoute }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(File.Exists(Path.Combine(outputRoot, "About%20Us", "index.html")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "About%20Us", "manual.pdf")));
            Assert.False(HasMissingRouteWarning(result, pageRoute), string.Join(Environment.NewLine, result.Warnings));
            Assert.False(HasMissingRouteWarning(result, resourceRoute), string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RegistersGeneratedTaxonomyTermWhenDraftContentOccupiesItsRoute()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-taxonomy-draft-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content", "blog"));
        Directory.CreateDirectory(Path.Combine(root, "content", "pages", "tags", "foo"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "blog", "post.md"), "---\ntitle: Post\ndate: 2026-08-30\ntags: [foo]\n---\nPost");
            File.WriteAllText(Path.Combine(root, "content", "pages", "tags", "foo", "index.md"), "---\ntitle: Reserved\ndraft: true\n---\nReserved");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            const string termRoute = "/tags/foo/";
            var spec = new SiteSpec
            {
                Name = "Taxonomy collision",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Collections =
                [
                    new CollectionSpec { Name = "blog", Preset = "blog", Input = "content/blog", Output = "/blog" },
                    new CollectionSpec { Name = "pages", Input = "content/pages", Output = "/" }
                ],
                Taxonomies = [new TaxonomySpec { Name = "tags", BasePath = "/tags" }],
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec { Name = "main", Items = [new MenuItemSpec { Title = "Foo", Url = termRoute }] }]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(File.Exists(Path.Combine(outputRoot, "tags", "foo", "index.html")));
            Assert.Contains(result.Warnings, warning => warning.Contains("overlaps content route", StringComparison.OrdinalIgnoreCase));
            Assert.False(HasMissingRouteWarning(result, termRoute), string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_ReservesMaterializedFallbackTranslationKeysBetweenItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-fallback-reservation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "first.md"), "---\ntitle: First\ntranslation_key: shared\n---\nFirst");
            File.WriteAllText(Path.Combine(root, "content", "second.md"), "---\ntitle: Second\ntranslation_key: shared\n---\nSecond");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            const string firstRoute = "/pl/first/";
            const string secondRoute = "/pl/second/";
            var spec = new SiteSpec
            {
                Name = "Fallback reservation",
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
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
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
                                new MenuItemSpec { Title = "First fallback", Url = firstRoute },
                                new MenuItemSpec { Title = "Second fallback", Url = secondRoute }
                            ]
                        }
                    ]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);
            var result = WebSiteVerifier.Verify(spec, plan);
            var firstExists = File.Exists(Path.Combine(outputRoot, "pl", "first", "index.html"));
            var secondExists = File.Exists(Path.Combine(outputRoot, "pl", "second", "index.html"));

            Assert.NotEqual(firstExists, secondExists);
            Assert.Equal(firstExists, !HasMissingRouteWarning(result, firstRoute));
            Assert.Equal(secondExists, !HasMissingRouteWarning(result, secondRoute));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static bool HasMissingRouteWarning(WebVerifyResult result, string route)
        => result.Warnings.Any(warning =>
            warning.Contains($"points to '{route}'", StringComparison.Ordinal) &&
            warning.Contains("does not match any generated route", StringComparison.OrdinalIgnoreCase));
}
