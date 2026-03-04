using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerProjectCatalogTests
{
    [Fact]
    public void RunPipeline_ProjectCatalog_AllowsDedicatedExternalSurfaceWhenExternalUrlCanBackfillLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-catalog-surface-warn-" + Guid.NewGuid().ToString("N"));
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
                      "slug": "intelligencex",
                      "name": "IntelligenceX",
                      "mode": "dedicated-external",
                      "hubPath": "/projects/intelligencex/",
                      "githubRepo": "EvotecIT/IntelligenceX",
                      "externalUrl": "https://intelligencex.example.com/",
                      "links": {
                        "source": "https://github.com/EvotecIT/IntelligenceX"
                      },
                      "surfaces": {
                        "apiPowerShell": true
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
                      "task": "project-catalog",
                      "catalog": "./data/projects/catalog.json",
                      "summaryPath": "./summary.json",
                      "importManifests": false,
                      "applyCuration": false,
                      "mergeTelemetry": false,
                      "mergeReleaseTelemetry": false,
                      "generatePages": false,
                      "generateSections": false,
                      "validate": true,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("project-catalog ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_AllowsHubFullSurfacesWithoutExplicitLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-catalog-hub-full-" + Guid.NewGuid().ToString("N"));
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
                      "slug": "pspublishmodule",
                      "name": "PSPublishModule",
                      "mode": "hub-full",
                      "hubPath": "/projects/pspublishmodule/",
                      "githubRepo": "EvotecIT/PSPublishModule",
                      "status": "active",
                      "listed": false,
                      "surfaces": {
                        "docs": true,
                        "apiDotNet": true,
                        "apiPowerShell": true,
                        "changelog": true,
                        "releases": true
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
                      "task": "project-catalog",
                      "catalog": "./data/projects/catalog.json",
                      "summaryPath": "./summary.json",
                      "importManifests": false,
                      "applyCuration": false,
                      "mergeTelemetry": false,
                      "mergeReleaseTelemetry": false,
                      "generatePages": false,
                      "generateSections": false,
                      "validate": true,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("project-catalog ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_DerivesCanonicalLinksAndDisplayDates_ForHubProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-catalog-derived-links-" + Guid.NewGuid().ToString("N"));
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
                      "slug": "pswritehtml",
                      "name": "PSWriteHTML",
                      "mode": "hub-full",
                      "githubRepo": "EvotecIT/PSWriteHTML",
                      "description": "PSWriteHTML module",
                      "metrics": {
                        "github": {
                          "repository": "EvotecIT/PSWriteHTML",
                          "url": "https://github.com/EvotecIT/PSWriteHTML",
                          "language": "PowerShell",
                          "stars": 987,
                          "forks": 113,
                          "watchers": 987,
                          "openIssues": 69,
                          "archived": false,
                          "lastPushedAt": "2026-02-14T21:19:34+00:00"
                        },
                        "powerShellGallery": {
                          "id": "PSWriteHTML",
                          "version": "1.40.0",
                          "totalDownloads": 7374604,
                          "galleryUrl": "https://www.powershellgallery.com/packages/PSWriteHTML/1.40.0"
                        },
                        "release": {
                          "latestTag": "v1.40.0",
                          "latestName": "v1.40.0",
                          "latestUrl": "https://github.com/EvotecIT/PSWriteHTML/releases/tag/v1.40.0",
                          "latestPublishedAt": "2025-12-14T19:44:49+00:00",
                          "isPrerelease": false,
                          "isDraft": false
                        }
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
                      "task": "project-catalog",
                      "catalog": "./data/projects/catalog.json",
                      "contentRoot": "./content/projects",
                      "summaryPath": "./summary.json",
                      "importManifests": false,
                      "applyCuration": false,
                      "mergeTelemetry": false,
                      "mergeReleaseTelemetry": false,
                      "generatePages": true,
                      "generateSections": false,
                      "validate": true,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var catalogText = File.ReadAllText(catalogPath);
            Assert.Contains("\"hubPath\": \"/projects/pswritehtml/\"", catalogText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"docs\": \"/docs/pswritehtml/\"", catalogText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"apiPowerShell\": \"/api/pswritehtml/\"", catalogText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"source\": \"https://github.com/EvotecIT/PSWriteHTML\"", catalogText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"powerShellGallery\": \"https://www.powershellgallery.com/packages/PSWriteHTML/1.40.0\"", catalogText, StringComparison.OrdinalIgnoreCase);

            var projectPagePath = Path.Combine(root, "content", "projects", "pswritehtml.md");
            Assert.True(File.Exists(projectPagePath));
            var projectPage = File.ReadAllText(projectPagePath);
            Assert.Contains("meta.project_link_psgallery: \"https://www.powershellgallery.com/packages/PSWriteHTML/1.40.0\"", projectPage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("meta.project_github_last_pushed_at_display: \"2026-02-14\"", projectPage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("meta.project_release_latest_published_at_display: \"2025-12-14\"", projectPage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_ImportsHybridContentModeExamplesAndArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-catalog-hybrid-artifacts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": []
                }
                """);

            var sourceManifestPath = Path.Combine(root, "projects-sources", "alpha", "WebsiteArtifacts");
            Directory.CreateDirectory(sourceManifestPath);
            File.WriteAllText(Path.Combine(sourceManifestPath, "project-manifest.json"),
                """
                {
                  "slug": "alpha",
                  "name": "Alpha",
                  "mode": "hub-full",
                  "contentMode": "hybrid",
                  "description": "Alpha project",
                  "surfaces": {
                    "docs": true,
                    "apiPowerShell": true,
                    "examples": true
                  },
                  "links": {
                    "docs": "/docs/alpha/",
                    "apiPowerShell": "/api/alpha/",
                    "examples": "/examples/alpha/",
                    "source": "https://github.com/EvotecIT/Alpha"
                  },
                  "artifacts": {
                    "docs": "https://example.invalid/alpha-docs.zip",
                    "api": "https://example.invalid/alpha-api.zip",
                    "examples": "https://example.invalid/alpha-examples.zip"
                  }
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-catalog",
                      "catalog": "./data/projects/catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "contentRoot": "./content/projects",
                      "summaryPath": "./summary.json",
                      "importManifests": true,
                      "allowCreateProjects": true,
                      "applyCuration": false,
                      "mergeTelemetry": false,
                      "mergeReleaseTelemetry": false,
                      "generatePages": true,
                      "generateSections": true,
                      "validate": true,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var catalogText = File.ReadAllText(catalogPath);
            Assert.Contains("\"contentMode\": \"hybrid\"", catalogText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"examples\": true", catalogText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"examples\": \"/examples/alpha/\"", catalogText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"artifacts\":", catalogText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"docs\": \"https://example.invalid/alpha-docs.zip\"", catalogText, StringComparison.OrdinalIgnoreCase);

            var overviewPath = Path.Combine(root, "content", "projects", "alpha.md");
            var examplesSectionPath = Path.Combine(root, "content", "projects", "alpha.examples.md");
            Assert.True(File.Exists(overviewPath));
            Assert.True(File.Exists(examplesSectionPath));

            var overview = File.ReadAllText(overviewPath);
            Assert.Contains("meta.project_content_mode: \"hybrid\"", overview, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("meta.project_surface_examples: true", overview, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("meta.project_artifact_examples: \"https://example.invalid/alpha-examples.zip\"", overview, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_RemovesStaleGeneratedSectionPages()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-catalog-stale-sections-" + Guid.NewGuid().ToString("N"));
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
                      "slug": "alpha",
                      "name": "Alpha",
                      "mode": "hub-full",
                      "githubRepo": "EvotecIT/Alpha",
                      "surfaces": {
                        "docs": true,
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            var contentRoot = Path.Combine(root, "content", "projects");
            Directory.CreateDirectory(contentRoot);
            var staleExamplesPath = Path.Combine(contentRoot, "alpha.examples.md");
            File.WriteAllText(staleExamplesPath,
                """
                ---
                title: "Alpha Examples"
                meta.generated_by: powerforge.project-catalog
                ---

                stale
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-catalog",
                      "catalog": "./data/projects/catalog.json",
                      "contentRoot": "./content/projects",
                      "summaryPath": "./summary.json",
                      "importManifests": false,
                      "applyCuration": false,
                      "mergeTelemetry": false,
                      "mergeReleaseTelemetry": false,
                      "generatePages": false,
                      "generateSections": true,
                      "validate": true,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.False(File.Exists(staleExamplesPath));
            Assert.True(File.Exists(Path.Combine(contentRoot, "alpha.docs.md")));
            Assert.True(File.Exists(Path.Combine(contentRoot, "alpha.api.md")));
            Assert.Contains("staleSectionsDeleted=1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
