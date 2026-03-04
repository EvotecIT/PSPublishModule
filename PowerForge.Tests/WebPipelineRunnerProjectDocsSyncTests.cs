using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerProjectDocsSyncTests
{
    [Fact]
    public void RunPipeline_ProjectDocsSync_CopiesDocsAndWritesToc()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    { "slug": "alpha", "surfaces": { "docs": true } },
                    { "slug": "beta", "surfaces": { "docs": false } },
                    { "slug": "gamma", "surfaces": { "docs": true } }
                  ]
                }
                """);

            var sourceDocs = Path.Combine(root, "projects-sources", "alpha", "Docs");
            Directory.CreateDirectory(Path.Combine(sourceDocs, "guide"));
            File.WriteAllText(Path.Combine(sourceDocs, "index.md"), "# Alpha");
            File.WriteAllText(Path.Combine(sourceDocs, "readme-file.md"), "# Readme");
            File.WriteAllText(Path.Combine(sourceDocs, "guide", "setup.md"), "# Setup");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-docs-sync",
                      "catalog": "./data/projects/catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "contentRoot": "./content/docs",
                      "summaryPath": "./Build/sync-project-docs-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("project-docs-sync ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var copiedRootDoc = Path.Combine(root, "content", "docs", "alpha", "index.md");
            var copiedSubDoc = Path.Combine(root, "content", "docs", "alpha", "guide", "setup.md");
            var tocPath = Path.Combine(root, "content", "docs", "alpha", "toc.yml");
            Assert.True(File.Exists(copiedRootDoc));
            Assert.True(File.Exists(copiedSubDoc));
            Assert.True(File.Exists(tocPath));

            var toc = File.ReadAllText(tocPath);
            Assert.Contains("href: index.md", toc, StringComparison.Ordinal);
            Assert.Contains("href: readme-file.md", toc, StringComparison.Ordinal);
            Assert.DoesNotContain("guide/setup.md", toc, StringComparison.Ordinal);

            var summaryPath = Path.Combine(root, "Build", "sync-project-docs-summary.json");
            Assert.True(File.Exists(summaryPath));
            using var summaryDoc = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.Equal("updated", summaryDoc.RootElement.GetProperty("status").GetString());
            Assert.Equal(3, summaryDoc.RootElement.GetProperty("totalProjects").GetInt32());
            Assert.Equal(2, summaryDoc.RootElement.GetProperty("docsProjects").GetInt32());
            Assert.Equal(1, summaryDoc.RootElement.GetProperty("synced").GetInt32());
            Assert.Equal(1, summaryDoc.RootElement.GetProperty("skipped").GetInt32());
            Assert.Equal(3, summaryDoc.RootElement.GetProperty("copiedFiles").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectDocsSync_FailsWhenMissingDocsSourceAndFailOnMissingSourceEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-sync-fail-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    { "slug": "alpha", "surfaces": { "docs": true } }
                  ]
                }
                """);
            Directory.CreateDirectory(Path.Combine(root, "projects-sources"));

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-docs-sync",
                      "catalog": "./data/projects/catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "contentRoot": "./content/docs",
                      "failOnMissingSource": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("source docs path not found", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectDocsSync_SyncsApiArtifactsUsingSourceCandidates()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-sync-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    { "slug": "alpha", "surfaces": { "docs": true, "apiPowerShell": true } },
                    { "slug": "beta", "surfaces": { "docs": false, "apiDotNet": true } }
                  ]
                }
                """);

            var alphaDocs = Path.Combine(root, "projects-sources", "alpha", "Website", "content", "docs");
            Directory.CreateDirectory(alphaDocs);
            File.WriteAllText(Path.Combine(alphaDocs, "index.md"), "# Alpha docs");

            var alphaApi = Path.Combine(root, "projects-sources", "alpha", "Website", "data", "apidocs");
            Directory.CreateDirectory(Path.Combine(alphaApi, "assets"));
            File.WriteAllText(Path.Combine(alphaApi, "index.json"), """{ "title": "alpha" }""");
            File.WriteAllText(Path.Combine(alphaApi, "assets", "search.json"), """{ "items": [] }""");

            var betaApi = Path.Combine(root, "projects-sources", "beta", "data", "apidocs");
            Directory.CreateDirectory(betaApi);
            File.WriteAllText(Path.Combine(betaApi, "openapi.json"), """{ "openapi": "3.0.0" }""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-docs-sync",
                      "catalog": "./data/projects/catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "contentRoot": "./content/docs",
                      "sourceDocsPaths": ["Website/content/docs", "Docs"],
                      "syncApi": true,
                      "apiRoot": "./data/apidocs",
                      "sourceApiPaths": ["Website/data/apidocs", "data/apidocs"],
                      "summaryPath": "./Build/sync-project-docs-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("project-docs-sync ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("api=2/2", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(Path.Combine(root, "content", "docs", "alpha", "index.md")));
            Assert.True(File.Exists(Path.Combine(root, "data", "apidocs", "alpha", "index.json")));
            Assert.True(File.Exists(Path.Combine(root, "data", "apidocs", "alpha", "assets", "search.json")));
            Assert.True(File.Exists(Path.Combine(root, "data", "apidocs", "beta", "openapi.json")));

            var summaryPath = Path.Combine(root, "Build", "sync-project-docs-summary.json");
            Assert.True(File.Exists(summaryPath));
            using var summaryDoc = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.Equal(2, summaryDoc.RootElement.GetProperty("apiProjects").GetInt32());
            Assert.Equal(2, summaryDoc.RootElement.GetProperty("syncedApi").GetInt32());
            Assert.Equal(0, summaryDoc.RootElement.GetProperty("skippedApi").GetInt32());
            Assert.Equal(3, summaryDoc.RootElement.GetProperty("copiedApiFiles").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectDocsSync_FailsWhenMissingApiSourceAndFailOnMissingApiSourceEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-sync-fail-missing-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    { "slug": "alpha", "surfaces": { "docs": true, "apiPowerShell": true } }
                  ]
                }
                """);

            var sourceDocs = Path.Combine(root, "projects-sources", "alpha", "Docs");
            Directory.CreateDirectory(sourceDocs);
            File.WriteAllText(Path.Combine(sourceDocs, "index.md"), "# Alpha");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-docs-sync",
                      "catalog": "./data/projects/catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "contentRoot": "./content/docs",
                      "syncApi": true,
                      "apiRoot": "./data/apidocs",
                      "sourceApiPath": "Website/data/apidocs",
                      "failOnMissingApiSource": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("source api path not found", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
