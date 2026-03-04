using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerSearchIndexExportTests
{
    [Fact]
    public void RunPipeline_SearchIndexExport_TruncatesAndWritesSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-search-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourceDir = Path.Combine(root, "_site", "search");
            Directory.CreateDirectory(sourceDir);
            var sourcePath = Path.Combine(sourceDir, "index.json");
            File.WriteAllText(sourcePath,
                """
                [
                  { "title": "One", "url": "/one/" },
                  { "title": "Two", "url": "/two/" },
                  { "title": "Three", "url": "/three/" }
                ]
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "search-index-export",
                      "source": "./_site/search/index.json",
                      "out": "./_site/search-index.json",
                      "maxItems": 2,
                      "summaryPath": "./Build/search-export-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("search-index-export ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var outputPath = Path.Combine(root, "_site", "search-index.json");
            Assert.True(File.Exists(outputPath));
            using var outputDoc = JsonDocument.Parse(File.ReadAllText(outputPath));
            Assert.Equal(JsonValueKind.Array, outputDoc.RootElement.ValueKind);
            Assert.Equal(2, outputDoc.RootElement.GetArrayLength());

            var summaryPath = Path.Combine(root, "Build", "search-export-summary.json");
            Assert.True(File.Exists(summaryPath));
            using var summaryDoc = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.Equal("updated", summaryDoc.RootElement.GetProperty("status").GetString());
            Assert.True(summaryDoc.RootElement.GetProperty("truncated").GetBoolean());
            Assert.Equal(3, summaryDoc.RootElement.GetProperty("sourceItems").GetInt32());
            Assert.Equal(2, summaryDoc.RootElement.GetProperty("items").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_SearchIndexExport_NonStrictSkipsWhenSourceMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-search-export-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "search-index-export",
                      "source": "./_site/search/index.json",
                      "out": "./_site/search-index.json",
                      "strict": false,
                      "summaryPath": "./Build/search-export-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("skipped", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var outputPath = Path.Combine(root, "_site", "search-index.json");
            Assert.False(File.Exists(outputPath));

            var summaryPath = Path.Combine(root, "Build", "search-export-summary.json");
            Assert.True(File.Exists(summaryPath));
            using var summaryDoc = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.Equal("skipped", summaryDoc.RootElement.GetProperty("status").GetString());
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
