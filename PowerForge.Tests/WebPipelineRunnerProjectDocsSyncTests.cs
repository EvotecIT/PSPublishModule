using System;
using System.IO;
using System.IO.Compression;
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

            var copiedRootDocContent = File.ReadAllText(copiedRootDoc);
            Assert.Contains("layout: docs", copiedRootDocContent, StringComparison.Ordinal);
            Assert.Contains("meta.generated_by: \"powerforge.project-docs-sync\"", copiedRootDocContent, StringComparison.Ordinal);
            Assert.Contains("meta.project_base_slug: \"alpha\"", copiedRootDocContent, StringComparison.Ordinal);
            Assert.Contains("meta.project_name: \"Alpha\"", copiedRootDocContent, StringComparison.Ordinal);
            Assert.Contains("meta.project_section: \"docs\"", copiedRootDocContent, StringComparison.Ordinal);
            Assert.Contains("meta.project_hub_path: \"/projects/alpha/\"", copiedRootDocContent, StringComparison.Ordinal);
            Assert.Contains("meta.project_link_docs: \"/projects/alpha/docs/\"", copiedRootDocContent, StringComparison.Ordinal);

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
    public void RunPipeline_ProjectDocsSync_FiltersProjectsWhenConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-sync-filter-" + Guid.NewGuid().ToString("N"));
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
                    { "slug": "beta", "surfaces": { "docs": true } }
                  ]
                }
                """);

            var alphaDocs = Path.Combine(root, "projects-sources", "alpha", "Docs");
            Directory.CreateDirectory(alphaDocs);
            File.WriteAllText(Path.Combine(alphaDocs, "index.md"), "# Alpha");

            var betaDocs = Path.Combine(root, "projects-sources", "beta", "Docs");
            Directory.CreateDirectory(betaDocs);
            File.WriteAllText(Path.Combine(betaDocs, "index.md"), "# Beta");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-docs-sync",
                      "catalog": "./data/projects/catalog.json",
                      "projects": ["beta"],
                      "sourcesRoot": "./projects-sources",
                      "contentRoot": "./content/docs",
                      "summaryPath": "./Build/sync-project-docs-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(root, "content", "docs", "beta", "index.md")));
            Assert.False(File.Exists(Path.Combine(root, "content", "docs", "alpha", "index.md")));

            using var summaryDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Build", "sync-project-docs-summary.json")));
            Assert.Equal(1, summaryDoc.RootElement.GetProperty("totalProjects").GetInt32());
            Assert.Equal(1, summaryDoc.RootElement.GetProperty("docsProjects").GetInt32());
            Assert.Equal(1, summaryDoc.RootElement.GetProperty("synced").GetInt32());
            Assert.Equal(0, summaryDoc.RootElement.GetProperty("skipped").GetInt32());
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

    [Fact]
    public void RunPipeline_ProjectDocsSync_StampsCuratedExampleMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-sync-examples-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    {
                      "slug": "alpha-toolkit",
                      "surfaces": { "docs": false, "examples": true },
                      "links": {
                        "source": "https://github.com/EvotecIT/AlphaToolkit"
                      }
                    }
                  ]
                }
                """);

            var sourceExamples = Path.Combine(root, "projects-sources", "alpha-toolkit", "Website", "content", "examples");
            Directory.CreateDirectory(sourceExamples);
            File.WriteAllText(Path.Combine(sourceExamples, "build-alpha-report.md"),
                """
                ---
                title: "Build Alpha Report"
                layout: docs
                ---

                Curated example body.
                """);

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
                      "syncDocs": false,
                      "syncExamples": true,
                      "examplesRoot": "./content/project-examples",
                      "examplesSectionFolder": "examples",
                      "summaryPath": "./Build/sync-project-docs-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("examples=1/1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var examplePath = Path.Combine(root, "content", "project-examples", "alpha-toolkit", "examples", "build-alpha-report.md");
            Assert.True(File.Exists(examplePath));

            var example = File.ReadAllText(examplePath);
            Assert.Contains("title: \"Build Alpha Report\"", example, StringComparison.Ordinal);
            Assert.Contains("meta.project_base_slug: \"alpha-toolkit\"", example, StringComparison.Ordinal);
            Assert.Contains("meta.project_name: \"AlphaToolkit\"", example, StringComparison.Ordinal);
            Assert.Contains("meta.project_section: \"examples\"", example, StringComparison.Ordinal);
            Assert.Contains("meta.project_hub_path: \"/projects/alpha-toolkit/\"", example, StringComparison.Ordinal);
            Assert.Contains("meta.project_link_examples: \"/projects/alpha-toolkit/examples/\"", example, StringComparison.Ordinal);

            var indexPath = Path.Combine(root, "content", "project-examples", "alpha-toolkit", "examples", "_index.md");
            Assert.True(File.Exists(indexPath));
            var index = File.ReadAllText(indexPath);
            Assert.Contains("title: \"AlphaToolkit Examples\"", index, StringComparison.Ordinal);
            Assert.Contains("meta.project_base_slug: \"alpha-toolkit\"", index, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectDocsSync_DoesNotUseRawExamplesFolderByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-sync-examples-defaults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    { "slug": "alpha", "surfaces": { "docs": false, "examples": true } }
                  ]
                }
                """);

            var rawExamples = Path.Combine(root, "projects-sources", "alpha", "Examples");
            Directory.CreateDirectory(rawExamples);
            File.WriteAllText(Path.Combine(rawExamples, "Invoke-Alpha.ps1"), "Invoke-Alpha");

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
                      "syncDocs": false,
                      "syncExamples": true,
                      "examplesRoot": "./content/project-examples",
                      "summaryPath": "./Build/sync-project-docs-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("examples=0/1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("examplesSkipped=1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(root, "content", "project-examples", "alpha", "Invoke-Alpha.md")));
            Assert.False(File.Exists(Path.Combine(root, "content", "project-examples", "alpha", "Invoke-Alpha.ps1")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectDocsSync_UsesRawExamplesFolderWhenExplicitlyConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-sync-examples-explicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    { "slug": "alpha", "surfaces": { "docs": false, "examples": true } }
                  ]
                }
                """);

            var rawExamples = Path.Combine(root, "projects-sources", "alpha", "Examples");
            Directory.CreateDirectory(rawExamples);
            File.WriteAllText(Path.Combine(rawExamples, "Invoke-Alpha.ps1"), "Invoke-Alpha -Name Demo");

            var projectPage = Path.Combine(root, "content", "projects", "alpha.md");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPage)!);
            File.WriteAllText(projectPage,
                """
                ---
                title: "Alpha"
                meta.project_hub_path: "/projects/alpha/"
                meta.project_link_examples: "/projects/alpha/examples/"
                ---

                Alpha project page.
                """);

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
                      "syncDocs": false,
                      "syncExamples": true,
                      "examplesRoot": "./content/project-examples",
                      "sourceExamplesPaths": ["Examples"],
                      "summaryPath": "./Build/sync-project-docs-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("examples=1/1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var copiedScript = Path.Combine(root, "content", "project-examples", "alpha", "Invoke-Alpha.ps1");
            var generatedMarkdown = Path.Combine(root, "content", "project-examples", "alpha", "Invoke-Alpha.md");
            Assert.True(File.Exists(copiedScript));
            Assert.True(File.Exists(generatedMarkdown));

            var markdown = File.ReadAllText(generatedMarkdown);
            Assert.Contains("meta.generated_by: \"powerforge.project-docs-sync\"", markdown, StringComparison.Ordinal);
            Assert.Contains("meta.project_base_slug: \"alpha\"", markdown, StringComparison.Ordinal);
            Assert.Contains("meta.project_artifact_examples: \"Examples\"", File.ReadAllText(Path.Combine(root, "content", "projects", "alpha.md")), StringComparison.Ordinal);
            Assert.Contains("```powershell", markdown, StringComparison.Ordinal);
            Assert.Contains("Invoke-Alpha -Name Demo", markdown, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectDocsSync_CleanExamplesTargetRemovesStaleExampleFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-sync-examples-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    { "slug": "alpha", "surfaces": { "docs": false, "examples": true } }
                  ]
                }
                """);

            var sourceExamples = Path.Combine(root, "projects-sources", "alpha", "content", "examples");
            Directory.CreateDirectory(sourceExamples);
            File.WriteAllText(Path.Combine(sourceExamples, "current-example.md"),
                """
                ---
                title: "Current Example"
                ---

                Current curated example.
                """);

            var staleTarget = Path.Combine(root, "content", "project-examples", "alpha", "examples", "stale-example.md");
            Directory.CreateDirectory(Path.GetDirectoryName(staleTarget)!);
            File.WriteAllText(staleTarget, "# stale");

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
                      "syncDocs": false,
                      "syncExamples": true,
                      "examplesRoot": "./content/project-examples",
                      "examplesSectionFolder": "examples",
                      "cleanExamplesTarget": true,
                      "sourceExamplesPaths": ["content/examples"],
                      "summaryPath": "./Build/sync-project-docs-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("examples=1/1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(staleTarget));
            Assert.True(File.Exists(Path.Combine(root, "content", "project-examples", "alpha", "examples", "current-example.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectDocsSync_HydratesDocsApiAndExamplesFromZipArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-sync-artifact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var artifactZip = Path.Combine(root, "artifact.zip");
            using (var archive = ZipFile.Open(artifactZip, ZipArchiveMode.Create))
            {
                var docsEntry = archive.CreateEntry("bundle/Website/content/docs/index.md");
                using (var writer = new StreamWriter(docsEntry.Open()))
                    writer.Write("# Artifact Docs");

                var apiEntry = archive.CreateEntry("bundle/Website/data/apidocs/openapi.json");
                using (var writer = new StreamWriter(apiEntry.Open()))
                    writer.Write("""{ "openapi": "3.0.0" }""");

                var examplesEntry = archive.CreateEntry("bundle/Website/content/examples/getting-started.md");
                using (var writer = new StreamWriter(examplesEntry.Open()))
                    writer.Write("# Artifact Example");
            }

            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath,
                $$"""
                {
                  "projects": [
                    {
                      "slug": "alpha",
                      "contentMode": "hybrid",
                      "surfaces": { "docs": true, "apiPowerShell": true, "examples": true },
                      "artifacts": {
                        "docs": "{{artifactZip.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                        "api": "{{artifactZip.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                        "examples": "{{artifactZip.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
                      }
                    }
                  ]
                }
                """);

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
                      "syncExamples": true,
                      "examplesRoot": "./content/examples",
                      "sourceDocsPaths": ["Website/content/docs", "Docs"],
                      "sourceApiPaths": ["Website/data/apidocs", "data/apidocs"],
                      "sourceExamplesPaths": ["Website/content/examples", "Examples"],
                      "hydrateFromArtifacts": true,
                      "summaryPath": "./Build/sync-project-docs-summary.json",
                      "strict": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("examples=1/1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(Path.Combine(root, "content", "docs", "alpha", "index.md")));
            Assert.True(File.Exists(Path.Combine(root, "data", "apidocs", "alpha", "openapi.json")));
            Assert.True(File.Exists(Path.Combine(root, "content", "examples", "alpha", "getting-started.md")));

            var summaryPath = Path.Combine(root, "Build", "sync-project-docs-summary.json");
            Assert.True(File.Exists(summaryPath));
            using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.Equal(1, summary.RootElement.GetProperty("examplesProjects").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("syncedExamples").GetInt32());
            Assert.True(summary.RootElement.GetProperty("artifactDownloads").GetInt32() >= 0);
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
