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
