using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerMarkdownFixTests
{
    [Fact]
    public void RunPipeline_MarkdownFix_WritesReportAndSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-markdown-fix-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "page.md"),
                """
                # Demo

                <iframe
                  src="https://example.test/embed/demo"
                  loading="lazy"
                  title="Demo"></iframe>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "markdown-fix",
                      "root": "./content",
                      "reportPath": "./_reports/markdown-fix.json",
                      "summaryPath": "./_reports/markdown-fix.md"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);

            var reportPath = Path.Combine(root, "_reports", "markdown-fix.json");
            var summaryPath = Path.Combine(root, "_reports", "markdown-fix.md");
            Assert.True(File.Exists(reportPath), "Expected markdown-fix JSON report.");
            Assert.True(File.Exists(summaryPath), "Expected markdown-fix markdown summary.");

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var reportRoot = reportDoc.RootElement;
            Assert.Equal(1, reportRoot.GetProperty("ChangedFileCount").GetInt32());
            Assert.True(reportRoot.GetProperty("MediaTagReplacementCount").GetInt32() >= 1);

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
    public void RunPipeline_MarkdownFix_FailsOnChanges_WhenConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-markdown-fix-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "page.md"),
                """
                # Demo

                <img src="/assets/screenshots/example.png"
                     alt="Example screenshot"
                     loading="lazy"
                     decoding="async" />
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "markdown-fix",
                      "root": "./content",
                      "failOnChanges": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("failOnChanges", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
