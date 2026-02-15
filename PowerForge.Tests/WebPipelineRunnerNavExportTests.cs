using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerNavExportTests
{
    [Fact]
    public void RunPipeline_NavExport_WritesOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-nav-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "index.md"),
                """
                ---
                title: Home
                slug: /
                ---

                # Home
                """);

            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "Name": "Pipeline Nav Export Test",
                  "BaseUrl": "https://example.test",
                  "ContentRoot": "content",
                  "Collections": [
                    { "Name": "pages", "Input": "content/pages", "Output": "/" }
                  ],
                  "Navigation": {
                    "Menus": [
                      { "Name": "main", "Items": [ { "Title": "Home", "Url": "/" } ] }
                    ],
                    "Surfaces": [
                      { "Name": "main", "Path": "/" }
                    ]
                  }
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    { "task": "nav-export", "config": "./site.json" }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var outPath = Path.Combine(root, "static", "data", "site-nav.json");
            Assert.True(File.Exists(outPath));

            using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.True(doc.RootElement.GetProperty("generated").GetBoolean());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_NavExport_FailsWhenRefusingOverwrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-nav-export-no-overwrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "index.md"),
                """
                ---
                title: Home
                slug: /
                ---

                # Home
                """);

            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "Name": "Pipeline Nav Export Test",
                  "BaseUrl": "https://example.test",
                  "ContentRoot": "content",
                  "Collections": [
                    { "Name": "pages", "Input": "content/pages", "Output": "/" }
                  ],
                  "Navigation": {
                    "Menus": [
                      { "Name": "main", "Items": [ { "Title": "Home", "Url": "/" } ] }
                    ],
                    "Surfaces": [
                      { "Name": "main", "Path": "/" }
                    ]
                  }
                }
                """);

            var outPath = Path.Combine(root, "static", "data", "site-nav.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, """{"schemaVersion":1,"primary":[]}""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    { "task": "nav-export", "config": "./site.json" }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("Refusing to overwrite", result.Steps[0].Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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
