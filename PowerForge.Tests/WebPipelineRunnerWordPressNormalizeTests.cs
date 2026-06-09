using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerWordPressNormalizeTests
{
    [Fact]
    public void RunPipeline_WordPressNormalize_NormalizesImportedContentAndWritesSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-normalize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogDir = Path.Combine(root, "content", "blog", "en");
            Directory.CreateDirectory(blogDir);
            var inputPath = Path.Combine(blogDir, "legacy-name.md");
            File.WriteAllText(inputPath,
                """
                ---
                title: "Legacy"
                slug: "new-name"
                description: "<div>[vc_row]This is <b>old</b> description</div>"
                translation_key: "blog:new-name"
                meta.wp_id: "100"
                meta.generated_by: import-wordpress-snapshot
                ---
                <h2>Heading</h2>
                <p>Hello <strong>World</strong></p>
                <p><img src="https://evotec.xyz/wp-content/uploads/demo.png" alt="Demo"></p>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "wordpress-normalize",
                      "siteRoot": ".",
                      "summaryPath": "./Build/normalize-wordpress-last-run.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("wordpress-normalize ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var renamedPath = Path.Combine(blogDir, "new-name.md");
            Assert.True(File.Exists(renamedPath));
            Assert.False(File.Exists(inputPath));

            var normalized = File.ReadAllText(renamedPath);
            Assert.Contains("## Heading", normalized, StringComparison.Ordinal);
            Assert.Contains("Hello **World**", normalized, StringComparison.Ordinal);
            Assert.Contains("/wp-content/uploads/demo.png", normalized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[vc_row]", normalized, StringComparison.OrdinalIgnoreCase);

            var summaryPath = Path.Combine(root, "Build", "normalize-wordpress-last-run.json");
            Assert.True(File.Exists(summaryPath));
            using var doc = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.True(doc.RootElement.TryGetProperty("processedFiles", out var processed));
            Assert.True(processed.GetInt32() >= 1);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_WordPressNormalize_WhatIfSkipsWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-normalize-whatif-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pageDir = Path.Combine(root, "content", "pages", "en");
            Directory.CreateDirectory(pageDir);
            var inputPath = Path.Combine(pageDir, "sample.md");
            var original =
                """
                ---
                title: "Sample"
                slug: "sample"
                meta.generated_by: import-wordpress-snapshot
                ---
                <p>Body</p>
                """;
            File.WriteAllText(inputPath, original);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "wordpress-normalize",
                      "siteRoot": ".",
                      "whatIf": true,
                      "summaryPath": "./Build/normalize-wordpress-last-run.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Equal(original, File.ReadAllText(inputPath));
            Assert.False(File.Exists(Path.Combine(root, "Build", "normalize-wordpress-last-run.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_WordPressNormalize_AppliesTranslationKeyMap()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-normalize-keymap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogDir = Path.Combine(root, "content", "blog", "pl");
            Directory.CreateDirectory(blogDir);
            var inputPath = Path.Combine(blogDir, "mapped-post.md");
            File.WriteAllText(inputPath,
                """
                ---
                title: "Mapped"
                slug: "mapped-post"
                language: "pl"
                translation_key: "wp-post-5674"
                meta.wp_id: "5674"
                meta.generated_by: import-wordpress-snapshot
                ---
                <p>Body</p>
                """);

            var mapDir = Path.Combine(root, "data", "wordpress");
            Directory.CreateDirectory(mapDir);
            var mapPath = Path.Combine(mapDir, "translation-key-map.json");
            File.WriteAllText(mapPath,
                """
                {
                  "wp-post-5674": "wp-post-5669"
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "wordpress-normalize",
                      "siteRoot": ".",
                      "translationKeyMapPath": "./data/wordpress/translation-key-map.json",
                      "summaryPath": "./Build/normalize-wordpress-last-run.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            var normalized = File.ReadAllText(inputPath);
            Assert.Contains("translation_key: \"wp-post-5669\"", normalized, StringComparison.Ordinal);
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
