using System;
using System.IO;
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
}
