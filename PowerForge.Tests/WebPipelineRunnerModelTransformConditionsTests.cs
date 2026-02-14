using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerModelTransformConditionsTests
{
    [Fact]
    public void RunPipeline_ModelTransform_WhenEquals_FiltersWildcardTargets()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-when-equals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            var outputPath = Path.Combine(root, "_temp", "transformed.json");
            var reportPath = Path.Combine(root, "_reports", "model-transform.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath,
                """
                {
                  "items": [
                    { "enabled": true },
                    { "enabled": false }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/transformed.json",
                      "reportPath": "./_reports/model-transform.json",
                      "operations": [
                        {
                          "op": "set",
                          "path": "items[*].enabled",
                          "value": false,
                          "exactTargets": 1,
                          "when": {
                            "equals": true
                          }
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

            using var outputDoc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var outputRoot = outputDoc.RootElement;
            Assert.False(outputRoot.GetProperty("items")[0].GetProperty("enabled").GetBoolean());
            Assert.False(outputRoot.GetProperty("items")[1].GetProperty("enabled").GetBoolean());

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(1, reportDoc.RootElement.GetProperty("Operations")[0].GetProperty("TargetsApplied").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_WhenExistsFalse_AppliesToMissingTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-when-exists-false-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            var outputPath = Path.Combine(root, "_temp", "transformed.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath, """{ "meta": {} }""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/transformed.json",
                      "operations": [
                        {
                          "op": "set",
                          "path": "meta.flag",
                          "value": "created",
                          "when": { "exists": false },
                          "exactTargets": 1
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

            using var outputDoc = JsonDocument.Parse(File.ReadAllText(outputPath));
            Assert.Equal("created", outputDoc.RootElement.GetProperty("meta").GetProperty("flag").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_Copy_WhenCondition_FiltersDestinationTargets()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-copy-when-filtered-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            var outputPath = Path.Combine(root, "_temp", "transformed.json");
            var reportPath = Path.Combine(root, "_reports", "model-transform.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath,
                """
                {
                  "site": { "name": "A" },
                  "targets": [
                    { "alias": "" },
                    { "alias": "keep" }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/transformed.json",
                      "reportPath": "./_reports/model-transform.json",
                      "operations": [
                        {
                          "op": "copy",
                          "from": "site.name",
                          "path": "targets[*].alias",
                          "exactTargets": 1,
                          "when": { "equals": "" }
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

            using var outputDoc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var outputRoot = outputDoc.RootElement;
            Assert.Equal("A", outputRoot.GetProperty("targets")[0].GetProperty("alias").GetString());
            Assert.Equal("keep", outputRoot.GetProperty("targets")[1].GetProperty("alias").GetString());

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(1, reportDoc.RootElement.GetProperty("Operations")[0].GetProperty("TargetsApplied").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_Move_WhenConditionSkipsTarget_KeepsSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-move-when-skipped-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            var outputPath = Path.Combine(root, "_temp", "transformed.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath, """{ "source": { "value": "A" }, "target": { "value": "" } }""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/transformed.json",
                      "operations": [
                        {
                          "op": "move",
                          "from": "source.value",
                          "path": "target.value",
                          "exactTargets": 0,
                          "when": { "exists": false }
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

            using var outputDoc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var outputRoot = outputDoc.RootElement;
            Assert.Equal("A", outputRoot.GetProperty("source").GetProperty("value").GetString());
            Assert.Equal(string.Empty, outputRoot.GetProperty("target").GetProperty("value").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_Copy_WhenCondition_WildcardPairingMismatchFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-copy-when-mismatch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath,
                """
                {
                  "sourceItems": [
                    { "value": "A" },
                    { "value": "B" }
                  ],
                  "targetItems": [
                    {},
                    {}
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/transformed.json",
                      "operations": [
                        {
                          "op": "copy",
                          "from": "sourceItems[*].value",
                          "path": "targetItems[*].missing",
                          "when": { "exists": true }
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
            Assert.Contains("after condition filtering", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_Copy_FromWhen_FiltersSourceWildcard()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-copy-fromwhen-filter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            var outputPath = Path.Combine(root, "_temp", "transformed.json");
            var reportPath = Path.Combine(root, "_reports", "model-transform.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath,
                """
                {
                  "sourceItems": [
                    { "value": "A" },
                    { "value": "B" }
                  ],
                  "targetItems": [
                    { "value": "" },
                    { "value": "locked" }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/transformed.json",
                      "reportPath": "./_reports/model-transform.json",
                      "operations": [
                        {
                          "op": "copy",
                          "from": "sourceItems[*].value",
                          "path": "targetItems[*].value",
                          "fromWhen": { "equals": "A" },
                          "when": { "equals": "" },
                          "exactTargets": 1
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

            using var outputDoc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var outputRoot = outputDoc.RootElement;
            Assert.Equal("A", outputRoot.GetProperty("targetItems")[0].GetProperty("value").GetString());
            Assert.Equal("locked", outputRoot.GetProperty("targetItems")[1].GetProperty("value").GetString());

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(1, reportDoc.RootElement.GetProperty("Operations")[0].GetProperty("TargetsApplied").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_Move_FromWhen_RemovesOnlyAppliedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-move-fromwhen-removal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            var outputPath = Path.Combine(root, "_temp", "transformed.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath,
                """
                {
                  "sourceItems": [
                    { "value": "A" },
                    { "value": "B" }
                  ],
                  "targetItems": [
                    { "value": "" },
                    { "value": "locked" }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "model-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/transformed.json",
                      "operations": [
                        {
                          "op": "move",
                          "from": "sourceItems[*].value",
                          "path": "targetItems[*].value",
                          "fromWhen": { "equals": "A" },
                          "when": { "equals": "" },
                          "exactTargets": 1
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

            using var outputDoc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var outputRoot = outputDoc.RootElement;
            Assert.False(outputRoot.GetProperty("sourceItems")[0].TryGetProperty("value", out _));
            Assert.Equal("B", outputRoot.GetProperty("sourceItems")[1].GetProperty("value").GetString());
            Assert.Equal("A", outputRoot.GetProperty("targetItems")[0].GetProperty("value").GetString());
            Assert.Equal("locked", outputRoot.GetProperty("targetItems")[1].GetProperty("value").GetString());
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
