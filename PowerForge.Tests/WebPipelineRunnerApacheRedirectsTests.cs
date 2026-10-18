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
            Assert.Contains("RewriteCond %{REQUEST_URI} !^/(?:\\.well-known/acme-challenge(?:/|$)|_powerforge/deployment\\.json$) [NC]", config, StringComparison.Ordinal);
            Assert.DoesNotContain("RewriteRule ^ - [END]", config, StringComparison.Ordinal);
            Assert.Equal(
                Regex.Matches(config, "^RewriteCond %\\{REQUEST_URI\\} !", RegexOptions.Multiline).Count,
                Regex.Matches(config, "^RewriteRule .+\\[R=", RegexOptions.Multiline).Count);
            Assert.Contains("RewriteCond %{QUERY_STRING} (^|&)p=15(&|$)", config, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{QUERY_STRING} (^|&)page_id=40(&|$)", config, StringComparison.Ordinal);
            Assert.Contains("/new-url [R=301,L]", config, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(
                config,
                @"RewriteRule \^/\?old\\?-url/\?\$ /new-url \[R=301,L\]",
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
            Assert.Contains("RewriteRule ^/?docs/?$ /docs/current/ [R=301,L,QSD]", config, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("/allianz/", "/pl/allianz/", "pl", "!^/allianz/$")]
    [InlineData("https://evotec.pl/company-news/", "https://evotec.pl/company-news/", "", "!^/company-news/$ [OR]")]
    [InlineData("/", "/", "", "!^/$")]
    [InlineData("https://evotec.pl/caf%C3%A9/", "https://evotec.pl/caf%C3%A9/", "", "!^(?:/caf%C3%A9/|/café/)$ [OR]")]
    public void RunPipeline_ApacheRedirects_UsesSharedCanonicalPathGuard(string source, string target, string language, string condition)
    {
        var config = GenerateCsvRules(source, target, language);
        Assert.Contains("RewriteCond %{REQUEST_URI} " + condition, config, StringComparison.Ordinal);
        if (target.StartsWith("https://", StringComparison.Ordinal))
        {
            Assert.Contains(@"RewriteCond %{HTTP_HOST} !^evotec\.pl(?::443)?$ [NC,OR]", config, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{HTTPS} !^on$", config, StringComparison.Ordinal);
        }
        // Non-query CSV rules preserve queries; canonical requests with a query must also stop redirecting.
        Assert.DoesNotContain("RewriteCond %{QUERY_STRING} !^$", config, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evotec.pl/space%20report/", "/space%20report/", @"space\x20report", @"/space\%20report/", ",NE")]
    [InlineData("/old-price/", "/price$1/", "old-price", @"/price\$1/", "")]
    [InlineData("/old-section/", "/page/#section", "old-section", "/page/#section", ",NE")]
    public void RunPipeline_ApacheRedirects_PreservesLiteralUriCharacters(string source, string target, string pattern, string destination, string flag)
    {
        var config = GenerateCsvRules(source, target, "");
        Assert.Contains($"RewriteRule ^/?{pattern}/?$ {destination} [R=301,L{flag}]", config, StringComparison.Ordinal);
    }

    private static string GenerateCsvRules(string source, string target, string language)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apache-csv-canonical-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "redirects.csv"), $"legacy_url,target_url,status,language\n{source},{target},301,{language}\n");
            File.WriteAllText(Path.Combine(root, "pipeline.json"), """
                {"steps":[{"task":"apache-redirects","sources":["./redirects.csv"],"languageHostMap":{"pl":"evotec.pl"},"out":"./rules.conf"}]}
                """);
            Assert.True(WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null).Success);
            return File.ReadAllText(Path.Combine(root, "rules.conf"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
