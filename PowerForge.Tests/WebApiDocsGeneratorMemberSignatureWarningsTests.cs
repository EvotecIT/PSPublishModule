using System;
using System.IO;
using System.Text.Json;
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

    [Fact]
    public void Generate_RendersOverloadedIndexerParametersAsDistinctSignatures()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-indexer-signatures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>PowerForge.Tests</name></assembly>
              <members>
                <member name="T:WebApiDocsOverloadedIndexerFixture">
                  <summary>Sample type.</summary>
                </member>
                <member name="P:WebApiDocsOverloadedIndexerFixture.Item(System.String)">
                  <summary>String indexer.</summary>
                  <param name="key">String key.</param>
                </member>
                <member name="P:WebApiDocsOverloadedIndexerFixture.Item(System.Int32)">
                  <summary>Integer indexer.</summary>
                  <param name="index">Integer index.</param>
                </member>
              </members>
            </doc>
            """);

        try
        {
            var xmlOnlyOutputPath = Path.Combine(root, "xml-only-api");
            var xmlOnlyResult = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                OutputPath = xmlOnlyOutputPath,
                Format = "both",
                BaseUrl = "/api"
            });
            Assert.DoesNotContain(xmlOnlyResult.Warnings, warning => warning.Contains("[PFWEB.APIDOCS.MEMBER.SIGNATURES]", StringComparison.OrdinalIgnoreCase));
            AssertIndexerProperties(
                xmlOnlyOutputPath,
                "WebApiDocsOverloadedIndexerFixture",
                "this[System.String key]",
                "System.String",
                "this[System.Int32 index]",
                "System.Int32");
            var xmlOnlyHtml = string.Join(
                Environment.NewLine,
                Directory.GetFiles(xmlOnlyOutputPath, "*.html", SearchOption.AllDirectories)
                    .Select(File.ReadAllText));
            Assert.Contains("this[System.String key]", xmlOnlyHtml, StringComparison.Ordinal);
            Assert.Contains("this[System.Int32 index]", xmlOnlyHtml, StringComparison.Ordinal);

            var reflectedOutputPath = Path.Combine(root, "reflected-api");
            var reflectedResult = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                AssemblyPath = typeof(WebApiDocsOverloadedIndexerFixture).Assembly.Location,
                OutputPath = reflectedOutputPath,
                Format = "json",
                BaseUrl = "/api",
                IncludeUndocumentedTypes = false
            });
            Assert.DoesNotContain(reflectedResult.Warnings, warning => warning.Contains("[PFWEB.APIDOCS.MEMBER.SIGNATURES]", StringComparison.OrdinalIgnoreCase));
            AssertIndexerProperties(
                reflectedOutputPath,
                "WebApiDocsOverloadedIndexerFixture",
                "this[String key]",
                "System.String",
                "this[Int32 index]",
                "System.Int32");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PreservesIndexerOverloadsWithSimilarNormalizedTypeNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-case-indexers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var xmlPath = Path.Combine(root, "fixture.xml");
        File.WriteAllText(
            xmlPath,
            """
            <?xml version="1.0"?>
            <doc>
              <assembly><name>PowerForge.Tests</name></assembly>
              <members>
                <member name="T:WebApiDocsNormalizedTypeIndexerFixture">
                  <summary>Normalized-type indexer fixture.</summary>
                </member>
                <member name="P:WebApiDocsNormalizedTypeIndexerFixture.Item(IndexerKey)">
                  <summary>Gets the global key value.</summary>
                  <param name="value">The global key.</param>
                </member>
                <member name="P:WebApiDocsNormalizedTypeIndexerFixture.Item(System.IndexerKey)">
                  <summary>Gets the System key value.</summary>
                  <param name="value">The System key.</param>
                </member>
              </members>
            </doc>
            """);

        try
        {
            var outputPath = Path.Combine(root, "api");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                AssemblyPath = typeof(WebApiDocsNormalizedTypeIndexerFixture).Assembly.Location,
                OutputPath = outputPath,
                Format = "json",
                BaseUrl = "/api",
                IncludeUndocumentedTypes = false
            });

            var properties = ReadProperties(outputPath, "WebApiDocsNormalizedTypeIndexerFixture");
            Assert.Equal(2, properties.Length);
            Assert.Contains(properties, property =>
                property.GetProperty("summary").GetString() == "Gets the global key value." &&
                property.GetProperty("parameters")[0].GetProperty("type").GetString() == "IndexerKey");
            Assert.Contains(properties, property =>
                property.GetProperty("summary").GetString() == "Gets the System key value." &&
                property.GetProperty("parameters")[0].GetProperty("type").GetString() == "System.IndexerKey");

            var fallbackXmlPath = Path.Combine(root, "undocumented.xml");
            File.WriteAllText(
                fallbackXmlPath,
                """
                <?xml version="1.0"?>
                <doc>
                  <assembly><name>PowerForge.Tests</name></assembly>
                  <members />
                </doc>
                """);
            var fallbackOutputPath = Path.Combine(root, "reflection-fallback-api");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = fallbackXmlPath,
                AssemblyPath = typeof(WebApiDocsNormalizedTypeIndexerFixture).Assembly.Location,
                OutputPath = fallbackOutputPath,
                Format = "json",
                BaseUrl = "/api",
                IncludeUndocumentedTypes = true
            });

            var fallbackProperties = ReadProperties(
                fallbackOutputPath,
                "WebApiDocsNormalizedTypeIndexerFixture");
            Assert.Equal(2, fallbackProperties.Length);
            Assert.All(fallbackProperties, property =>
                Assert.Equal("public", property.GetProperty("access").GetString()));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_DoesNotWarnForPowerShellParameterSetsWithSameRenderedSyntax()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-powershell-parameter-sets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var helpPath = Path.Combine(root, "PSWriteOffice-help.xml");
        File.WriteAllText(helpPath, CreatePowerShellDuplicateSyntaxHelpXml(includeExplicitParameterSetNames: true));

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            Type = ApiDocsType.PowerShell,
            HelpPath = helpPath,
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

    [Fact]
    public void Generate_WarnsForPowerShellDuplicateSyntaxWhenParameterSetNamesAreGenerated()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-powershell-generated-parameter-sets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var helpPath = Path.Combine(root, "PSWriteOffice-help.xml");
        File.WriteAllText(helpPath, CreatePowerShellDuplicateSyntaxHelpXml(includeExplicitParameterSetNames: false));

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            Type = ApiDocsType.PowerShell,
            HelpPath = helpPath,
            OutputPath = outputPath,
            Format = "json",
            BaseUrl = "/api"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, warning => warning.Contains("[PFWEB.APIDOCS.MEMBER.SIGNATURES]", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_WarnsForPowerShellDuplicateSyntaxWhenExplicitParameterSetNamesAreDuplicated()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-powershell-duplicate-explicit-parameter-sets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var helpPath = Path.Combine(root, "PSWriteOffice-help.xml");
        File.WriteAllText(helpPath, CreatePowerShellDuplicateSyntaxHelpXml(includeExplicitParameterSetNames: true, duplicateExplicitParameterSetName: true));

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            Type = ApiDocsType.PowerShell,
            HelpPath = helpPath,
            OutputPath = outputPath,
            Format = "json",
            BaseUrl = "/api"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, warning => warning.Contains("[PFWEB.APIDOCS.MEMBER.SIGNATURES]", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static string CreatePowerShellDuplicateSyntaxHelpXml(bool includeExplicitParameterSetNames)
        => CreatePowerShellDuplicateSyntaxHelpXml(includeExplicitParameterSetNames, duplicateExplicitParameterSetName: false);

    private static void AssertIndexerProperties(
        string outputPath,
        string fullName,
        string firstSignature,
        string firstParameterType,
        string secondSignature,
        string secondParameterType)
    {
        var properties = ReadProperties(outputPath, fullName);

        Assert.Equal(2, properties.Length);
        Assert.Contains(properties, property =>
            property.GetProperty("signature").GetString()!.Contains(firstSignature, StringComparison.Ordinal) &&
            property.GetProperty("parameters")[0].GetProperty("type").GetString() == firstParameterType);
        Assert.Contains(properties, property =>
            property.GetProperty("signature").GetString()!.Contains(secondSignature, StringComparison.Ordinal) &&
            property.GetProperty("parameters")[0].GetProperty("type").GetString() == secondParameterType);
    }

    private static JsonElement[] ReadProperties(string outputPath, string fullName)
    {
        var typePath = Directory.GetFiles(Path.Combine(outputPath, "types"), "*.json", SearchOption.TopDirectoryOnly)
            .Single(path => File.ReadAllText(path).Contains(
                $"\"fullName\": \"{fullName}\"",
                StringComparison.Ordinal));
        using var typeDocument = JsonDocument.Parse(File.ReadAllText(typePath));
        return typeDocument.RootElement.GetProperty("properties")
            .EnumerateArray()
            .Select(static property => property.Clone())
            .ToArray();
    }

    private static string CreatePowerShellDuplicateSyntaxHelpXml(bool includeExplicitParameterSetNames, bool duplicateExplicitParameterSetName)
    {
        var firstSetAttribute = includeExplicitParameterSetNames ? " parameterSetName=\"Document\"" : string.Empty;
        var secondSetName = duplicateExplicitParameterSetName ? "Document" : "PipelineDocument";
        var secondSetAttribute = includeExplicitParameterSetNames ? $" parameterSetName=\"{secondSetName}\"" : string.Empty;

        return
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
              <command:command>
                <command:details>
                  <command:name>Add-OfficeMarkdownTable</command:name>
                  <maml:description>
                    <maml:para>Adds a table to a markdown document.</maml:para>
                  </maml:description>
                </command:details>
                <maml:description>
                  <maml:para>Adds a table to a markdown document.</maml:para>
                </maml:description>
                <command:syntax>
                  <command:syntaxItem{{firstSetAttribute}}>
                    <command:name>Add-OfficeMarkdownTable</command:name>
                    <command:parameter required="true" globbing="false" pipelineInput="false" position="named">
                      <maml:name>Document</maml:name>
                      <command:parameterValue required="true">MarkdownDoc</command:parameterValue>
                    </command:parameter>
                    <command:parameter required="true" globbing="false" pipelineInput="true" position="named">
                      <maml:name>InputObject</maml:name>
                      <command:parameterValue required="true">object</command:parameterValue>
                    </command:parameter>
                  </command:syntaxItem>
                  <command:syntaxItem{{secondSetAttribute}}>
                    <command:name>Add-OfficeMarkdownTable</command:name>
                    <command:parameter required="true" globbing="false" pipelineInput="true" position="named">
                      <maml:name>Document</maml:name>
                      <command:parameterValue required="true">MarkdownDoc</command:parameterValue>
                    </command:parameter>
                    <command:parameter required="true" globbing="false" pipelineInput="true" position="named">
                      <maml:name>InputObject</maml:name>
                      <command:parameterValue required="true">object</command:parameterValue>
                    </command:parameter>
                  </command:syntaxItem>
                </command:syntax>
              </command:command>
            </helpItems>
            """;
    }
}

public sealed class WebApiDocsOverloadedIndexerFixture
{
    public string this[string key] => key;

    public string this[int index] => index.ToString();
}

public sealed class IndexerKey
{
}

public sealed class WebApiDocsNormalizedTypeIndexerFixture
{
    public string this[IndexerKey value] => value.GetType().Name;

    public string this[System.IndexerKey value] => value.GetType().Name;
}

namespace System
{
    public sealed class IndexerKey
    {
    }
}
