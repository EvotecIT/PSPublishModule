using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;
using Xunit;

namespace PowerForge.Tests;

public class WebApiDocsGeneratorSuiteTests
{
    [Fact]
    public void GenerateDocsHtml_AndJson_RenderApiSuiteSwitcherAndMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-suite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "docs.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>SuiteTests</name></assembly>
                  <members>
                    <member name="T:MyNamespace.Alpha">
                      <summary>Alpha docs.</summary>
                    </member>
                    <member name="T:MyNamespace.Beta">
                      <summary>Beta docs.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "_site", "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                BaseUrl = "/projects/testimox/api",
                Format = "both",
                Template = "docs",
                ApiSuiteTitle = "TestimoX API Suite",
                ApiSuiteCurrentId = "testimox",
                ApiSuiteHomeUrl = "/projects/",
                ApiSuiteHomeLabel = "All project APIs",
                ApiSuiteSearchUrl = "../../api-suite-search.json",
                ApiSuiteEntries =
                {
                    new WebApiDocsSuiteEntry
                    {
                        Id = "testimox",
                        Label = "TestimoX",
                        Href = "/projects/testimox/api/",
                        Summary = "Core validation API.",
                        Order = 1
                    },
                    new WebApiDocsSuiteEntry
                    {
                        Id = "adplayground",
                        Label = "ADPlayground",
                        Href = "/projects/adplayground/api/",
                        Summary = "Directory playground API.",
                        Order = 2
                    }
                }
            });

            Assert.True(result.TypeCount >= 2);

            var indexHtml = File.ReadAllText(Path.Combine(outputPath, "index.html"));
            Assert.Contains("api-suite-switcher", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("TestimoX API Suite", indexHtml, StringComparison.Ordinal);
            Assert.Contains("All project APIs", indexHtml, StringComparison.Ordinal);
            Assert.Contains("api-suite-card active", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("api-suite-search", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("../../api-suite-search.json", indexHtml, StringComparison.Ordinal);
            Assert.Contains("/projects/adplayground/api/", indexHtml, StringComparison.Ordinal);

            using var indexJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "index.json")));
            var suite = indexJson.RootElement.GetProperty("suite");
            Assert.Equal("TestimoX API Suite", suite.GetProperty("title").GetString());
            Assert.Equal("testimox", suite.GetProperty("currentId").GetString());
            Assert.Equal("../../api-suite-search.json", suite.GetProperty("searchUrl").GetString());
            Assert.Equal(2, suite.GetProperty("entries").GetArrayLength());

            var slug = indexJson.RootElement
                .GetProperty("types")
                .EnumerateArray()
                .Single(type => string.Equals(type.GetProperty("name").GetString(), "Alpha", StringComparison.Ordinal))
                .GetProperty("slug")
                .GetString();
            Assert.False(string.IsNullOrWhiteSpace(slug));

            var typeHtml = File.ReadAllText(Path.Combine(outputPath, slug!, "index.html"));
            Assert.Contains("api-suite-switcher", typeHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("api-suite-item active", typeHtml, StringComparison.OrdinalIgnoreCase);

            using var typeJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", $"{slug}.json")));
            Assert.Equal("TestimoX API Suite", typeJson.RootElement.GetProperty("suite").GetProperty("title").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateSuitePortal_WritesStandaloneLandingPageAndMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-suite-portal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outputPath = Path.Combine(root, "_site", "projects", "api-suite");
            var result = WebApiDocsGenerator.GenerateSuitePortal(new WebApiDocsOptions
            {
                OutputPath = outputPath,
                Title = "Project APIs",
                BaseUrl = "/projects/api-suite",
                CssHref = "/css/app.css,/css/api.css",
                ApiSuiteTitle = "Project APIs",
                ApiSuiteSearchUrl = "../api-suite-search.json",
                ApiSuiteCoverageUrl = "../api-suite-coverage.json",
                ApiSuiteXrefMapUrl = "../api-suite-xrefmap.json",
                ApiSuiteRelatedContentUrl = "../api-suite-related-content.json",
                ApiSuiteNarrativeUrl = "../api-suite-narrative.json",
                ApiSuiteEntries =
                {
                    new WebApiDocsSuiteEntry
                    {
                        Id = "testimox",
                        Label = "TestimoX",
                        Href = "/projects/testimox/api/",
                        Summary = "Core validation API.",
                        Order = 1
                    },
                    new WebApiDocsSuiteEntry
                    {
                        Id = "adplayground",
                        Label = "ADPlayground",
                        Href = "/projects/adplayground/api/",
                        Summary = "Directory playground API.",
                        Order = 2
                    }
                }
            });

            Assert.Equal(2, result.EntryCount);
            Assert.True(File.Exists(result.IndexPath));
            Assert.True(File.Exists(result.JsonPath));

            var html = File.ReadAllText(result.IndexPath);
            Assert.Contains("Project APIs", html, StringComparison.Ordinal);
            Assert.Contains("api-suite-search", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("api-suite-search-filter", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("../api-suite-search.json", html, StringComparison.Ordinal);
            Assert.Contains("api-suite-coverage-summary", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("../api-suite-coverage.json", html, StringComparison.Ordinal);
            Assert.Contains("api-suite-narrative", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("../api-suite-narrative.json", html, StringComparison.Ordinal);
            Assert.Contains("Start Here", html, StringComparison.Ordinal);
            Assert.Contains("api-suite-related-content", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("../api-suite-related-content.json", html, StringComparison.Ordinal);
            Assert.Contains("Guides & Samples", html, StringComparison.Ordinal);
            Assert.Contains("Suite Assets", html, StringComparison.Ordinal);
            Assert.Contains("/projects/testimox/api/", html, StringComparison.Ordinal);

            using var json = JsonDocument.Parse(File.ReadAllText(result.JsonPath));
            Assert.Equal("api-suite-portal", json.RootElement.GetProperty("kind").GetString());
            var suite = json.RootElement.GetProperty("suite");
            Assert.Equal("Project APIs", suite.GetProperty("title").GetString());
            Assert.Equal("../api-suite-coverage.json", suite.GetProperty("coverageUrl").GetString());
            Assert.Equal("../api-suite-related-content.json", suite.GetProperty("relatedContentUrl").GetString());
            Assert.Equal("../api-suite-narrative.json", suite.GetProperty("narrativeUrl").GetString());
            Assert.Equal(2, suite.GetProperty("entries").GetArrayLength());
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
