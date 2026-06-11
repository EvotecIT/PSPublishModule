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

    private static string CreatePowerShellDuplicateSyntaxHelpXml(bool includeExplicitParameterSetNames)
    {
        var firstSetAttribute = includeExplicitParameterSetNames ? " parameterSetName=\"Document\"" : string.Empty;
        var secondSetAttribute = includeExplicitParameterSetNames ? " parameterSetName=\"PipelineDocument\"" : string.Empty;

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
