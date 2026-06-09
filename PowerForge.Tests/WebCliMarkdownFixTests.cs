using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebCliMarkdownFixTests
{
    private const int CliEnvelopeSchemaVersion = 1;

    [Fact]
    public void HandleSubCommand_MarkdownFix_WritesReportAndSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-markdown-fix-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentPath = Path.Combine(root, "content");
            Directory.CreateDirectory(contentPath);
            File.WriteAllText(Path.Combine(contentPath, "page.md"),
                """
                # Demo

                <iframe
                  src="https://example.test/embed/demo"
                  loading="lazy"
                  title="Demo"></iframe>
                """);

            var reportPath = Path.Combine(root, "_reports", "markdown-fix.json");
            var summaryPath = Path.Combine(root, "_reports", "markdown-fix.md");
            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "markdown-fix",
                new[]
                {
                    "--path", contentPath,
                    "--report-path", reportPath,
                    "--summary-path", summaryPath
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(reportPath));
            Assert.True(File.Exists(summaryPath));

            using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(1, doc.RootElement.GetProperty("ChangedFileCount").GetInt32());
            Assert.True(doc.RootElement.GetProperty("MediaTagReplacementCount").GetInt32() >= 1);

            var summary = File.ReadAllText(summaryPath);
            Assert.Contains("Markdown Fix Summary", summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("`iframe`", summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_MarkdownFix_FailOnChanges_RequiresApply()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-markdown-fix-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentPath = Path.Combine(root, "content");
            Directory.CreateDirectory(contentPath);
            File.WriteAllText(Path.Combine(contentPath, "page.md"),
                """
                # Demo

                <img src="/assets/screenshots/example.png"
                     alt="Example screenshot"
                     loading="lazy"
                     decoding="async" />
                """);

            var dryRunExit = WebCliCommandHandlers.HandleSubCommand(
                "markdown-fix",
                new[]
                {
                    "--path", contentPath,
                    "--fail-on-changes"
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(1, dryRunExit);

            var applyExit = WebCliCommandHandlers.HandleSubCommand(
                "markdown-fix",
                new[]
                {
                    "--path", contentPath,
                    "--apply",
                    "--fail-on-changes"
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: CliEnvelopeSchemaVersion);

            Assert.Equal(0, applyExit);
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
