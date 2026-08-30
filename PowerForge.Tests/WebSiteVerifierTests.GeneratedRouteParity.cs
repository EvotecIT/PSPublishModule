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
