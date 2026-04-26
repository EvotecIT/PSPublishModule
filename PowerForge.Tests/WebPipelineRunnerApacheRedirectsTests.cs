using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerApacheRedirectsTests
{
    [Fact]
    public void RunPipeline_ApacheRedirects_GeneratesRulesFromCsvSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apache-redirects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var redirectsDir = Path.Combine(root, "data", "redirects");
            Directory.CreateDirectory(redirectsDir);
            var firstCsv = Path.Combine(redirectsDir, "legacy-wordpress-map.csv");
            var secondCsv = Path.Combine(redirectsDir, "legacy-wordpress-generated.csv");
            File.WriteAllText(firstCsv,
                """
                legacy_url,target_url,status
                /old-url,/new-url,301
                /?p=15,/blog/new-post,301
                """);
            File.WriteAllText(secondCsv,
                """
                legacy_url,target_url,status
                /?page_id=40,/contact,302
                /old-url,/new-url,301
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apache-redirects",
                      "sources": [
                        "./data/redirects/legacy-wordpress-map.csv",
                        "./data/redirects/legacy-wordpress-generated.csv"
                      ],
                      "out": "./deploy/apache/wordpress-redirects.conf",
                      "summaryPath": "./Build/apache-redirects-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("apache-redirects ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var outputPath = Path.Combine(root, "deploy", "apache", "wordpress-redirects.conf");
            Assert.True(File.Exists(outputPath));
            var config = File.ReadAllText(outputPath);
            Assert.Contains("RewriteEngine On", config, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{QUERY_STRING} (^|&)p=15(&|$)", config, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{QUERY_STRING} (^|&)page_id=40(&|$)", config, StringComparison.Ordinal);
            Assert.Contains("/new-url [R=301,L]", config, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(
                config,
                @"RewriteRule \^old\\?-url/\?\$ /new-url \[R=301,L\]",
                RegexOptions.CultureInvariant).Cast<Match>());

            var summaryPath = Path.Combine(root, "Build", "apache-redirects-summary.json");
            Assert.True(File.Exists(summaryPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApacheRedirects_StrictFailsWhenNoSourceCsvExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apache-redirects-strict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apache-redirects",
                      "sources": [
                        "./data/redirects/missing.csv"
                      ],
                      "strict": true,
                      "out": "./deploy/apache/wordpress-redirects.conf"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("strict mode failed", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApacheRedirects_PreservesAbsoluteSourceQueryConstraint()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apache-redirects-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var redirectsDir = Path.Combine(root, "data", "redirects");
            Directory.CreateDirectory(redirectsDir);
            File.WriteAllText(Path.Combine(redirectsDir, "legacy.csv"),
                """
                legacy_url,target_url,status
                https://example.test/docs/?v=1,/docs/current/,301
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apache-redirects",
                      "sources": ["./data/redirects/legacy.csv"],
                      "out": "./deploy/apache/wordpress-redirects.conf"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            var config = File.ReadAllText(Path.Combine(root, "deploy", "apache", "wordpress-redirects.conf"));
            Assert.Contains("RewriteCond %{HTTP_HOST} ^(.+\\.)?example\\.test$ [NC]", config, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{QUERY_STRING} ^v=1$", config, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^docs/?$ /docs/current/ [R=301,L,QSD]", config, StringComparison.Ordinal);
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
