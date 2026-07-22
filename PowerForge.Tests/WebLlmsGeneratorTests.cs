using System;
using System.IO;
using PowerForge.Web;
using Xunit;

public class WebLlmsGeneratorTests
{
    [Fact]
    public void Generate_WritesRecommendedLlmsTxtMarkdownLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-markdown-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product",
                Overview = "Example Product publishes API docs and implementation guidance.",
                ApiBase = "/projects/example/api/"
            });

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("# Example Product", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("## Machine-friendly API data", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("- [API index](/projects/example/api/index.json):", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("- [API search](/projects/example/api/search.json):", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("- [API type template](/projects/example/api/types/{slug}.json):", llmsTxt, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_AggregatesMultipleApiCatalogsWithTheirPublishedRoutes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-multi-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var wordIndex = WriteApiIndex(root, "word", "OfficeIMO.Word", 274);
            var excelIndex = WriteApiIndex(root, "excel", "OfficeIMO.Excel", 63);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "OfficeIMO",
                PackageId = "OfficeIMO.Word",
                ApiIndexPaths = new[] { wordIndex, excelIndex }
            });

            Assert.Equal(2, result.ApiCatalogCount);
            Assert.Equal(337, result.ApiTypeCount);

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("- API catalogs: 2", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("[OfficeIMO.Word API index](/api/word/index.json)", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("[OfficeIMO.Excel API search](/api/excel/search.json)", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("[API index](/api/index.json)", llmsTxt, StringComparison.Ordinal);

            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("\"apiCatalogs\"", llmsJson, StringComparison.Ordinal);
            Assert.Contains("\"index\": \"/api/word/index.json\"", llmsJson, StringComparison.Ordinal);
            Assert.Contains("\"type\": \"/api/excel/types/{slug}.json\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"api\":", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_UsesProjectDescription_WhenOverviewIsNotProvided()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-project-description-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "Example.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>ExampleProduct</AssemblyName>
                    <PackageId>Example.Product</PackageId>
                    <Version>1.2.3</Version>
                    <Description>ExampleProduct helps teams publish internal documentation and automation portals.</Description>
                  </PropertyGroup>
                </Project>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ProjectFile = projectPath
            });

            var llmsFull = File.ReadAllText(result.LlmsFullPath);
            Assert.Contains("ExampleProduct helps teams publish internal documentation and automation portals.", llmsFull, StringComparison.Ordinal);
            Assert.DoesNotContain("QR codes", llmsFull, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_UsesHomepageMetaDescription_WhenProjectDescriptionAndOverviewAreMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-homepage-description-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head>
                  <meta name="description" content="Test product site for Active Directory security, posture, and reporting workflows." />
                  <title>Example Product</title>
                </head>
                <body>
                  <h1>Example Product</h1>
                </body>
                </html>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product"
            });

            var llmsFull = File.ReadAllText(result.LlmsFullPath);
            Assert.Contains("Test product site for Active Directory security, posture, and reporting workflows.", llmsFull, StringComparison.Ordinal);
            Assert.DoesNotContain("QR codes", llmsFull, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_UsesNeutralFallback_WhenNoOverviewSourcesExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-neutral-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product"
            });

            var llmsFull = File.ReadAllText(result.LlmsFullPath);
            Assert.Contains("Example Product documentation site and API reference.", llmsFull, StringComparison.Ordinal);
            Assert.DoesNotContain("QR codes", llmsFull, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("barcodes", llmsFull, StringComparison.OrdinalIgnoreCase);
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
            // Ignore cleanup failures in tests.
        }
    }

    private static string WriteApiIndex(string root, string slug, string assemblyName, int typeCount)
    {
        var directory = Path.Combine(root, "api", slug);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "index.json");
        File.WriteAllText(path,
            $$"""
            {
              "title": "{{assemblyName}} API Reference",
              "assembly": {
                "assemblyName": "{{assemblyName}}",
                "assemblyVersion": "1.0.0.0"
              },
              "typeCount": {{typeCount}},
              "types": []
            }
            """);
        return path;
    }
}
