using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorAccessibilityTests
{
    [Fact]
    public void Generate_WithAssembly_OmitsInternalXmlTypesAndMembers()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-public-surface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>PowerForge.Tests</name></assembly>
                  <members>
                    <member name="T:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture"><summary>Public fixture.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.PublicMethod"><summary>Public method.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.InternalMethod"><summary>Internal method.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.op_Addition(PowerForge.Tests.WebApiDocsPublicAccessibilityFixture,PowerForge.Tests.WebApiDocsPublicAccessibilityFixture)"><summary>Public operator.</summary></member>
                    <member name="P:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.PublicProperty"><summary>Public property.</summary></member>
                    <member name="P:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.InternalProperty"><summary>Internal property.</summary></member>
                    <member name="T:PowerForge.Tests.WebApiDocsInternalAccessibilityFixture"><summary>Internal fixture.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsInternalAccessibilityFixture.LooksPublic"><summary>Public method on an internal type.</summary></member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                AssemblyPath = typeof(WebApiDocsGeneratorAccessibilityTests).Assembly.Location,
                OutputPath = outputPath,
                Format = "json",
                BaseUrl = "/api",
                IncludeUndocumentedTypes = false
            });

            Assert.Equal(1, result.TypeCount);

            var publicTypePath = Path.Combine(
                outputPath,
                "types",
                "powerforge-tests-webapidocspublicaccessibilityfixture.json");
            Assert.True(File.Exists(publicTypePath), "Expected the exported fixture type to be documented.");
            var publicTypeJson = File.ReadAllText(publicTypePath);
            Assert.Contains("PublicMethod", publicTypeJson, StringComparison.Ordinal);
            Assert.Contains("op_Addition", publicTypeJson, StringComparison.Ordinal);
            Assert.Contains("PublicProperty", publicTypeJson, StringComparison.Ordinal);
            Assert.DoesNotContain("InternalMethod", publicTypeJson, StringComparison.Ordinal);
            Assert.DoesNotContain("InternalProperty", publicTypeJson, StringComparison.Ordinal);

            Assert.False(File.Exists(Path.Combine(
                outputPath,
                "types",
                "powerforge-tests-webapidocsinternalaccessibilityfixture.json")));

            var xref = File.ReadAllText(Path.Combine(outputPath, "xrefmap.json"));
            Assert.DoesNotContain("InternalMethod", xref, StringComparison.Ordinal);
            Assert.DoesNotContain("InternalAccessibilityFixture", xref, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

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
                    <member name="T:Sample.Qr._2.Suffixed"><summary>Suffix-like namespace.</summary></member>
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

            Assert.Equal(3, result.TypeCount);
            var html = File.ReadAllText(Path.Combine(outputPath, "index.html"));
            Assert.Single(Regex.Matches(html, "id=\"namespace-sample-qr\"", RegexOptions.IgnoreCase).Cast<Match>());
            Assert.Single(Regex.Matches(html, "id=\"namespace-sample-qr-2\"", RegexOptions.IgnoreCase).Cast<Match>());
            Assert.Single(Regex.Matches(html, "id=\"namespace-sample-qr-2-2\"", RegexOptions.IgnoreCase).Cast<Match>());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class WebApiDocsPublicAccessibilityFixture
{
    /// <summary>Visible member used by API documentation visibility tests.</summary>
    public void PublicMethod()
    {
    }

    /// <summary>Visible operator used by API documentation visibility tests.</summary>
    public static WebApiDocsPublicAccessibilityFixture operator +(
        WebApiDocsPublicAccessibilityFixture left,
        WebApiDocsPublicAccessibilityFixture right) => left;

    /// <summary>Hidden member used by API documentation visibility tests.</summary>
    internal void InternalMethod()
    {
    }

    /// <summary>Visible property used by API documentation visibility tests.</summary>
    public string PublicProperty { get; } = string.Empty;

    /// <summary>Hidden property used by API documentation visibility tests.</summary>
    internal string InternalProperty { get; } = string.Empty;
}

internal sealed class WebApiDocsInternalAccessibilityFixture
{
    /// <summary>Looks public in XML, but its declaring type is not exported.</summary>
    public void LooksPublic()
    {
    }
}
