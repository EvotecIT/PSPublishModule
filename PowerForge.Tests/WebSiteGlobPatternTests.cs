using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSiteGlobPatternTests
{
    [Fact]
    public void ShortcodeDataResolutionPreservesObjectPayloads()
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "Release",
            ["items"] = new object?[]
            {
                new Dictionary<string, object?> { ["name"] = "PowerForge" }
            }
        };
        var data = new Dictionary<string, object?>
        {
            ["release"] = payload
        };
        var attrs = new Dictionary<string, string>
        {
            ["data"] = "release"
        };

        Assert.Same(payload, ShortcodeProcessor.ResolveData(data, attrs));
        Assert.Single(ShortcodeProcessor.ResolveList(data, attrs)!);
    }

    [Fact]
    public void RecursiveIncludeMatchesRootAndNestedMarkdownFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Web.GlobTests", Guid.NewGuid().ToString("N"));
        var contentRoot = Path.Combine(root, "content", "pages");
        var nestedRoot = Path.Combine(contentRoot, "nested");
        Directory.CreateDirectory(nestedRoot);
        var rootFile = Path.Combine(contentRoot, "index.md");
        var nestedFile = Path.Combine(nestedRoot, "details.md");
        File.WriteAllText(rootFile, "# Root");
        File.WriteAllText(nestedFile, "# Nested");
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");

        try
        {
            var collection = new CollectionSpec
            {
                Name = "pages",
                Input = "content/pages",
                Output = "/",
                Include = ["**/*.md"]
            };
            var spec = new SiteSpec
            {
                Name = "Glob contract",
                Collections = [collection]
            };

            var plan = WebSitePlanner.Plan(spec, configPath);
            Assert.Equal(2, Assert.Single(plan.Collections).FileCount);
            Assert.Equal(
                new[] { rootFile, nestedFile }.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                WebSiteBuilder.EnumerateCollectionFilesForDiscovery(plan, collection));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
