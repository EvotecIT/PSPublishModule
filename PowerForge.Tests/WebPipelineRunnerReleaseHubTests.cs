using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerReleaseHubTests
{
    [Fact]
    public void RunPipeline_ReleaseHub_KeepsDistinctTaglessTimelineEntriesWithRetention()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-tagless-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "releases.json"),
                """
                [
                  { "name": "First", "published_at": "2026-04-04T00:00:00Z", "assets": [] },
                  { "name": "Second", "published_at": "2026-04-03T00:00:00Z", "assets": [] },
                  { "tag_name": "Studio-v0.1.9", "published_at": "2026-04-01T00:00:00Z", "assets": [] }
                ]
                """);
            File.WriteAllText(Path.Combine(root, "pipeline.json"),
                """
                { "steps": [{ "task": "release-hub", "source": "file", "releasesPath": "./releases.json",
                  "maxReleases": 2, "retainLatestStableTagPrefixes": ["Studio-v"],
                  "out": "./data/release-hub.json" }] }
                """);

            Assert.True(WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null).Success);
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "release-hub.json")));
            var releases = doc.RootElement.GetProperty("releases");
            Assert.Equal(3, releases.GetArrayLength());
            Assert.Equal("First", releases[0].GetProperty("title").GetString());
            Assert.Equal("Second", releases[1].GetProperty("title").GetString());
            Assert.Equal("Studio-v0.1.9", releases[2].GetProperty("tag").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }


    [Fact]
    public void PipelineOutputStamp_TracksFilesBeyondInputStampLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-output-stamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var index = 0; index < 1002; index++)
                File.WriteAllText(Path.Combine(root, $"page-{index:D4}.html"), "page");

            var before = WebPipelineRunner.ComputeOutputStamp([root]);
            File.Delete(Path.Combine(root, "page-1001.html"));
            var after = WebPipelineRunner.ComputeOutputStamp([root]);
            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.NotEqual(before, after);

            var unchangedMetadataPath = Path.Combine(root, "page-0000.html");
            var originalTime = File.GetLastWriteTimeUtc(unchangedMetadataPath);
            File.WriteAllText(unchangedMetadataPath, "PAGE");
            File.SetLastWriteTimeUtc(unchangedMetadataPath, originalTime);
            var afterCorruption = WebPipelineRunner.ComputeOutputStamp([root]);
            Assert.NotEqual(after, afterCorruption);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ReleaseHubRefresh_InvalidatesLaterCachedSiteBuildWithoutExplicitDependency()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-live-hub-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content", "pages"));
        Directory.CreateDirectory(Path.Combine(root, "themes", "base", "layouts"));
        try
        {
            File.WriteAllText(Path.Combine(root, "content", "pages", "index.md"), "---\ntitle: Home\n---\nHome.");
            File.WriteAllText(Path.Combine(root, "themes", "base", "layouts", "page.html"),
                "<!doctype html><html><head><title>{{TITLE}}</title></head><body>{{CONTENT}}</body></html>");
            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                { "name": "Live release site", "baseUrl": "https://example.test", "contentRoot": "content",
                  "themesRoot": "themes", "defaultTheme": "base",
                  "collections": [{ "name": "pages", "input": "content/pages", "output": "/", "defaultLayout": "page" }] }
                """);
            var releasesPath = Path.Combine(root, "releases.json");
            File.WriteAllText(releasesPath, """[{"tag_name":"v1","published_at":"2026-04-01T00:00:00Z","assets":[]}]""");
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                { "cache": true, "steps": [
                  { "task": "release-hub", "source": "file", "releasesPath": "./releases.json", "out": "./data/release-hub.json" },
                  { "task": "build", "config": "./site.json", "out": "./site", "clean": true },
                  { "task": "verify", "config": "./site.json" }
                ] }
                """);

            var first = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(first.Success);
            Assert.All(first.Steps, step => Assert.False(step.Cached));
            var unchanged = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(unchanged.Success);
            Assert.All(unchanged.Steps, step => Assert.True(step.Cached, step.Task));

            var hubOutput = Path.Combine(root, "data", "release-hub.json");
            var hubWriteTime = File.GetLastWriteTimeUtc(hubOutput);
            var originalHub = File.ReadAllText(hubOutput);
            File.WriteAllText(hubOutput, originalHub.Replace("v1", "v9", StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(hubOutput, hubWriteTime);
            var afterCorruptedHub = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(afterCorruptedHub.Success);
            Assert.All(afterCorruptedHub.Steps, step => Assert.False(step.Cached));
            Assert.Contains("v1", File.ReadAllText(hubOutput), StringComparison.Ordinal);

            var builtPage = Path.Combine(root, "site", "index.html");
            Assert.True(File.Exists(builtPage));
            File.Delete(builtPage);
            var afterDeletedPage = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(afterDeletedPage.Success);
            Assert.True(afterDeletedPage.Steps[0].Cached);
            Assert.False(afterDeletedPage.Steps[1].Cached);
            Assert.False(afterDeletedPage.Steps[2].Cached);
            Assert.True(File.Exists(builtPage));

            File.WriteAllText(releasesPath, """[{"tag_name":"v2","published_at":"2026-04-02T00:00:00Z","assets":[]}]""");
            var second = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(second.Success);
            Assert.False(second.Steps[0].Cached);
            Assert.False(second.Steps[1].Cached);
            Assert.False(second.Steps[2].Cached);
            Assert.Contains("v2", File.ReadAllText(Path.Combine(root, "data", "release-hub.json")), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ReleaseHub_RetainsOlderStableProductReleaseBeyondTimelineLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-retained-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "releases.json"),
                """
                [
                  { "tag_name": "OfficeIMO-v4", "published_at": "2026-04-04T00:00:00Z", "draft": false, "prerelease": false, "assets": [] },
                  { "tag_name": "OfficeIMO-v3", "published_at": "2026-04-03T00:00:00Z", "draft": false, "prerelease": false, "assets": [] },
                  { "tag_name": "OfficeIMO-v2", "published_at": "2026-04-02T00:00:00Z", "draft": false, "prerelease": false, "assets": [] },
                  { "tag_name": "Studio-v0.1.9", "published_at": "2026-04-01T00:00:00Z", "draft": false, "prerelease": false,
                    "assets": [{ "name": "OfficeIMO-Studio-0.1.9-win-x64.msi", "browser_download_url": "https://example.test/Studio-v0.1.9/OfficeIMO-Studio-0.1.9-win-x64.msi" }] }
                ]
                """);
            File.WriteAllText(Path.Combine(root, "pipeline.json"),
                """
                { "steps": [{ "task": "release-hub", "source": "file", "releasesPath": "./releases.json",
                  "maxReleases": 2, "retainLatestStableTagPrefixes": ["Studio-v"],
                  "out": "./data/release-hub.json" }] }
                """);

            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);
            Assert.True(result.Success);
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "release-hub.json")));
            var releases = doc.RootElement.GetProperty("releases");
            Assert.Equal(3, releases.GetArrayLength());
            Assert.Equal("Studio-v0.1.9", releases[2].GetProperty("tag").GetString());
            Assert.Equal("OfficeIMO-Studio-0.1.9-win-x64.msi",
                releases[2].GetProperty("assets")[0].GetProperty("name").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ReleaseHub_RetainsPreviousStableProductAfterNewerIncompleteRelease()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-all-stable-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "releases.json"),
                """
                [
                  { "tag_name": "OfficeIMO-v4", "published_at": "2026-04-04T00:00:00Z", "assets": [] },
                  { "tag_name": "Studio-v0.1.10", "published_at": "2026-04-03T00:00:00Z", "assets": [] },
                  { "tag_name": "OfficeIMO-v3", "published_at": "2026-04-02T00:00:00Z", "assets": [] },
                  { "tag_name": "Studio-v0.1.9", "published_at": "2026-04-01T00:00:00Z",
                    "assets": [{ "name": "OfficeIMO-Studio-0.1.9-win-x64.msi", "browser_download_url": "https://example.test/Studio-v0.1.9/OfficeIMO-Studio-0.1.9-win-x64.msi" }] }
                ]
                """);
            File.WriteAllText(Path.Combine(root, "pipeline.json"),
                """
                { "steps": [{ "task": "release-hub", "source": "file", "releasesPath": "./releases.json",
                  "maxReleases": 2, "retainAllStableTagPrefixes": ["Studio-v"],
                  "out": "./data/release-hub.json" }] }
                """);

            Assert.True(WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null).Success);
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "release-hub.json")));
            var releases = doc.RootElement.GetProperty("releases");
            Assert.Equal(3, releases.GetArrayLength());
            Assert.Equal("Studio-v0.1.9", releases[2].GetProperty("tag").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

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
    public void RunPipeline_ReleaseHub_FailsClosedAndKeepsPreviousFileWhenSourceIsIncomplete()
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

            var previous = File.ReadAllText(outputPath);
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                { "steps": [{ "task": "release-hub", "source": "file", "releasesPath": "./missing.json",
                    "out": "./data/release-hub.json" }] }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Contains("publication requires a complete source", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(previous, File.ReadAllText(outputPath));
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
