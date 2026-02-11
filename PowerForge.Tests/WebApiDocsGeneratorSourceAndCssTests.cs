using System;
using System.IO;
using System.Linq;
using Xunit;
using PowerForge.Web;

public class WebApiDocsGeneratorSourceAndCssTests
{
    [Fact]
    public void GenerateDocsHtml_AppliesSourceUrlMappings_WithPathTokens()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-sourcemap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.True(File.Exists(assemblyPath), "PowerForge.Web assembly should exist for source link test.");
        Assert.True(File.Exists(xmlPath), "PowerForge.Web XML docs should exist for source link test.");

        var sourceRoot = ResolveGitRoot(assemblyPath) ?? Path.GetDirectoryName(assemblyPath) ?? root;
        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            AssemblyPath = assemblyPath,
            OutputPath = outputPath,
            SourceRootPath = sourceRoot,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };
        options.SourceUrlMappings.Add(new WebApiDocsSourceUrlMapping
        {
            PathPrefix = "PowerForge.Web",
            UrlPattern = "https://example.invalid/{root}/blob/main/{pathNoPrefix}#L{line}"
        });

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var htmlFiles = Directory.GetFiles(outputPath, "*.html", SearchOption.AllDirectories);
            Assert.True(htmlFiles.Length > 0, "Expected generated HTML pages.");

            var hasMappedSourceLink = htmlFiles.Any(path =>
                File.ReadAllText(path).Contains("https://example.invalid/PowerForge.Web/blob/main/", StringComparison.OrdinalIgnoreCase));

            Assert.True(hasMappedSourceLink, "Expected at least one source/edit link rendered using sourceUrlMappings tokens.");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateDocsHtml_AlwaysIncludesFallbackCssBaseline_WhenCustomCssIsConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-fallback-css-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var cssPath = Path.Combine(root, "css", "api.css");
        Directory.CreateDirectory(Path.GetDirectoryName(cssPath)!);
        File.WriteAllText(cssPath, ".api-layout { outline: 0; }");

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            CssHref = "/css/api.css"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);

            Assert.Contains("href=\"/css/api.css\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".type-chip{display:inline-flex", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".chip-icon{display:inline-flex", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string? ResolveGitRoot(string path)
    {
        var current = Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git")))
                return current;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }

        return null;
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
