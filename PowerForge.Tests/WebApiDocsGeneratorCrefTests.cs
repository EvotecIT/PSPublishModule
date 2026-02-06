using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    [Fact]
    public void RenderLinkedText_RendersHrefLinksFromXmlTags()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-href-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "test.xml");
        var outputPath = Path.Combine(root, "out");

        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.LinkDocs">
                  <summary>Read <see href="https://example.org/docs">the docs</see> or <a href="/docs/local">local docs</a>.</summary>
                  <seealso href="https://example.org/api">API portal</seealso>
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
            var htmlPath = Path.Combine(outputPath, "mynamespace-linkdocs.html");
            Assert.True(File.Exists(htmlPath), "Expected type HTML to be generated.");

            var html = File.ReadAllText(htmlPath);
            Assert.Contains("href=\"https://example.org/docs\"", html, StringComparison.Ordinal);
            Assert.Contains("target=\"_blank\" rel=\"noopener\"", html, StringComparison.Ordinal);
            Assert.Contains(">the docs</a>", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/docs/local\"", html, StringComparison.Ordinal);
            Assert.Contains(">local docs</a>", html, StringComparison.Ordinal);
            Assert.Contains("href=\"https://example.org/api\"", html, StringComparison.Ordinal);
            Assert.Contains(">API portal</a>", html, StringComparison.Ordinal);
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
    public void ParseXml_InheritDocCref_FallsBackToReferencedMemberDocs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-inheritdoc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "test.xml");
        var outputPath = Path.Combine(root, "out");

        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.BaseType">
                  <summary>Base summary text.</summary>
                  <remarks>Base remarks text.</remarks>
                </member>
                <member name="M:MyNamespace.BaseType.Run(System.String)">
                  <summary>Base method summary.</summary>
                  <param name="value">Input value docs.</param>
                  <returns>Result docs.</returns>
                  <exception cref="T:System.ArgumentNullException">Missing value.</exception>
                </member>
                <member name="T:MyNamespace.ChildType">
                  <inheritdoc cref="T:MyNamespace.BaseType" />
                </member>
                <member name="M:MyNamespace.ChildType.Run(System.String)">
                  <inheritdoc cref="M:MyNamespace.BaseType.Run(System.String)" />
                </member>
              </members>
            </doc>
            """);

        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            BaseUrl = "/api"
        };

        try
        {
            WebApiDocsGenerator.Generate(options);
            var typeJsonPath = Path.Combine(outputPath, "types", "mynamespace-childtype.json");
            Assert.True(File.Exists(typeJsonPath), "Expected child type JSON to be generated.");

            using var doc = JsonDocument.Parse(File.ReadAllText(typeJsonPath));
            var rootEl = doc.RootElement;

            Assert.Equal("Base summary text.", rootEl.GetProperty("summary").GetString());
            Assert.Equal("Base remarks text.", rootEl.GetProperty("remarks").GetString());

            var runMethod = rootEl.GetProperty("methods")
                .EnumerateArray()
                .First(m => string.Equals(m.GetProperty("name").GetString(), "Run", StringComparison.Ordinal));
            Assert.Equal("Base method summary.", runMethod.GetProperty("summary").GetString());
            Assert.Equal("Result docs.", runMethod.GetProperty("returns").GetString());

            var param = runMethod.GetProperty("parameters").EnumerateArray().First();
            Assert.Equal("value", param.GetProperty("name").GetString());
            Assert.Equal("Input value docs.", param.GetProperty("summary").GetString());

            var exception = runMethod.GetProperty("exceptions").EnumerateArray().First();
            Assert.Equal("System.ArgumentNullException", exception.GetProperty("type").GetString());
            Assert.Equal("Missing value.", exception.GetProperty("summary").GetString());
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
