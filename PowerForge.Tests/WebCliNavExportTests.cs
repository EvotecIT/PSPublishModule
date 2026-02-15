using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebCliNavExportTests
{
    [Fact]
    public void HandleSubCommand_NavExport_WritesDefaultOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-nav-export-" + Guid.NewGuid().ToString("N"));
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

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath,
                """
                {
                  "Name": "Nav Export Test",
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

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "nav-export",
                new[] { "--config", configPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);

            var outPath = Path.Combine(root, "static", "data", "site-nav.json");
            Assert.True(File.Exists(outPath));

            using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
            var json = doc.RootElement;
            Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
            Assert.True(json.GetProperty("generated").GetBoolean());
            Assert.True(json.TryGetProperty("surfaces", out _));
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

