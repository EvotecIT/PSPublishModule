using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerApiDocsBatchTests
{
    [Fact]
    public void RunPipeline_ApiDocsBatch_GeneratesAllInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlOne = Path.Combine(root, "core.xml");
            var xmlTwo = Path.Combine(root, "extensions.xml");
            File.WriteAllText(xmlOne, BuildMinimalXml("CoreAssembly", "T:Core.Widget", "Core widget type."));
            File.WriteAllText(xmlTwo, BuildMinimalXml("ExtensionsAssembly", "T:Extensions.Toolkit", "Extensions toolkit type."));

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "format": "json",
                      "inputs": [
                        {
                          "id": "core",
                          "type": "CSharp",
                          "xml": "./core.xml",
                          "out": "./_site/api/core",
                          "title": "Core API"
                        },
                        {
                          "id": "extensions",
                          "type": "CSharp",
                          "xml": "./extensions.xml",
                          "out": "./_site/api/extensions",
                          "title": "Extensions API"
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
            Assert.Contains("API docs batch 2", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var coreIndex = Path.Combine(root, "_site", "api", "core", "index.json");
            var extensionsIndex = Path.Combine(root, "_site", "api", "extensions", "index.json");
            Assert.True(File.Exists(coreIndex), "Expected core API docs index.json output.");
            Assert.True(File.Exists(extensionsIndex), "Expected extensions API docs index.json output.");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string BuildMinimalXml(string assemblyName, string memberName, string summary)
    {
        return
            $"""
            <doc>
              <assembly>
                <name>{assemblyName}</name>
              </assembly>
              <members>
                <member name="{memberName}">
                  <summary>{summary}</summary>
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