using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using PowerForge.Web;

public class WebApiDocsGeneratorCrefTests
{
    [Fact]
    public void RenderLinkedText_ReplacesCrefTokensInDocsHtml()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "test.xml");
        var outputPath = Path.Combine(root, "out");

        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.BitMatrix">
                  <summary>Represents a bit matrix.</summary>
                </member>
                <member name="T:MyNamespace.Decoder">
                  <summary>Attempts to decode from a <see cref="T:MyNamespace.BitMatrix"/>.</summary>
                </member>
              </members>
            </doc>
            """);

        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };

        try
        {
            WebApiDocsGenerator.Generate(options);
            var htmlPath = Path.Combine(outputPath, "mynamespace-decoder.html");
            Assert.True(File.Exists(htmlPath), "Expected type HTML to be generated.");

            var html = File.ReadAllText(htmlPath);
            Assert.DoesNotContain("from a .", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("BitMatrix", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/api/mynamespace-bitmatrix/", html, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_AssignsUniqueMemberIdsWhenSignaturesCollide()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-ids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "test.xml");
        var outputPath = Path.Combine(root, "out");

        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Type with duplicate method doc entries.</summary>
                </member>
                <member name="M:MyNamespace.Sample.Run(System.Int32)">
                  <summary>First overload entry.</summary>
                </member>
                <member name="M:MyNamespace.Sample.Run(System.Int32)">
                  <summary>Second overload entry.</summary>
                </member>
              </members>
            </doc>
            """);

        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };

        try
        {
            WebApiDocsGenerator.Generate(options);
            var htmlPath = Path.Combine(outputPath, "mynamespace-sample.html");
            Assert.True(File.Exists(htmlPath), "Expected type HTML to be generated.");

            var html = File.ReadAllText(htmlPath);
            Assert.Contains("2 overloads", html, StringComparison.OrdinalIgnoreCase);

            var methodIds = Regex.Matches(html, "class=\"member-card\" id=\"([^\"]+)\" data-kind=\"method\"")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();
            Assert.True(methodIds.Count >= 2);
            Assert.Equal(methodIds.Count, methodIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }
}
