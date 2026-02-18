using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerExtendsTests
{
    [Fact]
    public void RunPipeline_ExtendsOnlyConfig_LoadsPresetSteps()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-extends-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_temp"));
            File.WriteAllText(Path.Combine(root, "_temp", "source.json"), """{ "name": "Old" }""");

            var presetsRoot = Path.Combine(root, "config", "presets");
            Directory.CreateDirectory(presetsRoot);
            File.WriteAllText(Path.Combine(presetsRoot, "base.json"),
                """
                {
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/final.json",
                      "operations": [
                        { "op": "set", "path": "name", "value": "New" }
                      ]
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "extends": "./config/presets/base.json"
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            using var outputDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "_temp", "final.json")));
            Assert.Equal("New", outputDoc.RootElement.GetProperty("name").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ExtendsArray_AppendsStepsInOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-extends-array-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_temp"));
            File.WriteAllText(Path.Combine(root, "_temp", "source.json"), """{ "order": [] }""");

            File.WriteAllText(Path.Combine(root, "base-a.json"),
                """
                {
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/step-a.json",
                      "operations": [
                        { "op": "append", "path": "order", "value": "A" }
                      ]
                    }
                  ]
                }
                """);

            File.WriteAllText(Path.Combine(root, "base-b.json"),
                """
                {
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/step-a.json",
                      "out": "./_temp/step-b.json",
                      "operations": [
                        { "op": "append", "path": "order", "value": "B" }
                      ]
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "Extends": ["./base-a.json", "./base-b.json"],
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/step-b.json",
                      "out": "./_temp/final.json",
                      "operations": [
                        { "op": "append", "path": "order", "value": "C" }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Equal(3, result.Steps.Count);
            Assert.All(result.Steps, step => Assert.True(step.Success, step.Message));

            using var outputDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "_temp", "final.json")));
            var order = outputDoc.RootElement.GetProperty("order");
            Assert.Equal(3, order.GetArrayLength());
            Assert.Equal("A", order[0].GetString());
            Assert.Equal("B", order[1].GetString());
            Assert.Equal("C", order[2].GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ExtendsCycle_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-extends-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var first = Path.Combine(root, "pipeline-a.json");
            var second = Path.Combine(root, "pipeline-b.json");
            File.WriteAllText(first, """{ "extends": "./pipeline-b.json" }""");
            File.WriteAllText(second, """{ "extends": "./pipeline-a.json" }""");

            var error = Assert.Throws<InvalidOperationException>(() => WebPipelineRunner.RunPipeline(first, logger: null));
            Assert.Contains("inheritance loop", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
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
