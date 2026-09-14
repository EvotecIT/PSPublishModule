using PowerForge.Web;

public partial class WebSiteVerifierTests
{
    [Theory]
    [InlineData("", false, true)]
    [InlineData(", \"homeLinkInBrand\": false", false, true)]
    [InlineData(", \"homeLinkInBrand\": true", false, false)]
    [InlineData("", true, false)]
    public void Verify_HomeLinkCanBeProvidedByBrand(string setting, bool menuHasHome, bool warns)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-brand-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));
        try
        {
            File.WriteAllText(Path.Combine(root, "content", "index.md"), "---\ntitle: Home\nslug: index\n---\nHome");
            var configPath = Path.Combine(root, "site.json");
            var home = menuHasHome ? ", {\"title\":\"Home\",\"url\":\"/\"}" : "";
            File.WriteAllText(configPath, $$"""
                {
                  "name": "Brand home test",
                  "baseUrl": "https://example.test",
                  "contentRoot": "content",
                  "collections": [{"name":"pages","input":"content","output":"/"}],
                  "navigation": {
                    "autoDefaults": false{{setting}},
                    "menus": [{"name":"main","items":[{"title":"Missing","url":"/missing/"}{{home}}]}]
                  }
                }
                """);
            var (spec, _) = WebSiteSpecLoader.LoadWithPath(configPath);
            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));

            Assert.Equal(warns, result.Warnings.Any(warning => warning.Contains("main menu does not contain '/'", StringComparison.Ordinal)));
            // Declaring a brand home link must not disable ordinary destination validation.
            Assert.Contains(result.Warnings, warning => warning.Contains("/missing/", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
