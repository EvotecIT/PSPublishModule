using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;
using Xunit;

namespace PowerForge.Tests;

public class WebApiDocsGeneratorRelatedContentTests
{
    [Fact]
    public void GenerateDocsHtml_AndJson_RenderCuratedRelatedContent_ForTypeAndMembers()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-related-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var widgetType = typeof(ApiRelatedContentWidget).FullName!;
            var xmlPath = Path.Combine(root, "docs.xml");
            File.WriteAllText(xmlPath,
                $"""
                <doc>
                  <assembly><name>RelatedContentTests</name></assembly>
                  <members>
                    <member name="T:{widgetType}">
                      <summary>Widget docs.</summary>
                    </member>
                    <member name="M:{widgetType}.Configure(System.String)">
                      <summary>Configures the widget.</summary>
                      <param name="mode">Mode value.</param>
                    </member>
                    <member name="P:{widgetType}.Label">
                      <summary>Widget label.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var manifestPath = Path.Combine(root, "related-content.json");
            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    new
                    {
                        entries = new object[]
                        {
                            new
                            {
                                title = "Widget overview guide",
                                url = "/docs/widgets/overview/",
                                summary = $"Start with [[cref:T:{widgetType}]] before customising it.",
                                kind = "guide",
                                targets = new[] { $"T:{widgetType}" }
                            },
                            new
                            {
                                title = "Configure sample",
                                url = "/docs/widgets/configure/",
                                summary = "Shows a practical Configure flow.",
                                kind = "sample",
                                targets = new[] { $"M:{widgetType}.Configure(System.String)" }
                            },
                            new
                            {
                                title = "Label reference",
                                url = "/docs/widgets/label/",
                                summary = "Explains when to populate the label.",
                                kind = "reference",
                                targets = new[] { $"P:{widgetType}.Label" }
                            }
                        }
                    }));

            var outputPath = Path.Combine(root, "_site", "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                BaseUrl = "/api",
                Format = "both",
                Template = "docs",
                RelatedContentManifestPaths = { manifestPath }
            });

            Assert.True(result.TypeCount > 0);

            var indexPath = Path.Combine(outputPath, "index.json");
            using var indexDocument = JsonDocument.Parse(File.ReadAllText(indexPath));
            var slug = indexDocument.RootElement
                .GetProperty("types")
                .EnumerateArray()
                .Single(type => string.Equals(type.GetProperty("name").GetString(), nameof(ApiRelatedContentWidget), StringComparison.Ordinal))
                .GetProperty("slug")
                .GetString();

            Assert.False(string.IsNullOrWhiteSpace(slug));

            var htmlPath = Path.Combine(outputPath, slug!, "index.html");
            Assert.True(File.Exists(htmlPath));
            var html = File.ReadAllText(htmlPath);

            Assert.Contains("id=\"guides-and-samples\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Widget overview guide", html, StringComparison.Ordinal);
            Assert.Contains("/docs/widgets/configure/", html, StringComparison.Ordinal);
            Assert.Contains("/docs/widgets/label/", html, StringComparison.Ordinal);
            Assert.Contains("related-content-kind sample", html, StringComparison.OrdinalIgnoreCase);

            var jsonPath = Path.Combine(outputPath, "types", $"{slug}.json");
            Assert.True(File.Exists(jsonPath));
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var rootElement = document.RootElement;

            var relatedContent = rootElement.GetProperty("relatedContent");
            Assert.Equal("Widget overview guide", relatedContent.GetProperty("entries")[0].GetProperty("title").GetString());
            Assert.Equal(2, relatedContent.GetProperty("members").GetArrayLength());

            var configureMember = rootElement
                .GetProperty("methods")
                .EnumerateArray()
                .Single(member => string.Equals(member.GetProperty("name").GetString(), "Configure", StringComparison.Ordinal));
            Assert.Equal("Configure sample", configureMember.GetProperty("relatedContent")[0].GetProperty("title").GetString());

            var labelProperty = rootElement
                .GetProperty("properties")
                .EnumerateArray()
                .Single(member => string.Equals(member.GetProperty("name").GetString(), "Label", StringComparison.Ordinal));
            Assert.Equal("reference", labelProperty.GetProperty("relatedContent")[0].GetProperty("kind").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_CoverageAndWarnings_TrackQuickStartTypesMissingCuratedRelatedContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-related-coverage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var alphaType = typeof(ApiRelatedAlpha).FullName!;
            var betaType = typeof(ApiRelatedBeta).FullName!;
            var xmlPath = Path.Combine(root, "docs.xml");
            File.WriteAllText(xmlPath,
                $"""
                <doc>
                  <assembly><name>RelatedContentCoverageTests</name></assembly>
                  <members>
                    <member name="T:{alphaType}">
                      <summary>Alpha docs.</summary>
                    </member>
                    <member name="T:{betaType}">
                      <summary>Beta docs.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var manifestPath = Path.Combine(root, "related-content.json");
            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    new
                    {
                        entries = new object[]
                        {
                            new
                            {
                                title = "Alpha walkthrough",
                                url = "/docs/alpha/",
                                kind = "guide",
                                targets = new[] { $"T:{alphaType}" }
                            }
                        }
                    }));

            var outputPath = Path.Combine(root, "_site", "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                BaseUrl = "/api",
                Format = "json",
                CoverageReportPath = "reports/api-coverage.json",
                RelatedContentManifestPaths = { manifestPath },
                QuickStartTypeNames = { nameof(ApiRelatedAlpha), nameof(ApiRelatedBeta) }
            });

            Assert.Contains(result.Warnings, warning =>
                warning.Contains("[PFWEB.APIDOCS.RELATED]", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("quickStart type", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains(betaType, StringComparison.OrdinalIgnoreCase));

            using var coverage = JsonDocument.Parse(File.ReadAllText(result.CoveragePath!));
            var types = coverage.RootElement.GetProperty("types");
            Assert.Equal(50d, types.GetProperty("relatedContent").GetProperty("percent").GetDouble());
            Assert.Equal(50d, types.GetProperty("quickStartRelatedContent").GetProperty("percent").GetDouble());
            Assert.Equal(1, types.GetProperty("quickStartMissingRelatedContent").GetProperty("count").GetInt32());
            Assert.Contains(
                types.GetProperty("quickStartMissingRelatedContent").GetProperty("types").EnumerateArray().Select(static item => item.GetString()),
                value => string.Equals(value, betaType, StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_CoverageCountsAllMissingQuickStartTypes_EvenWhenPreviewIsTruncated()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-related-coverage-many-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const int typeCount = 105;
            var xmlPath = Path.Combine(root, "docs.xml");
            var members = string.Join(Environment.NewLine,
                Enumerable.Range(1, typeCount)
                    .Select(index =>
                        $$"""
                            <member name="T:ManyQuickStartType{{index}}">
                              <summary>Type {{index}} docs.</summary>
                            </member>
                        """));
            File.WriteAllText(xmlPath,
                $$"""
                <doc>
                  <assembly><name>RelatedContentCoverageManyTests</name></assembly>
                  <members>
                {{members}}
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "_site", "api");
            var options = new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                BaseUrl = "/api",
                Format = "json",
                CoverageReportPath = "reports/api-coverage.json"
            };
            options.QuickStartTypeNames.AddRange(Enumerable.Range(1, typeCount).Select(static index => $"ManyQuickStartType{index}"));
            var result = WebApiDocsGenerator.Generate(options);

            using var coverage = JsonDocument.Parse(File.ReadAllText(result.CoveragePath!));
            var missing = coverage.RootElement.GetProperty("types").GetProperty("quickStartMissingRelatedContent");
            Assert.Equal(typeCount, missing.GetProperty("count").GetInt32());
            Assert.Equal(100, missing.GetProperty("types").GetArrayLength());
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

public sealed class ApiRelatedContentWidget
{
    public string Label => "Widget";

    public void Configure(string mode)
    {
        _ = mode;
    }
}

public sealed class ApiRelatedAlpha
{
}

public sealed class ApiRelatedBeta
{
}
