using System;
using System.IO;
using PowerForge.Web;
using Xunit;

namespace PowerForge.Tests;

public class WebApiDocsGeneratorLegacyAliasTests
{
    [Fact]
    public void GenerateDocsHtml_MarksLegacyFlatAliasAsNoIndex_WhileKeepingCanonicalDirectoryPageIndexable()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-legacy-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.SampleType">
                  <summary>Sample type.</summary>
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
            Title = "Sample API"
        };

        try
        {
            WebApiDocsGenerator.Generate(options);

            var aliasPath = Path.Combine(outputPath, "mynamespace-sampletype.html");
            var canonicalPath = Path.Combine(outputPath, "mynamespace-sampletype", "index.html");
            Assert.True(File.Exists(aliasPath), "Expected legacy flat alias page to be generated.");
            Assert.True(File.Exists(canonicalPath), "Expected canonical directory page to be generated.");

            var aliasHtml = File.ReadAllText(aliasPath);
            var canonicalHtml = File.ReadAllText(canonicalPath);

            Assert.Contains("name=\"robots\" content=\"noindex,follow\"", aliasHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("data-pf=\"api-docs-legacy-alias\"", aliasHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<link rel=\"canonical\" href=\"/api/mynamespace-sampletype/\"", aliasHtml, StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain("data-pf=\"api-docs-legacy-alias\"", canonicalHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("name=\"robots\" content=\"noindex,follow\"", canonicalHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<link rel=\"canonical\" href=\"/api/mynamespace-sampletype/\"", canonicalHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
