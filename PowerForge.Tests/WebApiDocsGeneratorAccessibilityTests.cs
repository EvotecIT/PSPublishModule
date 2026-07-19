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
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.#ctor"><summary>Public constructor.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.#ctor(System.Int32)"><summary>Internal constructor.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.PublicMethod"><summary>Public method.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.InternalMethod"><summary>Internal method.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.Overloaded(System.String)"><summary>Public overload.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.Overloaded(System.Int32)"><summary>Internal overload.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.op_Addition(PowerForge.Tests.WebApiDocsPublicAccessibilityFixture,PowerForge.Tests.WebApiDocsPublicAccessibilityFixture)"><summary>Public operator.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.op_Implicit(PowerForge.Tests.WebApiDocsPublicAccessibilityFixture)~System.String"><summary>String conversion.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.op_Implicit(PowerForge.Tests.WebApiDocsPublicAccessibilityFixture)~System.Int32"><summary>Integer conversion.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.Generic``1(System.String)"><summary>Single generic parameter.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.Generic``2(System.String)"><summary>Two generic parameters.</summary></member>
                    <member name="P:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.PublicProperty"><summary>Public property.</summary></member>
                    <member name="P:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.InternalProperty"><summary>Internal property.</summary></member>
                    <member name="P:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.Item(System.String)"><summary>Public indexer.</summary></member>
                    <member name="P:PowerForge.Tests.WebApiDocsPublicAccessibilityFixture.Item(System.Int32)"><summary>Internal indexer.</summary></member>
                    <member name="T:PowerForge.Tests.WebApiDocsInternalAccessibilityFixture"><summary>Internal fixture.</summary></member>
                    <member name="M:PowerForge.Tests.WebApiDocsInternalAccessibilityFixture.LooksPublic"><summary>Public method on an internal type.</summary></member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "api");
            const string internalTypeSlug = "powerforge-tests-webapidocsinternalaccessibilityfixture";
            var internalJsonPath = Path.Combine(outputPath, "types", internalTypeSlug + ".json");
            var internalRoutePath = Path.Combine(outputPath, internalTypeSlug, "index.html");
            var internalAliasPath = Path.Combine(outputPath, internalTypeSlug + ".html");

            var xmlOnlyResult = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                OutputPath = outputPath,
                Format = "both",
                Template = "docs",
                BaseUrl = "/api",
                IncludeUndocumentedTypes = false
            });
            Assert.Equal(2, xmlOnlyResult.TypeCount);
            Assert.True(File.Exists(internalJsonPath));
            Assert.True(File.Exists(internalRoutePath));
            Assert.True(File.Exists(internalAliasPath));

            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                AssemblyPath = typeof(WebApiDocsGeneratorAccessibilityTests).Assembly.Location,
                OutputPath = outputPath,
                Format = "both",
                Template = "docs",
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
            Assert.Contains("Public constructor.", publicTypeJson, StringComparison.Ordinal);
            Assert.Contains("Public overload.", publicTypeJson, StringComparison.Ordinal);
            Assert.Contains("Public operator.", publicTypeJson, StringComparison.Ordinal);
            Assert.Contains("String conversion.", publicTypeJson, StringComparison.Ordinal);
            Assert.Contains("Integer conversion.", publicTypeJson, StringComparison.Ordinal);
            Assert.Contains("Single generic parameter.", publicTypeJson, StringComparison.Ordinal);
            Assert.Contains("Two generic parameters.", publicTypeJson, StringComparison.Ordinal);
            Assert.Contains("Public indexer.", publicTypeJson, StringComparison.Ordinal);
            Assert.DoesNotContain("InternalMethod", publicTypeJson, StringComparison.Ordinal);
            Assert.DoesNotContain("InternalProperty", publicTypeJson, StringComparison.Ordinal);
            Assert.DoesNotContain("Internal constructor.", publicTypeJson, StringComparison.Ordinal);
            Assert.DoesNotContain("Internal overload.", publicTypeJson, StringComparison.Ordinal);
            Assert.DoesNotContain("Internal indexer.", publicTypeJson, StringComparison.Ordinal);

            Assert.False(File.Exists(internalJsonPath));
            Assert.False(File.Exists(internalRoutePath));
            Assert.False(File.Exists(internalAliasPath));

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
    public void Generate_WithUninspectableRequestedAssembly_FailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-fail-closed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Broken</name></assembly>
                  <members>
                    <member name="T:Broken.InternalType"><summary>Must not be emitted.</summary></member>
                  </members>
                </doc>
                """);
            var assemblyPath = Path.Combine(root, "broken.dll");
            File.WriteAllText(assemblyPath, "not an assembly");
            var outputPath = Path.Combine(root, "api");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                WebApiDocsGenerator.Generate(new WebApiDocsOptions
                {
                    XmlPath = xmlPath,
                    AssemblyPath = assemblyPath,
                    OutputPath = outputPath,
                    Format = "json"
                }));

            Assert.Contains("could not be inspected", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(outputPath, "index.json")));
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
    /// <summary>Visible constructor used by API documentation visibility tests.</summary>
    public WebApiDocsPublicAccessibilityFixture()
    {
    }

    /// <summary>Hidden constructor used by API documentation visibility tests.</summary>
    internal WebApiDocsPublicAccessibilityFixture(int value)
    {
        _ = value;
    }

    /// <summary>Visible member used by API documentation visibility tests.</summary>
    public void PublicMethod()
    {
    }

    /// <summary>Visible operator used by API documentation visibility tests.</summary>
    public static WebApiDocsPublicAccessibilityFixture operator +(
        WebApiDocsPublicAccessibilityFixture left,
        WebApiDocsPublicAccessibilityFixture right) => left;

    /// <summary>Visible conversion used by API documentation visibility tests.</summary>
    public static implicit operator string(WebApiDocsPublicAccessibilityFixture value) =>
        value.ToString() ?? string.Empty;

    /// <summary>Visible conversion used by API documentation visibility tests.</summary>
    public static implicit operator int(WebApiDocsPublicAccessibilityFixture value) => 0;

    /// <summary>Visible generic overload used by API documentation visibility tests.</summary>
    public string Generic<T>(string value) => value;

    /// <summary>Visible generic overload used by API documentation visibility tests.</summary>
    public string Generic<TFirst, TSecond>(string value) => value;

    /// <summary>Visible overload used by API documentation visibility tests.</summary>
    public void Overloaded(string value)
    {
        _ = value;
    }

    /// <summary>Hidden overload used by API documentation visibility tests.</summary>
    internal void Overloaded(int value)
    {
        _ = value;
    }

    /// <summary>Hidden member used by API documentation visibility tests.</summary>
    internal void InternalMethod()
    {
    }

    /// <summary>Visible property used by API documentation visibility tests.</summary>
    public string PublicProperty { get; } = string.Empty;

    /// <summary>Hidden property used by API documentation visibility tests.</summary>
    internal string InternalProperty { get; } = string.Empty;

    /// <summary>Visible indexer used by API documentation visibility tests.</summary>
    public string this[string key] => key;

    /// <summary>Hidden indexer used by API documentation visibility tests.</summary>
    internal string this[int index] => index.ToString();
}

internal sealed class WebApiDocsInternalAccessibilityFixture
{
    /// <summary>Looks public in XML, but its declaring type is not exported.</summary>
    public void LooksPublic()
    {
    }
}
