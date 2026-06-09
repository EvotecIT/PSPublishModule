using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerVersionHubTests
{
    [Fact]
    public void RunPipeline_VersionHub_DiscoversFoldersAndMarksLatest()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-version-hub-discover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "versions", "v1.0"));
            Directory.CreateDirectory(Path.Combine(root, "versions", "v2.1"));

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "version-hub",
                      "discoverRoot": "./versions",
                      "discoverPattern": "v*",
                      "basePath": "/docs/",
                      "out": "./data/version-hub.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("Version hub 2 versions", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var outputPath = Path.Combine(root, "data", "version-hub.json");
            Assert.True(File.Exists(outputPath));

            using var doc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var json = doc.RootElement;
            Assert.Equal("/docs/v2.1/", json.GetProperty("latestPath").GetString());
            var versions = json.GetProperty("versions");
            Assert.Equal(2, versions.GetArrayLength());
            Assert.Equal("2.1", versions[0].GetProperty("version").GetString());
            Assert.True(versions[0].GetProperty("latest").GetBoolean());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_VersionHub_SupportsExplicitEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-version-hub-explicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "version-hub",
                      "title": "Contoso Versions",
                      "entries": [
                        {
                          "version": "2.0",
                          "label": "v2 (latest)",
                          "path": "/docs/v2/",
                          "latest": true,
                          "aliases": ["latest", "stable"]
                        },
                        {
                          "version": "1.5",
                          "label": "v1.5 (LTS)",
                          "path": "/docs/v1.5/",
                          "lts": true
                        }
                      ],
                      "out": "./data/version-hub.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var outputPath = Path.Combine(root, "data", "version-hub.json");
            Assert.True(File.Exists(outputPath));
            using var doc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var json = doc.RootElement;
            Assert.Equal("Contoso Versions", json.GetProperty("title").GetString());
            Assert.Equal("/docs/v2/", json.GetProperty("latestPath").GetString());
            Assert.Equal("/docs/v1.5/", json.GetProperty("ltsPath").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_VersionHub_FailsWithoutEntriesOrDiscoverRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-version-hub-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "version-hub",
                      "out": "./data/version-hub.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("at least one version entry", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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