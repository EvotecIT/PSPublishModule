using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorAccessibilityTests
{
    [Fact]
    public void Generate_AssignsUniqueIdsToCaseDistinctNamespaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-namespace-ids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Test</name></assembly>
                  <members>
                    <member name="T:Sample.QR.Upper"><summary>Upper namespace.</summary></member>
                    <member name="T:Sample.Qr.Mixed"><summary>Mixed namespace.</summary></member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                OutputPath = outputPath,
                Format = "html",
                Template = "docs",
                BaseUrl = "/api"
            });

            Assert.Equal(2, result.TypeCount);
            var html = File.ReadAllText(Path.Combine(outputPath, "index.html"));
            Assert.Single(Regex.Matches(html, "id=\"namespace-sample-qr\"", RegexOptions.IgnoreCase).Cast<Match>());
            Assert.Single(Regex.Matches(html, "id=\"namespace-sample-qr-2\"", RegexOptions.IgnoreCase).Cast<Match>());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
