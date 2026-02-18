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
            var configPath = WriteSiteFixture(root);

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
            Assert.Equal(2, json.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("powerforge.site-nav", json.GetProperty("format").GetString());
            Assert.True(json.GetProperty("generated").GetBoolean());
            Assert.True(json.TryGetProperty("surfaceAliases", out var aliases));
            Assert.Equal("apidocs", aliases.GetProperty("api").GetString());
            Assert.True(json.TryGetProperty("surfaces", out _));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_NavExport_NormalizesApiSurfaceAliasToApidocs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-nav-export-surface-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = WriteSiteFixture(root, includeApiAliasSurface: true);

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
            var surfaces = doc.RootElement.GetProperty("surfaces");
            Assert.True(surfaces.TryGetProperty("apidocs", out _));
            Assert.False(surfaces.TryGetProperty("api", out _));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_NavExport_RefusesToOverwriteUserManagedFile_ByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-nav-export-no-overwrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = WriteSiteFixture(root);
            var outPath = Path.Combine(root, "static", "data", "site-nav.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, """{"schemaVersion":1,"primary":[]}""");
            var original = File.ReadAllText(outPath);

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "nav-export",
                new[] { "--config", configPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(1, exitCode);
            Assert.Equal(original, File.ReadAllText(outPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_NavExport_OverwritesGeneratedFile_ByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-nav-export-generated-overwrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = WriteSiteFixture(root);
            var outPath = Path.Combine(root, "static", "data", "site-nav.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, """{"generated":true}""");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "nav-export",
                new[] { "--config", configPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.True(doc.RootElement.GetProperty("generated").GetBoolean());
            Assert.True(doc.RootElement.TryGetProperty("menus", out _));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_NavExport_ForceOverwritesUserManagedFile_WithOverwrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-nav-export-force-overwrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = WriteSiteFixture(root);
            var outPath = Path.Combine(root, "static", "data", "site-nav.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, """{"schemaVersion":1,"primary":[]}""");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "nav-export",
                new[] { "--config", configPath, "--overwrite" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
            Assert.True(doc.RootElement.GetProperty("generated").GetBoolean());
            Assert.True(doc.RootElement.TryGetProperty("menus", out _));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_NavExport_RefusesToWriteOutsideSiteRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-nav-export-outside-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var outside = Path.Combine(Path.GetTempPath(), "pf-web-cli-nav-export-outside-" + Guid.NewGuid().ToString("N"), "site-nav.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        if (File.Exists(outside)) File.Delete(outside);

        try
        {
            var configPath = WriteSiteFixture(root);

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "nav-export",
                new[] { "--config", configPath, "--out", outside, "--overwrite" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(outside));
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(Path.GetDirectoryName(outside)!);
        }
    }

    private static string WriteSiteFixture(string root, bool includeApiAliasSurface = false)
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
        var configJson = includeApiAliasSurface
            ? """
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
                    { "Name": "main", "Path": "/" },
                    { "Name": "api", "Path": "/api/", "Layout": "apiDocs", "PrimaryMenu": "main" }
                  ]
                }
              }
              """
            : """
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
              """;
        File.WriteAllText(configPath, configJson);

        return configPath;
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
