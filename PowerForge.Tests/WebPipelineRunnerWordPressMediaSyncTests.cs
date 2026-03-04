using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerWordPressMediaSyncTests
{
    [Fact]
    public void RunPipeline_WordPressMediaSync_RewritesAndAddsHintsWhenLocalMediaExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-media-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentDir = Path.Combine(root, "content", "blog", "en");
            Directory.CreateDirectory(contentDir);
            var mediaDir = Path.Combine(root, "static", "wp-content", "uploads", "2026", "03");
            Directory.CreateDirectory(mediaDir);
            var mediaPath = Path.Combine(mediaDir, "demo.png");
            File.WriteAllBytes(mediaPath, new byte[] { 1, 2, 3, 4 });

            var pagePath = Path.Combine(contentDir, "sample.md");
            File.WriteAllText(pagePath,
                """
                ---
                title: "Sample"
                slug: "sample"
                meta.wp_link: "https://evotec.xyz/sample/"
                meta.generated_by: import-wordpress-snapshot
                ---
                Check https://evotec.xyz/wp-content/uploads/2026/03/demo.png
                <img src="https://evotec.xyz/wp-content/uploads/2026/03/demo.png">
                <iframe src="https://www.youtube.com/embed/abc123"></iframe>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "wordpress-media-sync",
                      "siteRoot": ".",
                      "noDownload": true,
                      "summaryPath": "./Build/sync-wordpress-media-last-run.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("wordpress-media-sync ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var updated = File.ReadAllText(pagePath);
            Assert.Contains("/wp-content/uploads/2026/03/demo.png", updated, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("loading=\"lazy\"", updated, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("decoding=\"async\"", updated, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("youtube-nocookie.com/embed/abc123", updated, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("referrerpolicy=\"strict-origin-when-cross-origin\"", updated, StringComparison.OrdinalIgnoreCase);

            var summaryPath = Path.Combine(root, "Build", "sync-wordpress-media-last-run.json");
            Assert.True(File.Exists(summaryPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_WordPressMediaSync_WhatIfDoesNotWriteFilesOrSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-media-sync-whatif-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentDir = Path.Combine(root, "content", "pages", "en");
            Directory.CreateDirectory(contentDir);

            var pagePath = Path.Combine(contentDir, "sample.md");
            var original =
                """
                ---
                title: "Sample"
                slug: "sample"
                meta.generated_by: import-wordpress-snapshot
                ---
                Check https://evotec.xyz/wp-content/uploads/2026/03/missing.png
                """;
            File.WriteAllText(pagePath, original);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "wordpress-media-sync",
                      "siteRoot": ".",
                      "whatIf": true,
                      "summaryPath": "./Build/sync-wordpress-media-last-run.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Equal(original, File.ReadAllText(pagePath));
            Assert.False(File.Exists(Path.Combine(root, "Build", "sync-wordpress-media-last-run.json")));
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
