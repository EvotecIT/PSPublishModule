using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerEcosystemStatsTests
{
    [Fact]
    public void RunPipeline_EcosystemStats_PreservesExistingWhenWarningsProduceEmptyTotals()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-ecosystem-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var statsPath = Path.Combine(root, "data", "ecosystem", "stats.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statsPath)!);
            WriteStats(statsPath, repositoryCount: 7, nugetPackageCount: 4, psGalleryModuleCount: 3, totalDownloads: 12345);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "ecosystem-stats",
                      "out": "./data/ecosystem/stats.json",
                      "summaryPath": "./summary.json",
                      "githubOrg": "pf-test-org-{{Guid.NewGuid():N}}",
                      "timeoutSeconds": 1
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("fallback", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(File.ReadAllText(statsPath));
            var summary = document.RootElement.GetProperty("summary");
            Assert.Equal(7, summary.GetProperty("repositoryCount").GetInt32());
            Assert.Equal(4, summary.GetProperty("nuGetPackageCount").GetInt32());
            Assert.Equal(3, summary.GetProperty("powerShellGalleryModuleCount").GetInt32());
            Assert.Equal(12345L, summary.GetProperty("totalDownloads").GetInt64());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_EcosystemStats_AllowsReplacingWhenPreserveOnWarningsDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-ecosystem-no-preserve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var statsPath = Path.Combine(root, "data", "ecosystem", "stats.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statsPath)!);
            WriteStats(statsPath, repositoryCount: 9, nugetPackageCount: 5, psGalleryModuleCount: 4, totalDownloads: 54321);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "ecosystem-stats",
                      "out": "./data/ecosystem/stats.json",
                      "summaryPath": "./summary.json",
                      "githubOrg": "pf-test-org-{{Guid.NewGuid():N}}",
                      "timeoutSeconds": 1,
                      "preserveOnWarnings": false
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.DoesNotContain("fallback", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(File.ReadAllText(statsPath));
            var summary = document.RootElement.GetProperty("summary");
            Assert.Equal(0, summary.GetProperty("repositoryCount").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void WriteStats(string path, int repositoryCount, int nugetPackageCount, int psGalleryModuleCount, long totalDownloads)
    {
        var content =
            $$"""
            {
              "title": "Ecosystem Stats",
              "generatedAtUtc": "2026-03-03T00:00:00.0000000Z",
              "summary": {
                "repositoryCount": {{repositoryCount}},
                "nuGetPackageCount": {{nugetPackageCount}},
                "powerShellGalleryModuleCount": {{psGalleryModuleCount}},
                "gitHubStars": 0,
                "gitHubForks": 0,
                "nuGetDownloads": 0,
                "powerShellGalleryDownloads": 0,
                "totalDownloads": {{totalDownloads}}
              },
              "warnings": []
            }
            """;
        File.WriteAllText(path, content);
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
