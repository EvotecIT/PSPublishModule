using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerLinksTests
{
    [Fact]
    public void RunPipeline_LinksExportApache_UsesSiteLinksConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data", "links"));
            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "name": "Links Test",
                  "baseUrl": "https://evotec.xyz",
                  "collections": [],
                  "links": {
                    "redirects": "./data/links/redirects.json",
                    "shortlinks": "./data/links/shortlinks.json",
                    "hosts": {
                      "short": "evo.yt"
                    },
                    "apacheOut": "./deploy/apache/link-service-redirects.conf"
                  }
                }
                """);
            File.WriteAllText(Path.Combine(root, "data", "links", "redirects.json"),
                """
                [
                  {
                    "id": "legacy",
                    "sourcePath": "/old/",
                    "targetUrl": "/new/",
                    "status": 301
                  }
                ]
                """);
            File.WriteAllText(Path.Combine(root, "data", "links", "shortlinks.json"),
                """
                [
                  {
                    "slug": "discord",
                    "host": "evo.yt",
                    "targetUrl": "https://discord.gg/example",
                    "status": 302,
                    "owner": "evotec",
                    "allowExternal": true
                  }
                ]
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-export-apache",
                      "config": "./site.json",
                      "includeErrorDocument404": true,
                      "summaryPath": "./Build/links-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("links-export-apache ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var outputPath = Path.Combine(root, "deploy", "apache", "link-service-redirects.conf");
            Assert.True(File.Exists(outputPath));
            var apache = File.ReadAllText(outputPath);
            Assert.Contains("RewriteRule ^old/?$ /new/ [R=301,L,QSD]", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^discord/?$ https://discord.gg/example [R=302,L,QSD]", apache, StringComparison.Ordinal);

            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Build", "links-summary.json")));
            Assert.Equal(1, summary.RootElement.GetProperty("redirects").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("shortlinks").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksValidate_FailsWhenConfigPathIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-missing-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-validate",
                      "config": "./missing-site.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("links config file not found", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksValidate_FailsOnUnsafeExternalTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data", "links"));
            File.WriteAllText(Path.Combine(root, "data", "links", "redirects.json"),
                """
                [
                  {
                    "id": "unsafe",
                    "sourcePath": "/old/",
                    "targetUrl": "https://example.com/new",
                    "status": 301
                  }
                ]
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-validate",
                      "redirects": "./data/links/redirects.json",
                      "summaryPath": "./Build/links-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("failed", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksValidate_WritesDuplicateReviewReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-duplicates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data", "links"));
            File.WriteAllText(Path.Combine(root, "data", "links", "redirects.json"),
                """
                [
                  {
                    "id": "canonical",
                    "sourcePath": "/old/",
                    "targetUrl": "/new/",
                    "status": 301
                  },
                  {
                    "id": "duplicate",
                    "sourcePath": "/old",
                    "targetUrl": "/new",
                    "status": 301
                  }
                ]
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-validate",
                      "redirects": "./data/links/redirects.json",
                      "summaryPath": "./Build/links-summary.json",
                      "duplicateReportPath": "./Build/duplicates.csv"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            var reportPath = Path.Combine(root, "Build", "duplicates.csv");
            Assert.True(File.Exists(reportPath));
            var report = File.ReadAllText(reportPath);
            Assert.Contains("suggested_action", report, StringComparison.Ordinal);
            Assert.Contains("dedupe_generated_or_imported_row", report, StringComparison.Ordinal);
            Assert.Contains("canonical", report, StringComparison.Ordinal);
            Assert.Contains("duplicate", report, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksValidate_FailOnNewWarningsUsesBaseline()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data", "links"));
            var redirectsPath = Path.Combine(root, "data", "links", "redirects.json");
            File.WriteAllText(redirectsPath,
                """
                [
                  {
                    "id": "canonical",
                    "sourcePath": "/old/",
                    "targetUrl": "/new/",
                    "status": 301
                  },
                  {
                    "id": "duplicate",
                    "sourcePath": "/old",
                    "targetUrl": "/new",
                    "status": 301
                  }
                ]
                """);

            var baselinePipelinePath = Path.Combine(root, "pipeline-baseline.json");
            File.WriteAllText(baselinePipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-validate",
                      "redirects": "./data/links/redirects.json",
                      "baseline": "./.powerforge/link-baseline.json",
                      "baselineGenerate": true,
                      "summaryPath": "./Build/links-summary.json"
                    }
                  ]
                }
                """);

            var baselineResult = WebPipelineRunner.RunPipeline(baselinePipelinePath, logger: null);
            Assert.True(baselineResult.Success);
            Assert.True(File.Exists(Path.Combine(root, ".powerforge", "link-baseline.json")));

            var failOnNewPipelinePath = Path.Combine(root, "pipeline-fail-on-new.json");
            File.WriteAllText(failOnNewPipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-validate",
                      "redirects": "./data/links/redirects.json",
                      "baseline": "./.powerforge/link-baseline.json",
                      "failOnNewWarnings": true,
                      "summaryPath": "./Build/links-summary.json"
                    }
                  ]
                }
                """);

            var existingResult = WebPipelineRunner.RunPipeline(failOnNewPipelinePath, logger: null);
            Assert.True(existingResult.Success);
            Assert.Contains("newWarnings=0", existingResult.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(redirectsPath,
                """
                [
                  {
                    "id": "canonical",
                    "sourcePath": "/old/",
                    "targetUrl": "/new/",
                    "status": 301
                  },
                  {
                    "id": "duplicate",
                    "sourcePath": "/old",
                    "targetUrl": "/new",
                    "status": 301
                  },
                  {
                    "id": "second-canonical",
                    "sourcePath": "/legacy/",
                    "targetUrl": "/modern/",
                    "status": 301
                  },
                  {
                    "id": "second-duplicate",
                    "sourcePath": "/legacy",
                    "targetUrl": "/modern",
                    "status": 301
                  }
                ]
                """);

            var newWarningResult = WebPipelineRunner.RunPipeline(failOnNewPipelinePath, logger: null);
            Assert.False(newWarningResult.Success);
            Assert.Contains("newWarnings=1", newWarningResult.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksImportWordPress_WritesShortlinksAndSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data", "links", "imports"));
            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "name": "Links Import Test",
                  "baseUrl": "https://evotec.xyz",
                  "collections": [],
                  "links": {
                    "shortlinks": "./data/links/shortlinks.json",
                    "hosts": {
                      "short": "evo.yt"
                    }
                  }
                }
                """);
            File.WriteAllText(Path.Combine(root, "data", "links", "imports", "pretty-links.csv"),
                """
                id,name,slug,url,clicks
                10,Teams,teams,https://teams.example.test,42
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-import-wordpress",
                      "config": "./site.json",
                      "source": "./data/links/imports/pretty-links.csv",
                      "owner": "evotec",
                      "tags": [ "imported" ],
                      "summaryPath": "./Build/import-links-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Contains("links-import-wordpress ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var shortlinksPath = Path.Combine(root, "data", "links", "shortlinks.json");
            Assert.True(File.Exists(shortlinksPath));
            var json = File.ReadAllText(shortlinksPath);
            Assert.Contains("\"slug\": \"teams\"", json, StringComparison.Ordinal);
            Assert.Contains("\"host\": \"evo.yt\"", json, StringComparison.Ordinal);
            Assert.Contains("\"importedHits\": 42", json, StringComparison.Ordinal);

            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Build", "import-links-summary.json")));
            Assert.Equal(1, summary.RootElement.GetProperty("importedCount").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("writtenCount").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksReport404_WritesSuggestionReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_site", "docs", "install"));
            File.WriteAllText(Path.Combine(root, "_site", "docs", "install", "index.html"), "<html>install</html>");
            File.WriteAllText(Path.Combine(root, "access.log"), "127.0.0.1 - - [01/Jan/2026:00:00:00 +0000] \"GET /docs/instal HTTP/1.1\" 404 123 \"-\" \"test\"");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-report-404",
                      "siteRoot": "./_site",
                      "source": "./access.log",
                      "out": "./Build/404-suggestions.json",
                      "reviewCsv": "./Build/404-suggestions.csv"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Contains("links-report-404 ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            var json = File.ReadAllText(Path.Combine(root, "Build", "404-suggestions.json"));
            Assert.Contains("\"targetPath\": \"/docs/install/\"", json, StringComparison.Ordinal);
            var csv = File.ReadAllText(Path.Combine(root, "Build", "404-suggestions.csv"));
            Assert.Contains("review_redirect_candidate", csv, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksReport404_AllowsMissingSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-404-missing-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_site"));
            File.WriteAllText(Path.Combine(root, "_site", "index.html"), "<html>home</html>");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-report-404",
                      "siteRoot": "./_site",
                      "source": "./missing.log",
                      "out": "./Build/404-suggestions.json",
                      "reviewCsv": "./Build/404-suggestions.csv",
                      "allowMissingSource": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Contains("observations=0", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, "Build", "404-suggestions.json")));
            Assert.True(File.Exists(Path.Combine(root, "Build", "404-suggestions.csv")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksPromote404_WritesRedirectCandidates()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-promote-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "404-suggestions.json"),
                """
                {
                  "suggestions": [
                    {
                      "path": "/docs/instal",
                      "host": "evotec.xyz",
                      "count": 2,
                      "suggestions": [
                        { "targetPath": "/docs/install/", "score": 0.91 }
                      ]
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
                      "task": "links-promote-404",
                      "source": "./404-suggestions.json",
                      "out": "./data/links/redirects.json",
                      "reviewCsv": "./Build/promoted-redirects.csv",
                      "summaryPath": "./Build/promote-404-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Contains("links-promote-404 ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            var json = File.ReadAllText(Path.Combine(root, "data", "links", "redirects.json"));
            Assert.Contains("\"sourcePath\": \"/docs/instal\"", json, StringComparison.Ordinal);
            var csv = File.ReadAllText(Path.Combine(root, "Build", "promoted-redirects.csv"));
            Assert.Contains("/docs/install/", csv, StringComparison.Ordinal);

            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Build", "promote-404-summary.json")));
            Assert.Equal(1, summary.RootElement.GetProperty("candidateCount").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("writtenCount").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_LinksIgnore404_WritesIgnoredRules()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-ignore-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "404-suggestions.json"),
                """
                {
                  "suggestions": [
                    {
                      "path": "/wp-login.php",
                      "count": 4,
                      "suggestions": []
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
                      "task": "links-ignore-404",
                      "source": "./404-suggestions.json",
                      "out": "./data/links/ignored-404.json",
                      "path": "/wp-login.php",
                      "reason": "scanner noise",
                      "reviewCsv": "./Build/ignored-404.csv",
                      "summaryPath": "./Build/ignore-404-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Contains("links-ignore-404 ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            var json = File.ReadAllText(Path.Combine(root, "data", "links", "ignored-404.json"));
            Assert.Contains("\"path\": \"/wp-login.php\"", json, StringComparison.Ordinal);
            var csv = File.ReadAllText(Path.Combine(root, "Build", "ignored-404.csv"));
            Assert.Contains("scanner noise", csv, StringComparison.Ordinal);

            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Build", "ignore-404-summary.json")));
            Assert.Equal(1, summary.RootElement.GetProperty("candidateCount").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("writtenCount").GetInt32());
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
            // best-effort cleanup
        }
    }
}
