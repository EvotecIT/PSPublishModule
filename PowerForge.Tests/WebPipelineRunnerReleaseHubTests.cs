using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerReleaseHubTests
{
    [Fact]
    public void RunPipeline_ReleaseHub_GeneratesOutputFromLocalReleasesJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-release-hub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var releasesPath = Path.Combine(root, "releases.json");
            File.WriteAllText(releasesPath,
                """
                [
                  {
                    "tag_name": "v2.0.0",
                    "name": "IntelligenceX 2.0.0",
                    "published_at": "2026-02-22T12:00:00Z",
                    "prerelease": false,
                    "draft": false,
                    "assets": [
                      {
                        "name": "IntelligenceX.Chat-v2.0.0-win-x64.zip",
                        "browser_download_url": "https://example.test/IntelligenceX.Chat-v2.0.0-win-x64.zip",
                        "size": 12345,
                        "content_type": "application/zip"
                      }
                    ]
                  }
                ]
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "release-hub",
                      "source": "file",
                      "releasesPath": "./releases.json",
                      "assetRules": [
                        { "product": "intelligencex.chat", "match": [ "IntelligenceX.Chat*.zip" ], "kind": "zip" }
                      ],
                      "out": "./data/release-hub.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("Release hub 1 releases, 1 assets", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var outputPath = Path.Combine(root, "data", "release-hub.json");
            Assert.True(File.Exists(outputPath));

            using var doc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var json = doc.RootElement;
            var releases = json.GetProperty("releases");
            Assert.Equal(1, releases.GetArrayLength());
            var asset = releases[0].GetProperty("assets")[0];
            Assert.Equal("intelligencex.chat", asset.GetProperty("product").GetString());
            Assert.Equal("zip", asset.GetProperty("kind").GetString());
            Assert.Equal("windows", asset.GetProperty("platform").GetString());
            Assert.Equal("x64", asset.GetProperty("arch").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ReleaseHub_PreservesExistingOutputWhenWarningsProduceEmptyRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-release-hub-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outputPath = Path.Combine(root, "data", "release-hub.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath,
                """
                {
                  "title": "Release Hub",
                  "generatedAtUtc": "2026-04-01T00:00:00Z",
                  "source": "github",
                  "repo": "EvotecIT/TestRepo",
                  "latest": { "stableTag": "v1.0.0" },
                  "products": [],
                  "releases": [
                    {
                      "id": "v1-0-0",
                      "tag": "v1.0.0",
                      "title": "v1.0.0",
                      "assets": []
                    }
                  ],
                  "warnings": []
                }
                """);

            var preserveMethod = typeof(WebPipelineRunner)
                .GetMethod("TryPreserveExistingReleaseHub", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(preserveMethod);

            var generatedPath = Path.Combine(root, "generated.json");
            File.WriteAllText(generatedPath,
                """
                {
                  "title": "Release Hub",
                  "generatedAtUtc": "2026-04-02T00:00:00Z",
                  "source": "github",
                  "repo": "EvotecIT/TestRepo",
                  "latest": {},
                  "products": [],
                  "releases": [],
                  "warnings": [ "GitHub release fetch failed (401) for EvotecIT/TestRepo." ]
                }
                """);

            var preserved = Assert.IsType<bool>(preserveMethod!.Invoke(null, new object?[] { File.ReadAllText(outputPath), generatedPath }));
            Assert.True(preserved);

            using var doc = JsonDocument.Parse(File.ReadAllText(generatedPath));
            Assert.Equal(1, doc.RootElement.GetProperty("releases").GetArrayLength());
            Assert.Equal("v1.0.0", doc.RootElement.GetProperty("latest").GetProperty("stableTag").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
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
