using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebXrefMapMergerTests
{
    [Fact]
    public void Merge_CombinesApiAndSiteXrefMaps()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var apiMap = Path.Combine(root, "api-xref.json");
            File.WriteAllText(apiMap,
                """
                {
                  "references": [
                    {
                      "uid": "System.String",
                      "name": "String",
                      "href": "/api/types/system-string.json",
                      "aliases": [ "T:System.String" ]
                    }
                  ]
                }
                """);

            var siteMap = Path.Combine(root, "site-xref.json");
            File.WriteAllText(siteMap,
                """
                {
                  "entries": [
                    {
                      "id": "docs.install",
                      "url": "/docs/install/",
                      "title": "Install"
                    }
                  ]
                }
                """);

            var outputPath = Path.Combine(root, "merged", "xrefmap.json");
            var options = new WebXrefMergeOptions
            {
                OutputPath = outputPath
            };
            options.Inputs.Add(apiMap);
            options.Inputs.Add(siteMap);

            var result = WebXrefMapMerger.Merge(options);
            Assert.Equal(2, result.SourceCount);
            Assert.Equal(2, result.ReferenceCount);
            Assert.True(File.Exists(outputPath));

            using var doc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var references = doc.RootElement.GetProperty("references").EnumerateArray().ToArray();
            var systemString = references.Single(r => string.Equals(r.GetProperty("uid").GetString(), "System.String", StringComparison.Ordinal));
            var install = references.Single(r => string.Equals(r.GetProperty("uid").GetString(), "docs.install", StringComparison.Ordinal));

            Assert.Equal("/api/types/system-string.json", systemString.GetProperty("href").GetString());
            Assert.Contains(systemString.GetProperty("aliases").EnumerateArray().Select(static a => a.GetString()), value => string.Equals(value, "T:System.String", StringComparison.Ordinal));
            Assert.Equal("/docs/install/", install.GetProperty("href").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Merge_PreferLast_ReplacesDuplicateHref()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-merge-prefer-last-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var firstMap = Path.Combine(root, "first.json");
            File.WriteAllText(firstMap,
                """
                {
                  "references": [
                    { "uid": "sample.uid", "href": "/a/", "aliases": [ "sample:first" ] }
                  ]
                }
                """);

            var secondMap = Path.Combine(root, "second.json");
            File.WriteAllText(secondMap,
                """
                {
                  "references": [
                    { "uid": "sample.uid", "href": "/b/", "aliases": [ "sample:second" ] }
                  ]
                }
                """);

            var outputPath = Path.Combine(root, "merged.json");
            var options = new WebXrefMergeOptions
            {
                OutputPath = outputPath,
                PreferLast = true
            };
            options.Inputs.Add(firstMap);
            options.Inputs.Add(secondMap);

            var result = WebXrefMapMerger.Merge(options);
            Assert.Equal(1, result.DuplicateCount);

            using var doc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var reference = doc.RootElement.GetProperty("references").EnumerateArray().Single();
            Assert.Equal("/b/", reference.GetProperty("href").GetString());
            var aliases = reference.GetProperty("aliases").EnumerateArray().Select(static a => a.GetString()).Where(static v => !string.IsNullOrWhiteSpace(v)).ToArray();
            Assert.Contains("sample:first", aliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("sample:second", aliases, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Merge_FailOnDuplicateIds_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-merge-fail-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var firstMap = Path.Combine(root, "first.json");
            var secondMap = Path.Combine(root, "second.json");
            File.WriteAllText(firstMap, """{"references":[{"uid":"sample.uid","href":"/a/"}]}""");
            File.WriteAllText(secondMap, """{"references":[{"uid":"sample.uid","href":"/b/"}]}""");

            var options = new WebXrefMergeOptions
            {
                OutputPath = Path.Combine(root, "merged.json"),
                FailOnDuplicateIds = true
            };
            options.Inputs.Add(firstMap);
            options.Inputs.Add(secondMap);

            var ex = Assert.Throws<InvalidOperationException>(() => WebXrefMapMerger.Merge(options));
            Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Merge_MaxReferences_EmitsWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-merge-max-refs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var map = Path.Combine(root, "map.json");
            File.WriteAllText(map,
                """
                {
                  "references": [
                    { "uid": "docs.install", "href": "/docs/install/" },
                    { "uid": "docs.quickstart", "href": "/docs/quickstart/" }
                  ]
                }
                """);

            var options = new WebXrefMergeOptions
            {
                OutputPath = Path.Combine(root, "merged.json"),
                MaxReferences = 1
            };
            options.Inputs.Add(map);

            var result = WebXrefMapMerger.Merge(options);
            Assert.Equal(2, result.ReferenceCount);
            Assert.Contains(result.Warnings, warning => warning.Contains("maxReferences", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Merge_MaxDuplicates_EmitsWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-merge-max-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var first = Path.Combine(root, "first.json");
            var second = Path.Combine(root, "second.json");
            var third = Path.Combine(root, "third.json");
            File.WriteAllText(first, """{"references":[{"uid":"sample.uid","href":"/a/"}]}""");
            File.WriteAllText(second, """{"references":[{"uid":"sample.uid","href":"/b/"}]}""");
            File.WriteAllText(third, """{"references":[{"uid":"sample.uid","href":"/c/"}]}""");

            var options = new WebXrefMergeOptions
            {
                OutputPath = Path.Combine(root, "merged.json"),
                MaxDuplicates = 1
            };
            options.Inputs.Add(first);
            options.Inputs.Add(second);
            options.Inputs.Add(third);

            var result = WebXrefMapMerger.Merge(options);
            Assert.Equal(2, result.DuplicateCount);
            Assert.Contains(result.Warnings, warning => warning.Contains("maxDuplicates", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Merge_MaxReferenceGrowthCount_EmitsWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-merge-max-growth-count-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var output = Path.Combine(root, "merged.json");
            File.WriteAllText(output,
                """
                {
                  "references": [
                    { "uid": "docs.old", "href": "/docs/old/" }
                  ]
                }
                """);

            var map = Path.Combine(root, "current.json");
            File.WriteAllText(map,
                """
                {
                  "references": [
                    { "uid": "docs.one", "href": "/docs/one/" },
                    { "uid": "docs.two", "href": "/docs/two/" },
                    { "uid": "docs.three", "href": "/docs/three/" }
                  ]
                }
                """);

            var options = new WebXrefMergeOptions
            {
                OutputPath = output,
                MaxReferenceGrowthCount = 1
            };
            options.Inputs.Add(map);

            var result = WebXrefMapMerger.Merge(options);
            Assert.Equal(1, result.PreviousReferenceCount);
            Assert.Equal(2, result.ReferenceDeltaCount);
            Assert.Contains(result.Warnings, warning => warning.Contains("maxReferenceGrowthCount", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Merge_MaxReferenceGrowthPercent_EmitsWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-xref-merge-max-growth-percent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var output = Path.Combine(root, "merged.json");
            File.WriteAllText(output,
                """
                {
                  "references": [
                    { "uid": "docs.one", "href": "/docs/one/" },
                    { "uid": "docs.two", "href": "/docs/two/" }
                  ]
                }
                """);

            var map = Path.Combine(root, "current.json");
            File.WriteAllText(map,
                """
                {
                  "references": [
                    { "uid": "docs.one", "href": "/docs/one/" },
                    { "uid": "docs.two", "href": "/docs/two/" },
                    { "uid": "docs.three", "href": "/docs/three/" },
                    { "uid": "docs.four", "href": "/docs/four/" }
                  ]
                }
                """);

            var options = new WebXrefMergeOptions
            {
                OutputPath = output,
                MaxReferenceGrowthPercent = 50
            };
            options.Inputs.Add(map);

            var result = WebXrefMapMerger.Merge(options);
            Assert.Equal(2, result.PreviousReferenceCount);
            Assert.Equal(2, result.ReferenceDeltaCount);
            Assert.NotNull(result.ReferenceDeltaPercent);
            Assert.True(result.ReferenceDeltaPercent > 99.9);
            Assert.Contains(result.Warnings, warning => warning.Contains("maxReferenceGrowthPercent", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
