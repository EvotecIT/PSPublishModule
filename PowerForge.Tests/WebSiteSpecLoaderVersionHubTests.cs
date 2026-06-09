using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSiteSpecLoaderVersionHubTests
{
    [Fact]
    public void LoadWithPath_LoadsVersioningFromHubPath_WhenVersionsAreNotDeclared()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-spec-loader-hub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var dataRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(dataRoot);
            File.WriteAllText(Path.Combine(dataRoot, "version-hub.json"),
                """
                {
                  "title": "Versions",
                  "versions": [
                    { "id": "v3", "version": "3.0", "label": "v3", "path": "/docs/v3/", "latest": true },
                    { "id": "v2", "version": "2.0", "label": "v2 (LTS)", "path": "/docs/v2/", "lts": true, "deprecated": true }
                  ]
                }
                """);

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath,
                """
                {
                  "Name": "Hub Loader Test",
                  "BaseUrl": "https://example.test",
                  "Versioning": {
                    "Enabled": true,
                    "BasePath": "/docs",
                    "HubPath": "./data/version-hub.json"
                  }
                }
                """);

            var (spec, _) = WebSiteSpecLoader.LoadWithPath(configPath);
            Assert.NotNull(spec.Versioning);
            Assert.Equal("v3", spec.Versioning!.Current);
            Assert.Equal(2, spec.Versioning.Versions.Length);

            var latest = spec.Versioning.Versions.First(v => v.Name == "v3");
            Assert.True(latest.Latest);
            Assert.False(latest.Lts);

            var lts = spec.Versioning.Versions.First(v => v.Name == "v2");
            Assert.True(lts.Lts);
            Assert.True(lts.Deprecated);
            Assert.Equal("/docs/v2/", lts.Url);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadWithPath_DoesNotRequireHubFile_WhenInlineVersionsAreDeclared()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-spec-loader-inline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath,
                """
                {
                  "Name": "Inline Versioning Test",
                  "BaseUrl": "https://example.test",
                  "Versioning": {
                    "Enabled": true,
                    "HubPath": "./data/missing-version-hub.json",
                    "Current": "manual",
                    "Versions": [
                      { "Name": "manual", "Label": "Manual", "Url": "/docs/manual/", "Latest": true }
                    ]
                  }
                }
                """);

            var (spec, _) = WebSiteSpecLoader.LoadWithPath(configPath);
            Assert.NotNull(spec.Versioning);
            Assert.Single(spec.Versioning!.Versions);
            Assert.Equal("manual", spec.Versioning.Versions[0].Name);
            Assert.Equal("manual", spec.Versioning.Current);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LoadWithPath_Throws_WhenHubPathIsMissingAndNoInlineVersions()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-spec-loader-missing-hub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath,
                """
                {
                  "Name": "Missing Hub Test",
                  "BaseUrl": "https://example.test",
                  "Versioning": {
                    "Enabled": true,
                    "HubPath": "./data/missing-version-hub.json"
                  }
                }
                """);

            var ex = Assert.Throws<FileNotFoundException>(() => WebSiteSpecLoader.LoadWithPath(configPath));
            Assert.Contains("Versioning hub file not found", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
