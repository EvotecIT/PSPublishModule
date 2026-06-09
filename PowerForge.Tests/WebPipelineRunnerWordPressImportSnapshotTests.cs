using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerWordPressImportSnapshotTests
{
    [Fact]
    public void RunPipeline_WordPressImportSnapshot_GeneratesMarkdownAndRedirectCsv()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var rawDefault = Path.Combine(root, "snapshot", "raw", "default");
            Directory.CreateDirectory(rawDefault);

            File.WriteAllText(Path.Combine(rawDefault, "categories.json"),
                """
                [
                  { "id": 1, "name": "Category One" }
                ]
                """);
            File.WriteAllText(Path.Combine(rawDefault, "tags.json"),
                """
                [
                  { "id": 5, "name": "Tag One" }
                ]
                """);

            File.WriteAllText(Path.Combine(rawDefault, "posts.json"),
                """
                [
                  {
                    "id": 101,
                    "slug": "hello-world",
                    "title": { "raw": "Hello World" },
                    "excerpt": { "raw": "<p>Post description.</p>" },
                    "date": "2025-06-15T10:00:00+00:00",
                    "content": { "raw": "<p>Post body</p>" },
                    "categories": [1],
                    "tags": [5],
                    "link": "https://evotec.xyz/legacy-hello/",
                    "status": "publish",
                    "featured_media": 77
                  }
                ]
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "wordpress-import-snapshot",
                      "snapshotPath": "./snapshot",
                      "siteRoot": ".",
                      "collections": [ "posts" ],
                      "summaryPath": "./Build/import-wordpress-last-run.json",
                      "redirectCsvPath": "./data/redirects/legacy-wordpress-generated.csv"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("wordpress-import-snapshot ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var markdownPath = Path.Combine(root, "content", "blog", "en", "hello-world.md");
            Assert.True(File.Exists(markdownPath));
            var markdown = File.ReadAllText(markdownPath);
            Assert.Contains("title: \"Hello World\"", markdown, StringComparison.Ordinal);
            Assert.Contains("translation_key: \"wp-post-101\"", markdown, StringComparison.Ordinal);
            Assert.Contains("categories:", markdown, StringComparison.Ordinal);
            Assert.Contains("tags:", markdown, StringComparison.Ordinal);
            Assert.Contains("meta.generated_by: import-wordpress-snapshot", markdown, StringComparison.Ordinal);
            Assert.Contains("meta.raw_html: true", markdown, StringComparison.Ordinal);

            var redirectCsvPath = Path.Combine(root, "data", "redirects", "legacy-wordpress-generated.csv");
            Assert.True(File.Exists(redirectCsvPath));
            var csv = File.ReadAllText(redirectCsvPath);
            Assert.Contains("/?p=101", csv, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/legacy-hello/", csv, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/blog/hello-world/", csv, StringComparison.OrdinalIgnoreCase);

            var summaryPath = Path.Combine(root, "Build", "import-wordpress-last-run.json");
            Assert.True(File.Exists(summaryPath));
            using var summaryDoc = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.Equal(1, summaryDoc.RootElement.GetProperty("generated_files").GetInt32());
            Assert.Equal(0, summaryDoc.RootElement.GetProperty("skipped_files").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_WordPressImportSnapshot_ProtectsNonGeneratedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-import-protect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var rawDefault = Path.Combine(root, "snapshot", "raw", "default");
            Directory.CreateDirectory(rawDefault);
            File.WriteAllText(Path.Combine(rawDefault, "pages.json"),
                """
                [
                  {
                    "id": 200,
                    "slug": "custom-page",
                    "title": { "raw": "Imported Page" },
                    "content": { "raw": "<p>Imported body</p>" },
                    "date": "2025-06-16T10:00:00+00:00",
                    "link": "https://evotec.xyz/custom-page/"
                  }
                ]
                """);

            var existingDir = Path.Combine(root, "content", "pages", "en");
            Directory.CreateDirectory(existingDir);
            var existingPath = Path.Combine(existingDir, "custom-page.md");
            var existingContent =
                """
                ---
                title: "Curated Page"
                ---
                Keep this file.
                """;
            File.WriteAllText(existingPath, existingContent);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "wordpress-import-snapshot",
                      "snapshotPath": "./snapshot",
                      "siteRoot": ".",
                      "collections": [ "pages" ],
                      "summaryPath": "./Build/import-wordpress-last-run.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Equal(existingContent, File.ReadAllText(existingPath));

            var summaryPath = Path.Combine(root, "Build", "import-wordpress-last-run.json");
            Assert.True(File.Exists(summaryPath));
            using var summaryDoc = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.Equal(0, summaryDoc.RootElement.GetProperty("generated_files").GetInt32());
            Assert.Equal(1, summaryDoc.RootElement.GetProperty("skipped_files").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_WordPressImportSnapshot_UsesTranslationsGroupAcrossLanguages()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-import-translations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var rawEn = Path.Combine(root, "snapshot", "raw", "en");
            var rawPl = Path.Combine(root, "snapshot", "raw", "pl");
            Directory.CreateDirectory(rawEn);
            Directory.CreateDirectory(rawPl);

            File.WriteAllText(Path.Combine(rawEn, "posts.json"),
                """
                [
                  {
                    "id": 5669,
                    "slug": "exchange-english",
                    "title": { "raw": "Exchange EN" },
                    "content": { "raw": "<p>Body EN</p>" },
                    "date": "2025-06-15T10:00:00+00:00",
                    "translations": { "en": 5669, "pl": 5674 }
                  }
                ]
                """);

            File.WriteAllText(Path.Combine(rawPl, "posts.json"),
                """
                [
                  {
                    "id": 5674,
                    "slug": "exchange-polish",
                    "title": { "raw": "Exchange PL" },
                    "content": { "raw": "<p>Body PL</p>" },
                    "date": "2025-06-15T10:00:00+00:00",
                    "translations": { "en": 5669, "pl": 5674 }
                  }
                ]
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "wordpress-import-snapshot",
                      "snapshotPath": "./snapshot",
                      "siteRoot": ".",
                      "collections": [ "posts" ],
                      "defaultLanguage": "en",
                      "summaryPath": "./Build/import-wordpress-last-run.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var enPath = Path.Combine(root, "content", "blog", "en", "exchange-english.md");
            var plPath = Path.Combine(root, "content", "blog", "pl", "exchange-polish.md");
            Assert.True(File.Exists(enPath));
            Assert.True(File.Exists(plPath));

            var enMarkdown = File.ReadAllText(enPath);
            var plMarkdown = File.ReadAllText(plPath);
            Assert.Contains("translation_key: \"wp-post-5669\"", enMarkdown, StringComparison.Ordinal);
            Assert.Contains("translation_key: \"wp-post-5669\"", plMarkdown, StringComparison.Ordinal);
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
