using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

public partial class WebSiteVerifierTests
{
    [Fact]
    public void Verify_DoesNotProjectAnObservedFailedSocialCardRender()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-failed-social-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));
        string? cardRoute = null;

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "index.md"), "---\ntitle: Home\nslug: index\n---\nHome");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            var spec = new SiteSpec
            {
                Name = "Failed social-card render",
                BaseUrl = "https://example.test",
                Collections = [new CollectionSpec { Name = "pages", Input = "content", Output = "/" }],
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Social = new SocialSpec { Enabled = true, AutoGenerateCards = true },
                Navigation = new NavigationSpec { AutoDefaults = false }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var item = Assert.Single(WebSiteBuilder.BuildContentItemsForVerification(spec, plan));
            cardRoute = WebSiteBuilder.ResolveGeneratedSocialCardRoute(spec, item, root);
            Assert.StartsWith("/assets/social/generated/", cardRoute, StringComparison.Ordinal);
            spec.Navigation.Menus =
            [
                new MenuSpec { Name = "main", Items = [new MenuItemSpec { Title = "Failed card", Url = cardRoute }] }
            ];
            WebSiteBuilder.RecordFailedGeneratedSocialCardRenderForTesting(cardRoute, root);

            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(HasMissingRouteWarning(result, cardRoute), string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(cardRoute))
                WebSiteBuilder.ClearGeneratedSocialCardRenderOutcomeForTesting(cardRoute, root);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_ProjectsPaginationForTaxonomyTermsWithEmptySlugs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-taxonomy-empty-slug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "one.md"), "---\ntitle: One\ntags: [\"+++\"]\n---\nOne");
            File.WriteAllText(Path.Combine(root, "content", "two.md"), "---\ntitle: Two\ntags: [\"+++\"]\n---\nTwo");
            File.WriteAllText(Path.Combine(root, "content", "three.md"), "---\ntitle: Three\ntags: [\"+++\"]\n---\nThree");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            const string pageTwoRoute = "/tags/page/2/";
            const string pageThreeRoute = "/tags/page/3/";
            var spec = new SiteSpec
            {
                Name = "Empty taxonomy slug pagination",
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

            Assert.True(File.Exists(Path.Combine(outputRoot, "tags", "page", "2", "index.html")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "tags", "page", "3", "index.html")));
            Assert.False(HasMissingRouteWarning(result, pageTwoRoute), string.Join(Environment.NewLine, result.Warnings));
            Assert.False(HasMissingRouteWarning(result, pageThreeRoute), string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_ValidatesRoutesWhenConfiguredStaticAssetsAreMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-missing-static-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "guide.md"), "---\ntitle: Guide\n---\nGuide");
            const string missingAssetRoute = "/logo.svg";
            var spec = new SiteSpec
            {
                Name = "Missing configured static input",
                BaseUrl = "https://example.test",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [new CollectionSpec { Name = "docs", Input = "content", Output = "/docs" }],
                StaticAssets = [new StaticAssetSpec { Source = "missing-logo.svg", Destination = "logo.svg" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec { Name = "main", Items = [new MenuItemSpec { Title = "Logo", Url = missingAssetRoute }] }]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.False(File.Exists(Path.Combine(outputRoot, "logo.svg")));
            Assert.True(HasMissingRouteWarning(result, missingAssetRoute), string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_ExcludesProjectFilteredContentFromGeneratedRoutes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-project-content-filter-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "projects", "demo");
        var contentRoot = Path.Combine(projectRoot, "content");
        Directory.CreateDirectory(contentRoot);

        try
        {
            File.WriteAllText(
                Path.Combine(projectRoot, "project.json"),
                """
                {
                  "name": "Demo",
                  "slug": "demo",
                  "content": {
                    "exclude": [ "demo/content/excluded.md" ]
                  }
                }
                """);
            File.WriteAllText(Path.Combine(contentRoot, "included.md"), "---\ntitle: Included\n---\nIncluded");
            File.WriteAllText(Path.Combine(contentRoot, "excluded.md"), "---\ntitle: Excluded\n---\nExcluded");
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            const string excludedJsonRoute = "/projects/demo/excluded/index.json";
            var spec = new SiteSpec
            {
                Name = "Project content filter parity",
                BaseUrl = "https://example.test",
                ProjectsRoot = "projects",
                TrailingSlash = TrailingSlashMode.Always,
                Collections =
                [
                    new CollectionSpec
                    {
                        Name = "projects",
                        Input = "projects/*/content",
                        Output = "/projects/{project}/",
                        Outputs = ["html", "json"]
                    }
                ],
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec { Name = "main", Items = [new MenuItemSpec { Title = "Excluded JSON", Url = excludedJsonRoute }] }]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(File.Exists(Path.Combine(outputRoot, "projects", "demo", "included", "index.json")));
            Assert.False(File.Exists(Path.Combine(outputRoot, "projects", "demo", "excluded", "index.json")));
            Assert.True(HasMissingRouteWarning(result, excludedJsonRoute), string.Join(Environment.NewLine, result.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_UsesBuildSerializerForProjectSpecs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-project-serializer-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "projects", "demo");
        Directory.CreateDirectory(projectRoot);

        try
        {
            File.WriteAllText(
                Path.Combine(projectRoot, "project.json"),
                """
                {
                  "name": "Demo",
                  "slug": "demo",
                  "redirects": [
                    { "from": "/legacy", "to": "/", "matchType": "prefix" }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(root, "static-marker.txt"), "marker");
            const string redirectedRoute = "/legacy/guide";
            var spec = new SiteSpec
            {
                Name = "Project serializer parity",
                BaseUrl = "https://example.test",
                ProjectsRoot = "projects",
                StaticAssets = [new StaticAssetSpec { Source = "static-marker.txt", Destination = "./" }],
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = [new MenuSpec { Name = "main", Items = [new MenuItemSpec { Title = "Legacy", Url = redirectedRoute }] }]
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);

            var strictPropertyOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
            WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site-strict"), strictPropertyOptions);
            var strictResult = WebSiteVerifier.Verify(spec, plan, strictPropertyOptions);

            Assert.True(HasMissingRouteWarning(strictResult, redirectedRoute), string.Join(Environment.NewLine, strictResult.Warnings));

            WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site-cli"), WebCliJson.Options);
            var cliResult = WebSiteVerifier.Verify(spec, plan, WebCliJson.Options);

            Assert.False(HasMissingRouteWarning(cliResult, redirectedRoute), string.Join(Environment.NewLine, cliResult.Warnings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
