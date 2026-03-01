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
