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

            using var runSummary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "summary.json")));
            Assert.Equal("fallback", runSummary.RootElement.GetProperty("status").GetString());
            Assert.Equal("existing-on-warning-empty", runSummary.RootElement.GetProperty("reason").GetString());
            Assert.Equal(7, runSummary.RootElement.GetProperty("repositoryCount").GetInt32());
            Assert.Equal(4, runSummary.RootElement.GetProperty("nugetPackageCount").GetInt32());
            Assert.Equal(3, runSummary.RootElement.GetProperty("powerShellGalleryModuleCount").GetInt32());
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

    [Fact]
    public void RunPipeline_EcosystemStats_PreservesExistingPowerShellGallery_WhenOnlyThatSourceFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-ecosystem-partial-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var statsPath = Path.Combine(root, "data", "ecosystem", "stats.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statsPath)!);
            File.WriteAllText(statsPath,
                """
                {
                  "title": "Ecosystem Stats",
                  "generatedAtUtc": "2026-03-03T00:00:00.0000000Z",
                  "summary": {
                    "repositoryCount": 4,
                    "nuGetPackageCount": 2,
                    "powerShellGalleryModuleCount": 3,
                    "gitHubStars": 10,
                    "gitHubForks": 2,
                    "nuGetDownloads": 500,
                    "powerShellGalleryDownloads": 9000,
                    "totalDownloads": 9500
                  },
                  "gitHub": {
                    "organization": "EvotecIT",
                    "repositoryCount": 4,
                    "totalStars": 10,
                    "totalForks": 2,
                    "repositories": []
                  },
                  "nuget": {
                    "owner": "Evotec",
                    "packageCount": 2,
                    "totalDownloads": 500,
                    "packages": []
                  },
                  "powerShellGallery": {
                    "owner": "Przemyslaw.Klys",
                    "moduleCount": 3,
                    "totalDownloads": 9000,
                    "modules": [
                      { "id": "PSWriteHTML", "downloadCount": 9000 }
                    ]
                  },
                  "warnings": []
                }
                """);

            File.WriteAllText(statsPath,
                """
                {
                  "title": "Ecosystem Stats",
                  "generatedAtUtc": "2026-03-03T00:00:00.0000000Z",
                  "summary": {
                    "repositoryCount": 4,
                    "nuGetPackageCount": 2,
                    "powerShellGalleryModuleCount": 3,
                    "gitHubStars": 10,
                    "gitHubForks": 2,
                    "nuGetDownloads": 500,
                    "powerShellGalleryDownloads": 9000,
                    "totalDownloads": 9500
                  },
                  "gitHub": {
                    "organization": "EvotecIT",
                    "repositoryCount": 4,
                    "totalStars": 10,
                    "totalForks": 2,
                    "repositories": []
                  },
                  "nuget": {
                    "owner": "Evotec",
                    "packageCount": 2,
                    "totalDownloads": 500,
                    "packages": []
                  },
                  "powerShellGallery": {
                    "owner": "Przemyslaw.Klys",
                    "moduleCount": 3,
                    "totalDownloads": 9000,
                    "modules": [
                      { "id": "PSWriteHTML", "downloadCount": 9000 }
                    ]
                  },
                  "warnings": []
                }
                """);

            var generatedPath = Path.Combine(root, "generated.json");
            File.WriteAllText(generatedPath,
                """
                {
                  "title": "Ecosystem Stats",
                  "generatedAtUtc": "2026-03-04T00:00:00.0000000Z",
                  "summary": {
                    "repositoryCount": 5,
                    "nuGetPackageCount": 3,
                    "powerShellGalleryModuleCount": 0,
                    "gitHubStars": 12,
                    "gitHubForks": 3,
                    "nuGetDownloads": 700,
                    "powerShellGalleryDownloads": 0,
                    "totalDownloads": 700
                  },
                  "gitHub": {
                    "organization": "EvotecIT",
                    "repositoryCount": 5,
                    "totalStars": 12,
                    "totalForks": 3,
                    "repositories": []
                  },
                  "nuget": {
                    "owner": "Evotec",
                    "packageCount": 3,
                    "totalDownloads": 700,
                    "packages": []
                  },
                  "powerShellGallery": {
                    "owner": "Przemyslaw.Klys",
                    "moduleCount": 0,
                    "totalDownloads": 0,
                    "modules": []
                  },
                  "warnings": [
                    "PowerShell Gallery request failed: TaskCanceledException: timeout"
                  ]
                }
                """);

            var preserveMethod = typeof(WebPipelineRunner)
                .GetMethod("TryPreserveEcosystemSources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(preserveMethod);

            var args = new object?[] { File.ReadAllText(statsPath), generatedPath, true, true, true, false, 30, null };
            var preserved = Assert.IsType<bool>(preserveMethod!.Invoke(null, args));
            Assert.True(preserved);

            using var document = JsonDocument.Parse(File.ReadAllText(generatedPath));
            var summary = document.RootElement.GetProperty("summary");
            Assert.Equal(5, summary.GetProperty("repositoryCount").GetInt32());
            Assert.Equal(3, summary.GetProperty("nuGetPackageCount").GetInt32());
            Assert.Equal(3, summary.GetProperty("powerShellGalleryModuleCount").GetInt32());
            Assert.Equal(9700L, summary.GetProperty("totalDownloads").GetInt64());

            var gallery = document.RootElement.GetProperty("powerShellGallery");
            Assert.Equal(3, gallery.GetProperty("moduleCount").GetInt32());
            Assert.Equal(9000L, gallery.GetProperty("totalDownloads").GetInt64());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_EcosystemStats_SyncsProjectCatalogTelemetry()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-ecosystem-catalog-telemetry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var statsPath = Path.Combine(root, "data", "ecosystem", "stats.json");
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            var publishedCatalogPath = Path.Combine(root, "static", "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statsPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(statsPath,
                """
                {
                  "title": "Ecosystem Stats",
                  "generatedAtUtc": "2026-03-03T00:00:00.0000000Z",
                  "summary": {
                    "repositoryCount": 1,
                    "nuGetPackageCount": 0,
                    "powerShellGalleryModuleCount": 1,
                    "gitHubStars": 42,
                    "gitHubForks": 5,
                    "nuGetDownloads": 0,
                    "powerShellGalleryDownloads": 123456,
                    "totalDownloads": 123456
                  },
                  "gitHub": {
                    "organization": "EvotecIT",
                    "repositoryCount": 1,
                    "totalStars": 42,
                    "totalForks": 5,
                    "repositories": [
                      {
                        "name": "SecurityPolicy",
                        "fullName": "EvotecIT/SecurityPolicy",
                        "url": "https://github.com/EvotecIT/SecurityPolicy",
                        "language": "PowerShell",
                        "stars": 42,
                        "forks": 5,
                        "watchers": 7,
                        "openIssues": 3
                      }
                    ]
                  },
                  "powerShellGallery": {
                    "owner": "Przemyslaw.Klys",
                    "moduleCount": 1,
                    "totalDownloads": 123456,
                    "modules": [
                      {
                        "id": "SecurityPolicy",
                        "version": "0.0.13",
                        "downloadCount": 123456,
                        "galleryUrl": "https://www.powershellgallery.com/packages/SecurityPolicy",
                        "projectUrl": "https://github.com/EvotecIT/SecurityPolicy"
                      }
                    ]
                  },
                  "warnings": []
                }
                """);
            File.WriteAllText(catalogPath,
                """
                {
                  "generatedOn": "2026-03-02T00:00:00.0000000Z",
                  "projects": [
                    {
                      "slug": "securitypolicy",
                      "name": "SecurityPolicy",
                      "githubRepo": "EvotecIT/SecurityPolicy",
                      "links": {
                        "powerShellGallery": "https://www.powershellgallery.com/packages/SecurityPolicy"
                      },
                      "metrics": {
                        "github": {
                          "repository": "EvotecIT/SecurityPolicy",
                          "stars": 1
                        },
                        "powerShellGallery": {
                          "id": "SecurityPolicy",
                          "totalDownloads": 1
                        },
                        "downloads": {
                          "total": 1,
                          "powerShellGallery": 1
                        }
                      }
                    }
                  ]
                }
                """);

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
                      "syncProjectCatalogTelemetry": true,
                      "projectCatalogPath": "./data/projects/catalog.json",
                      "projectCatalogPublishPath": "./static/data/projects/catalog.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("projectCatalogTelemetry=1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var project = catalog.RootElement.GetProperty("projects")[0];
            const long staleCatalogSeedDownloads = 1L;
            Assert.Equal(42, project.GetProperty("metrics").GetProperty("github").GetProperty("stars").GetInt32());
            // This run requests only GitHub stats, so catalog sync preserves the stale seeded PSGallery metrics.
            Assert.Equal(staleCatalogSeedDownloads, project.GetProperty("metrics").GetProperty("powerShellGallery").GetProperty("totalDownloads").GetInt64());
            Assert.Equal(staleCatalogSeedDownloads, project.GetProperty("metrics").GetProperty("downloads").GetProperty("total").GetInt64());
            Assert.True(File.Exists(publishedCatalogPath));

            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "summary.json")));
            Assert.Equal(1, summary.RootElement.GetProperty("projectCatalogTelemetry").GetProperty("merged").GetInt32());
            Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("projectCatalogTelemetry").GetProperty("warning").ValueKind);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SyncProjectCatalogTelemetryFromStats_MergesPowerShellGalleryDownloads_WhenStatsIncludeGallery()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-ecosystem-catalog-telemetry-direct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var statsPath = Path.Combine(root, "data", "ecosystem", "stats.json");
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            var publishedCatalogPath = Path.Combine(root, "static", "data", "projects", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statsPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllText(statsPath,
                """
                {
                  "title": "Ecosystem Stats",
                  "generatedAtUtc": "2026-03-03T00:00:00.0000000Z",
                  "summary": {
                    "repositoryCount": 1,
                    "nuGetPackageCount": 0,
                    "powerShellGalleryModuleCount": 1,
                    "gitHubStars": 42,
                    "gitHubForks": 5,
                    "nuGetDownloads": 0,
                    "powerShellGalleryDownloads": 123456,
                    "totalDownloads": 123456
                  },
                  "gitHub": {
                    "organization": "EvotecIT",
                    "repositoryCount": 1,
                    "totalStars": 42,
                    "totalForks": 5,
                    "repositories": [
                      {
                        "name": "SecurityPolicy",
                        "fullName": "EvotecIT/SecurityPolicy",
                        "url": "https://github.com/EvotecIT/SecurityPolicy",
                        "language": "PowerShell",
                        "stars": 42,
                        "forks": 5,
                        "watchers": 7,
                        "openIssues": 3
                      }
                    ]
                  },
                  "powerShellGallery": {
                    "owner": "Przemyslaw.Klys",
                    "moduleCount": 1,
                    "totalDownloads": 123456,
                    "modules": [
                      {
                        "id": "SecurityPolicy",
                        "version": "0.0.13",
                        "downloadCount": 123456,
                        "galleryUrl": "https://www.powershellgallery.com/packages/SecurityPolicy",
                        "projectUrl": "https://github.com/EvotecIT/SecurityPolicy"
                      }
                    ]
                  },
                  "warnings": []
                }
                """);
            File.WriteAllText(catalogPath,
                """
                {
                  "generatedOn": "2026-03-02T00:00:00.0000000Z",
                  "projects": [
                    {
                      "slug": "securitypolicy",
                      "name": "SecurityPolicy",
                      "githubRepo": "EvotecIT/SecurityPolicy",
                      "links": {
                        "powerShellGallery": "https://www.powershellgallery.com/packages/SecurityPolicy"
                      },
                      "metrics": {
                        "github": {
                          "repository": "EvotecIT/SecurityPolicy",
                          "stars": 1
                        },
                        "powerShellGallery": {
                          "id": "SecurityPolicy",
                          "totalDownloads": 1
                        },
                        "downloads": {
                          "total": 1,
                          "powerShellGallery": 1
                        }
                      }
                    }
                  ]
                }
                """);

            var syncMethod = typeof(WebPipelineRunner)
                .GetMethod("SyncProjectCatalogTelemetryFromStats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(syncMethod);

            var args = new object?[] { statsPath, catalogPath, publishedCatalogPath, null };
            var merged = Assert.IsType<int>(syncMethod!.Invoke(null, args));
            Assert.Equal(1, merged);
            Assert.Null(args[3]);

            using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var project = catalog.RootElement.GetProperty("projects")[0];
            Assert.Equal(42, project.GetProperty("metrics").GetProperty("github").GetProperty("stars").GetInt32());
            Assert.Equal(123456L, project.GetProperty("metrics").GetProperty("powerShellGallery").GetProperty("totalDownloads").GetInt64());
            Assert.Equal(123456L, project.GetProperty("metrics").GetProperty("downloads").GetProperty("total").GetInt64());
            Assert.True(File.Exists(publishedCatalogPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildPowerShellGalleryPackageByIdUrl_EscapesODataLiteralBeforeUrlEncoding()
    {
        var urlMethod = typeof(WebPipelineRunner)
            .GetMethod("BuildPowerShellGalleryPackageByIdUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(urlMethod);

        var url = Assert.IsType<string>(urlMethod!.Invoke(null, new object?[] { "Name'With Space" }));
        var uri = new Uri(url);
        var filterQuery = uri.Query.TrimStart('?');
        Assert.StartsWith("$filter=", filterQuery, StringComparison.Ordinal);
        var filter = Uri.UnescapeDataString(filterQuery["$filter=".Length..]);

        Assert.Equal("Id eq 'Name''With Space' and IsLatestVersion eq true", filter);
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
