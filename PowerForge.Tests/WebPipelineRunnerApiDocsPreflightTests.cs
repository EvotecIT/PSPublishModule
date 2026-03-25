using System;
using System.IO;
using System.Linq;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerApiDocsPreflightTests
{
    [Fact]
    public void RunPipeline_ApiDocsPreflight_FailsOnSourceMappingsWithoutSourceConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "sourceUrlMappings": [
                        {
                          "pathPrefix": "PowerForge.Web",
                          "urlPattern": "https://example.invalid/blob/main/{path}#L{line}"
                        }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("[PFWEB.APIDOCS.SOURCE]", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sourceUrlMappings are configured but both sourceRoot and sourceUrl are empty", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocsPreflight_AllowsSuppressedSourceWarnings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-suppress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "suppressWarnings": [ "PFWEB.APIDOCS.SOURCE" ],
                      "sourceUrlMappings": [
                        {
                          "pathPrefix": "PowerForge.Web",
                          "urlPattern": "https://example.invalid/blob/main/{path}#L{line}"
                        }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            var indexJson = Path.Combine(root, "_site", "api", "index.json");
            Assert.True(File.Exists(indexJson), "Expected apidocs output to be generated when preflight warning is suppressed.");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocsPreflight_FailsWhenNavSurfaceConfiguredWithoutNavFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "navSurface": "apidocs"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("[PFWEB.APIDOCS.NAV]", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("navSurface is set but nav/navJson is empty", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocsPreflight_FailsWhenSourceRootLooksOneLevelAboveGitHubRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "TestRepo"));
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "sourceRoot": ".",
                      "sourceUrl": "https://github.com/EvotecIT/TestRepo/blob/main/{path}#L{line}"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("[PFWEB.APIDOCS.SOURCE]", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("looks one level above repo 'TestRepo'", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_RespectsMemberXrefKindsAndMaxPerType()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-member-xref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXmlWithMembers());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "both",
                      "memberXrefKinds": [ "methods", "properties" ],
                      "memberXrefMaxPerType": 1
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var xrefPath = Path.Combine(root, "_site", "api", "xrefmap.json");
            Assert.True(File.Exists(xrefPath));
            var json = File.ReadAllText(xrefPath);
            Assert.Contains("M:TestNamespace.TestType.Run(System.String)", json, StringComparison.Ordinal);
            Assert.DoesNotContain("P:TestNamespace.TestType.Name", json, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenLegacyAliasModeIsInvalid()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-legacy-mode-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "legacyAliasMode": "legacy"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("legacy-alias-mode", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenGeneratedOnlyPowerShellExamplesExceedThreshold()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-powershell-generated-threshold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpWithoutExamples());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) }");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnCoverage": true,
                      "maxPowerShellGeneratedFallbackOnlyExampleCount": 0
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("PowerShell generated-only fallback example count", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("exceeds allowed 0", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string BuildMinimalXml()
    {
        return
            """
            <doc>
              <assembly>
                <name>TestAssembly</name>
              </assembly>
              <members>
                <member name="T:TestNamespace.TestType">
                  <summary>Type summary.</summary>
                </member>
              </members>
            </doc>
            """;
    }

    private static string BuildMinimalXmlWithMembers()
    {
        return
            """
            <doc>
              <assembly>
                <name>TestAssembly</name>
              </assembly>
              <members>
                <member name="T:TestNamespace.TestType">
                  <summary>Type summary.</summary>
                </member>
                <member name="M:TestNamespace.TestType.Run(System.String)">
                  <summary>Run summary.</summary>
                  <param name="name">Name.</param>
                </member>
                <member name="P:TestNamespace.TestType.Name">
                  <summary>Name summary.</summary>
                </member>
              </members>
            </doc>
            """;
    }

    private static string BuildMinimalPowerShellHelpWithoutExamples()
    {
        return
            """
            <?xml version="1.0" encoding="utf-8"?>
            <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
              <command:command>
                <command:details>
                  <command:name>Invoke-SampleFunction</command:name>
                  <command:commandType>Function</command:commandType>
                  <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                </command:details>
                <command:syntax>
                  <command:syntaxItem>
                    <command:name>Invoke-SampleFunction</command:name>
                    <command:parameter required="true">
                      <maml:name>Name</maml:name>
                      <command:parameterValue required="true">string</command:parameterValue>
                    </command:parameter>
                  </command:syntaxItem>
                </command:syntax>
                <command:parameters>
                  <command:parameter required="true">
                    <maml:name>Name</maml:name>
                    <maml:description><maml:para>Name value.</maml:para></maml:description>
                    <command:parameterValue required="true">string</command:parameterValue>
                  </command:parameter>
                </command:parameters>
              </command:command>
            </helpItems>
            """;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
