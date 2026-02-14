using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;
using Xunit;

public class WebApiDocsGeneratorDisplayNameTests
{
    [Fact]
    public void GenerateDocsHtml_DisambiguatesDuplicateTypeNamesAcrossNamespaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-displayname-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:First.Tools.Widget">
                  <summary>First widget.</summary>
                </member>
                <member name="T:Second.Tools.Widget">
                  <summary>Second widget.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            Title = "Ambiguous API"
        };

        try
        {
            WebApiDocsGenerator.Generate(options);

            var firstPath = Path.Combine(outputPath, "first-tools-widget.html");
            var secondPath = Path.Combine(outputPath, "second-tools-widget.html");
            var indexPath = Path.Combine(outputPath, "index.html");

            Assert.True(File.Exists(firstPath), "Expected first type page to be generated.");
            Assert.True(File.Exists(secondPath), "Expected second type page to be generated.");
            Assert.True(File.Exists(indexPath), "Expected docs index page to be generated.");

            var firstHtml = File.ReadAllText(firstPath);
            var secondHtml = File.ReadAllText(secondPath);
            var indexHtml = File.ReadAllText(indexPath);

            Assert.Contains("Widget (First.Tools) - Ambiguous API", firstHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Widget (Second.Tools) - Ambiguous API", secondHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<h1>Widget (First.Tools)</h1>", firstHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<h1>Widget (Second.Tools)</h1>", secondHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Widget (First.Tools)", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Widget (Second.Tools)", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<title>Widget - Ambiguous API</title>", firstHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<title>Widget - Ambiguous API</title>", secondHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GenerateDocsHtml_UsesShortDisplayNames_WhenDisplayNameModeShort()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-displayname-short-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "test.xml");
        WriteDuplicateWidgetXml(xmlPath);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            Title = "Ambiguous API",
            DisplayNameMode = "short"
        };

        try
        {
            WebApiDocsGenerator.Generate(options);

            var firstHtml = File.ReadAllText(Path.Combine(outputPath, "first-tools-widget.html"));
            var secondHtml = File.ReadAllText(Path.Combine(outputPath, "second-tools-widget.html"));

            Assert.Contains("<h1>Widget</h1>", firstHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<h1>Widget</h1>", secondHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Widget - Ambiguous API", firstHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Widget - Ambiguous API", secondHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Widget (First.Tools)", firstHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Widget (Second.Tools)", secondHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GenerateDocsHtml_UsesFullNames_WhenDisplayNameModeFull()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-displayname-full-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "test.xml");
        WriteDuplicateWidgetXml(xmlPath);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            Title = "Ambiguous API",
            DisplayNameMode = "full"
        };

        try
        {
            WebApiDocsGenerator.Generate(options);

            var firstHtml = File.ReadAllText(Path.Combine(outputPath, "first-tools-widget.html"));
            var secondHtml = File.ReadAllText(Path.Combine(outputPath, "second-tools-widget.html"));

            Assert.Contains("<h1>First.Tools.Widget</h1>", firstHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<h1>Second.Tools.Widget</h1>", secondHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("First.Tools.Widget - Ambiguous API", firstHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Second.Tools.Widget - Ambiguous API", secondHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WarnsAndFallsBack_WhenDisplayNameModeIsUnknown()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-displayname-unknown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "test.xml");
        WriteDuplicateWidgetXml(xmlPath);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            Title = "Ambiguous API",
            DisplayNameMode = "mystery-mode"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, warning => warning.Contains("[PFWEB.APIDOCS.DISPLAY]", StringComparison.OrdinalIgnoreCase));

            var firstHtml = File.ReadAllText(Path.Combine(outputPath, "first-tools-widget.html"));
            Assert.Contains("Widget (First.Tools) - Ambiguous API", firstHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GenerateJson_IncludesDisplayNamesAndAliases_ForSearchAndTypeArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-displayname-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "test.xml");
        WriteDuplicateWidgetXml(xmlPath);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "json",
            BaseUrl = "/api",
            Title = "Ambiguous API"
        };

        try
        {
            WebApiDocsGenerator.Generate(options);

            var indexDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "index.json")));
            var typeEntries = indexDoc.RootElement.GetProperty("types").EnumerateArray().ToList();
            var firstIndexType = typeEntries.First(type =>
                string.Equals(type.GetProperty("fullName").GetString(), "First.Tools.Widget", StringComparison.Ordinal));

            Assert.Equal("Widget (First.Tools)", firstIndexType.GetProperty("displayName").GetString());
            var firstAliases = firstIndexType.GetProperty("aliases").EnumerateArray().Select(alias => alias.GetString()).Where(static value => !string.IsNullOrWhiteSpace(value)).ToList();
            Assert.Contains("Widget", firstAliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("First.Tools.Widget", firstAliases, StringComparer.OrdinalIgnoreCase);

            var searchDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "search.json")));
            var firstSearchEntry = searchDoc.RootElement.EnumerateArray().First(item =>
                string.Equals(item.GetProperty("slug").GetString(), "first-tools-widget", StringComparison.Ordinal));
            Assert.Equal("Widget (First.Tools)", firstSearchEntry.GetProperty("displayName").GetString());
            Assert.True(firstSearchEntry.TryGetProperty("aliases", out var searchAliasesProperty));
            Assert.Contains("First.Tools.Widget", searchAliasesProperty.EnumerateArray().Select(alias => alias.GetString()), StringComparer.OrdinalIgnoreCase);

            var typeDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "types", "first-tools-widget.json")));
            Assert.Equal("Widget (First.Tools)", typeDoc.RootElement.GetProperty("displayName").GetString());
            Assert.True(typeDoc.RootElement.TryGetProperty("aliases", out var typeAliasesProperty));
            Assert.Contains("Widget", typeAliasesProperty.EnumerateArray().Select(alias => alias.GetString()), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void WriteDuplicateWidgetXml(string xmlPath)
    {
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:First.Tools.Widget">
                  <summary>First widget.</summary>
                </member>
                <member name="T:Second.Tools.Widget">
                  <summary>Second widget.</summary>
                </member>
              </members>
            </doc>
            """);
    }
}
