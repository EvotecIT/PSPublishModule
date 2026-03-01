using PowerForge.Web;

public class WebSiteVerifierReleaseHubTests
{
    [Fact]
    public void Verify_WarnsWhenReleaseSelectorsExistButReleaseHubDataIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-releasehub-missing-" + Guid.NewGuid().ToString("N"));
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

                {{< release-button product="demo.app" label="Download Demo" >}}
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier ReleaseHub Missing Test",
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
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.RELEASE.NO_MATCH]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("data/release-hub.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenReleaseHubCatalogIsMissingProductsAndAssetsCollide()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-releasehub-catalog-" + Guid.NewGuid().ToString("N"));
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

                {{< release-button product="demo.app" label="Download Demo" >}}
                """);

            var dataPath = Path.Combine(root, "data");
            Directory.CreateDirectory(dataPath);
            File.WriteAllText(Path.Combine(dataPath, "release-hub.json"),
                """
                {
                  "title": "Demo Releases",
                  "products": [],
                  "releases": [
                    {
                      "tag": "v1.0.0",
                      "assets": [
                        {
                          "name": "DemoApp-win-x64.zip",
                          "downloadUrl": "https://example.test/downloads/DemoApp-win-x64.zip",
                          "product": "demo.app"
                        },
                        {
                          "name": "DemoApp-win-x64.zip",
                          "downloadUrl": "https://example.test/downloads/DemoApp-win-x64.zip",
                          "product": "demo.app"
                        }
                      ]
                    }
                  ]
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier ReleaseHub Catalog Test",
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
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.RELEASE.PRODUCT_MISSING]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("demo.app", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.RELEASE.ASSET_COLLISION]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("DemoApp-win-x64.zip", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotWarnForWildcardReleaseProductSelectors()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-releasehub-wildcard-" + Guid.NewGuid().ToString("N"));
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

                {{< release-buttons product="*" channel="stable" groupBy="product" >}}
                """);

            var dataPath = Path.Combine(root, "data");
            Directory.CreateDirectory(dataPath);
            File.WriteAllText(Path.Combine(dataPath, "release-hub.json"),
                """
                {
                  "title": "Demo Releases",
                  "products": [
                    { "id": "demo.app", "name": "Demo App" },
                    { "id": "demo.docs", "name": "Demo Docs" }
                  ],
                  "releases": [
                    {
                      "tag": "v1.0.0",
                      "assets": [
                        {
                          "name": "DemoApp-win-x64.zip",
                          "downloadUrl": "https://example.test/downloads/DemoApp-win-x64.zip",
                          "product": "demo.app"
                        },
                        {
                          "name": "DemoDocs-win-x64.zip",
                          "downloadUrl": "https://example.test/downloads/DemoDocs-win-x64.zip",
                          "product": "demo.docs"
                        }
                      ]
                    }
                  ]
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier ReleaseHub Wildcard Test",
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
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("[PFWEB.RELEASE.NO_MATCH]", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhenReleasePlacementReferenceIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-release-placement-missing-" + Guid.NewGuid().ToString("N"));
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

                {{< release-changelog placement="changelog.timeline" >}}
                """);

            var dataPath = Path.Combine(root, "data");
            Directory.CreateDirectory(dataPath);
            File.WriteAllText(Path.Combine(dataPath, "release_placements.json"),
                """
                {
                  "home": {
                    "primary": {
                      "product": "demo.app"
                    }
                  }
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Release Placement Test",
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
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.RELEASE.PLACEMENT_MISSING]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("changelog.timeline", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
