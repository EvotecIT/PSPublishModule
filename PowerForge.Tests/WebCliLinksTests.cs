using System;
using System.IO;
using PowerForge.Web;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebCliLinksTests
{
    private const int CliEnvelopeSchemaVersion = 1;

    [Fact]
    public void HandleSubCommand_LinksValidate_UsesBaselineAndWritesDuplicateReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-links-validate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = WriteSiteFixture(root, duplicateRedirects: true);
            var baselinePath = Path.Combine(root, ".powerforge", "link-baseline.json");
            var duplicateReportPath = Path.Combine(root, "Build", "duplicates.csv");

            var generateExitCode = WebCliCommandHandlers.HandleSubCommand(
                "links",
                new[] { "validate", "--config", configPath, "--baseline", baselinePath, "--baseline-generate" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, generateExitCode);
            Assert.True(File.Exists(baselinePath));

            var validateExitCode = WebCliCommandHandlers.HandleSubCommand(
                "links",
                new[] { "validate", "--config", configPath, "--baseline", baselinePath, "--fail-on-new-warnings", "--duplicate-report-path", duplicateReportPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, validateExitCode);
            Assert.True(File.Exists(duplicateReportPath));
            var report = File.ReadAllText(duplicateReportPath);
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
    public void HandleSubCommand_LinksExportApache_WritesConfiguredOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-links-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = WriteSiteFixture(root, duplicateRedirects: false);

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "links",
                new[] { "export-apache", "--config", configPath, "--include-404" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, exitCode);

            var outputPath = Path.Combine(root, "deploy", "apache", "link-service-redirects.conf");
            Assert.True(File.Exists(outputPath));
            var apache = File.ReadAllText(outputPath);
            Assert.Contains("ErrorDocument 404 /404.html", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^/?old/?$ /new/ [R=301,L,QSD]", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^/?discord/?$ https://discord.gg/example [R=302,L,QSD]", apache, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void EvaluateBaseline_AcceptsStableAndLegacyWarningKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-links-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var baselinePath = Path.Combine(root, ".powerforge", "link-baseline.json");
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            var legacyBaselineIssue = BuildMissingOwnerIssue("legacy-baseline");
            var stableBaselineIssue = BuildMissingOwnerIssue("stable-baseline", "/stable");
            var legacyCurrentIssue = BuildMissingOwnerIssue("legacy-current");
            var stableCurrentIssue = BuildMissingOwnerIssue("stable-current", "/stable");
            var newIssue = BuildMissingOwnerIssue("new", "/new");
            File.WriteAllText(baselinePath,
                $$"""
                {
                  "version": 1,
                  "warningKeys": [
                    {{JsonString(WebLinkCommandSupport.BuildLegacyIssueKey(legacyBaselineIssue))}},
                    {{JsonString(WebLinkCommandSupport.BuildIssueKey(stableBaselineIssue))}}
                  ]
                }
                """);

            var state = WebLinkCommandSupport.EvaluateBaseline(
                root,
                baselinePath,
                new LinkValidationResult
                {
                    Issues = new[] { legacyCurrentIssue, stableCurrentIssue, newIssue },
                    WarningCount = 3
                },
                baselineGenerate: false,
                baselineUpdate: false,
                failOnNewWarnings: true);

            var warning = Assert.Single(state.NewWarnings);
            Assert.Equal("new", warning.Id);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_LinksValidate_AcceptsCamelCaseDirectSourceFlags()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-links-direct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            _ = WriteSiteFixture(root, duplicateRedirects: false);
            var redirectsPath = Path.Combine(root, "data", "links", "redirects.json");
            var shortlinksPath = Path.Combine(root, "data", "links", "shortlinks.json");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "links",
                new[] { "validate", "--redirectsPath", redirectsPath, "--shortlinksPath", shortlinksPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static LinkValidationIssue BuildMissingOwnerIssue(string id, string sourcePath = "/go")
        => new()
        {
            Severity = LinkValidationSeverity.Warning,
            Code = "PFLINK.SHORTLINK.OWNER",
            Source = "shortlink",
            Id = id,
            SourceHost = "evo.yt",
            SourcePath = sourcePath,
            Status = 302,
            TargetUrl = "https://example.test"
        };

    private static string JsonString(string value)
        => System.Text.Json.JsonSerializer.Serialize(value);

    [Fact]
    public void HandleSubCommand_LinksImportWordPress_ImportsPrettyLinksCsv()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-links-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = WriteSiteFixture(root, duplicateRedirects: false);
            var importPath = Path.Combine(root, "pretty-links.csv");
            File.WriteAllText(importPath,
                """
                id,name,slug,url,clicks,created_at
                10,Teams,teams,https://teams.example.test,42,2024-01-02T03:04:05Z
                """);

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "links",
                new[] { "import-wordpress", "--config", configPath, "--source", importPath, "--host", "short=evo.yt", "--owner", "evotec", "--tag", "imported" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, exitCode);

            var shortlinksPath = Path.Combine(root, "data", "links", "shortlinks.json");
            var json = File.ReadAllText(shortlinksPath);
            Assert.Contains("\"slug\": \"teams\"", json, StringComparison.Ordinal);
            Assert.Contains("\"host\": \"evo.yt\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"host\": \"short=evo.yt\"", json, StringComparison.Ordinal);
            Assert.Contains("\"targetUrl\": \"https://teams.example.test\"", json, StringComparison.Ordinal);
            Assert.Contains("\"importedHits\": 42", json, StringComparison.Ordinal);
            Assert.Contains("\"source\": \"imported-pretty-links\"", json, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_LinksReport404_WritesSuggestionReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-links-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_site", "docs", "install"));
            File.WriteAllText(Path.Combine(root, "_site", "docs", "install", "index.html"), "<html>install</html>");
            var logPath = Path.Combine(root, "access.log");
            File.WriteAllText(logPath, "127.0.0.1 - - [01/Jan/2026:00:00:00 +0000] \"GET /docs/instal HTTP/1.1\" 404 123 \"-\" \"test\"");
            var reportPath = Path.Combine(root, "Build", "404-suggestions.json");
            var reviewCsvPath = Path.Combine(root, "Build", "404-suggestions.csv");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "links",
                new[] { "report-404", "--site-root", Path.Combine(root, "_site"), "--source", logPath, "--out", reportPath, "--review-csv", reviewCsvPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(reportPath));
            var json = File.ReadAllText(reportPath);
            Assert.Contains("\"path\":\"/docs/instal\"", json, StringComparison.Ordinal);
            Assert.Contains("\"targetPath\":\"/docs/install/\"", json, StringComparison.Ordinal);
            var csv = File.ReadAllText(reviewCsvPath);
            Assert.Contains("review_redirect_candidate", csv, StringComparison.Ordinal);
            Assert.Contains("/docs/install/", csv, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_LinksReview404_WritesReviewArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-links-review-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = WriteSiteFixture(root, duplicateRedirects: false);
            Directory.CreateDirectory(Path.Combine(root, "_site", "docs", "install"));
            File.WriteAllText(Path.Combine(root, "_site", "docs", "install", "index.html"), "<html>install</html>");
            var logPath = Path.Combine(root, "access.log");
            File.WriteAllText(logPath, "127.0.0.1 - - [01/Jan/2026:00:00:00 +0000] \"GET /docs/instal HTTP/1.1\" 404 123 \"-\" \"test\"");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "links",
                new[] { "review-404", "--config", configPath, "--site-root", Path.Combine(root, "_site"), "--source", logPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, exitCode);
            var reportsPath = Path.Combine(root, "Build", "link-reports");
            Assert.True(File.Exists(Path.Combine(reportsPath, "404-suggestions.json")));
            Assert.True(File.Exists(Path.Combine(reportsPath, "404-suggestions.csv")));
            Assert.True(File.Exists(Path.Combine(reportsPath, "404-promoted-candidates.json")));
            Assert.True(File.Exists(Path.Combine(reportsPath, "404-promoted-candidates.csv")));
            Assert.True(File.Exists(Path.Combine(reportsPath, "ignored-404-candidates.json")));
            Assert.True(File.Exists(Path.Combine(reportsPath, "ignored-404-candidates.csv")));
            Assert.Contains("\"sourcePath\": \"/docs/instal\"", File.ReadAllText(Path.Combine(reportsPath, "404-promoted-candidates.json")), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_LinksPromote404_WritesRedirectCandidates()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-links-promote-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var reportPath = Path.Combine(root, "404-suggestions.json");
            File.WriteAllText(reportPath,
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
            var redirectsPath = Path.Combine(root, "data", "links", "redirects.json");
            var reviewCsvPath = Path.Combine(root, "Build", "promoted-redirects.csv");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "links",
                new[] { "promote-404", "--source", reportPath, "--out", redirectsPath, "--review-csv", reviewCsvPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, exitCode);
            var json = File.ReadAllText(redirectsPath);
            Assert.Contains("\"sourcePath\": \"/docs/instal\"", json, StringComparison.Ordinal);
            Assert.Contains("\"targetUrl\": \"/docs/install/\"", json, StringComparison.Ordinal);
            Assert.Contains("\"enabled\": false", json, StringComparison.Ordinal);
            var csv = File.ReadAllText(reviewCsvPath);
            Assert.Contains("/docs/instal", csv, StringComparison.Ordinal);
            Assert.Contains("/docs/install/", csv, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_LinksIgnore404_WritesIgnoredRules()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-links-ignore-404-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var reportPath = Path.Combine(root, "404-suggestions.json");
            File.WriteAllText(reportPath,
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
            var ignoredPath = Path.Combine(root, "data", "links", "ignored-404.json");
            var reviewCsvPath = Path.Combine(root, "Build", "ignored-404.csv");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "links",
                new[] { "ignore-404", "--source", reportPath, "--out", ignoredPath, "--path", "/wp-login.php", "--reason", "scanner noise", "--review-csv", reviewCsvPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, exitCode);
            var json = File.ReadAllText(ignoredPath);
            Assert.Contains("\"path\": \"/wp-login.php\"", json, StringComparison.Ordinal);
            Assert.Contains("\"reason\": \"scanner noise\"", json, StringComparison.Ordinal);
            var csv = File.ReadAllText(reviewCsvPath);
            Assert.Contains("/wp-login.php", csv, StringComparison.Ordinal);
            Assert.Contains("scanner noise", csv, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_LinksApplyReview_AppliesCandidateFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-links-apply-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = WriteSiteFixture(root, duplicateRedirects: false);
            var reportsPath = Path.Combine(root, "Build", "link-reports");
            Directory.CreateDirectory(reportsPath);
            File.WriteAllText(Path.Combine(reportsPath, "404-promoted-candidates.json"),
                """
                {
                  "redirects": [
                    {
                      "id": "reviewed",
                      "sourcePath": "/docs/instal",
                      "targetUrl": "/docs/install/",
                      "enabled": false,
                      "source": "404-promoted"
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(reportsPath, "ignored-404-candidates.json"),
                """
                {
                  "ignored404": [
                    {
                      "path": "/wp-login.php",
                      "reason": "scanner noise"
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(root, "data", "links", "ignored-404.json"), "{ \"ignored404\": [] }");
            var summaryPath = Path.Combine(reportsPath, "apply-summary.json");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "links",
                new[] { "apply-review", "--config", configPath, "--all", "--ignored-404", Path.Combine(root, "data", "links", "ignored-404.json"), "--summary-path", summaryPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(summaryPath));
            Assert.Contains("\"sourcePath\": \"/docs/instal\"", File.ReadAllText(Path.Combine(root, "data", "links", "redirects.json")), StringComparison.Ordinal);
            Assert.Contains("\"path\": \"/wp-login.php\"", File.ReadAllText(Path.Combine(root, "data", "links", "ignored-404.json")), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string WriteSiteFixture(string root, bool duplicateRedirects)
    {
        Directory.CreateDirectory(Path.Combine(root, "data", "links"));
        File.WriteAllText(Path.Combine(root, "site.json"),
            """
            {
              "name": "Links CLI Test",
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

        var redirectsJson = duplicateRedirects
            ? """
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
              """
            : """
              [
                {
                  "id": "canonical",
                  "sourcePath": "/old/",
                  "targetUrl": "/new/",
                  "status": 301
                }
              ]
              """;
        File.WriteAllText(Path.Combine(root, "data", "links", "redirects.json"), redirectsJson);
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

        return Path.Combine(root, "site.json");
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
