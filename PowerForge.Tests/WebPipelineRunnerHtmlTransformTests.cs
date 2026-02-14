using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerHtmlTransformTests
{
    [Fact]
    public void RunPipeline_HtmlTransform_StdoutMode_TransformsMatchingFilesAndWritesReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-html-transform-stdout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(siteRoot);
            Directory.CreateDirectory(Path.Combine(siteRoot, "docs"));
            Directory.CreateDirectory(Path.Combine(siteRoot, "docs", "drafts"));

            var indexPath = Path.Combine(siteRoot, "index.html");
            var docsPath = Path.Combine(siteRoot, "docs", "intro.html");
            var draftPath = Path.Combine(siteRoot, "docs", "drafts", "skip.html");
            File.WriteAllText(indexPath, "<html><body>home</body></html>");
            File.WriteAllText(docsPath, "<html><body>docs</body></html>");
            File.WriteAllText(draftPath, "<html><body>draft</body></html>");

            var reportPath = Path.Combine(root, "_reports", "html-transform.json");
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "html-transform",
                      "siteRoot": "./_site",
                      "include": ["index.html", "docs/**"],
                      "exclude": ["docs/drafts/**"],
                      "command": "dotnet",
                      "args": "--version",
                      "writeMode": "stdout",
                      "reportPath": "./_reports/html-transform.json"
                    }
                  ]
                }
                """);

            var beforeDraft = File.ReadAllText(draftPath);
            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("processed 2", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("changed 2", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var afterIndex = File.ReadAllText(indexPath);
            var afterDocs = File.ReadAllText(docsPath);
            var afterDraft = File.ReadAllText(draftPath);
            Assert.NotEqual("<html><body>home</body></html>", afterIndex);
            Assert.NotEqual("<html><body>docs</body></html>", afterDocs);
            Assert.Equal(beforeDraft, afterDraft);

            Assert.True(File.Exists(reportPath));
            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var reportRoot = reportDoc.RootElement;
            Assert.Equal("stdout", reportRoot.GetProperty("WriteMode").GetString());
            Assert.Equal(2, reportRoot.GetProperty("ProcessedCount").GetInt32());
            Assert.Equal(2, reportRoot.GetProperty("ChangedCount").GetInt32());
            Assert.Equal(0, reportRoot.GetProperty("FailedCount").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_HtmlTransform_InplaceMode_CanSucceedWithoutChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-html-transform-inplace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(siteRoot);
            var indexPath = Path.Combine(siteRoot, "index.html");
            File.WriteAllText(indexPath, "<html><body>home</body></html>");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "html-transform",
                      "siteRoot": "./_site",
                      "command": "dotnet",
                      "args": "--version",
                      "writeMode": "inplace"
                    }
                  ]
                }
                """);

            var before = File.ReadAllText(indexPath);
            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            var after = File.ReadAllText(indexPath);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("processed 1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("changed 0", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, after);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_HtmlTransform_AllowFailure_ContinuesAndReportsFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-html-transform-allow-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(siteRoot);
            File.WriteAllText(Path.Combine(siteRoot, "index.html"), "<html><body>home</body></html>");

            var reportPath = Path.Combine(root, "_reports", "html-transform.json");
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "html-transform",
                      "siteRoot": "./_site",
                      "command": "dotnet",
                      "args": "not-a-real-command-xyz",
                      "writeMode": "stdout",
                      "allowFailure": true,
                      "reportPath": "./_reports/html-transform.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("allowed failures", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var reportRoot = reportDoc.RootElement;
            Assert.Equal(1, reportRoot.GetProperty("ProcessedCount").GetInt32());
            Assert.Equal(0, reportRoot.GetProperty("ChangedCount").GetInt32());
            Assert.Equal(1, reportRoot.GetProperty("FailedCount").GetInt32());
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
