using System;
using System.IO;
using PowerForge.Web;
using Xunit;

public class WebApiDocsGeneratorMemberSignatureWarningsTests
{
    [Fact]
    public void Generate_WarnsWhenDuplicateMemberSignaturesDetected()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-duplicate-signatures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample type.</summary>
                </member>
                <member name="M:MyNamespace.Sample.Run(System.Int32)">
                  <summary>First duplicate.</summary>
                </member>
                <member name="M:MyNamespace.Sample.Run(System.Int32)">
                  <summary>Second duplicate.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "json",
            BaseUrl = "/api"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, warning => warning.Contains("[PFWEB.APIDOCS.MEMBER.SIGNATURES]", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("MyNamespace.Sample", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_DoesNotWarnForOverloadsWithDistinctSignatures()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-overload-signatures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample type.</summary>
                </member>
                <member name="M:MyNamespace.Sample.Run(System.Int32)">
                  <summary>Int overload.</summary>
                </member>
                <member name="M:MyNamespace.Sample.Run(System.String)">
                  <summary>String overload.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "json",
            BaseUrl = "/api"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("[PFWEB.APIDOCS.MEMBER.SIGNATURES]", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
