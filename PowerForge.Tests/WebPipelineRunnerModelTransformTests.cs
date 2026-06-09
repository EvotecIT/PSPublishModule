using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerModelTransformTests
{
    [Fact]
    public void RunPipeline_ModelTransform_AppliesTypedOperations()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-ok-" + Guid.NewGuid().ToString("N"));
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
                  "site": {
                    "name": "Old"
                  },
                  "legacy": {
                    "items": [
                      { "id": 0 }
                    ]
                  },
                  "draft": true,
                  "items": [
                    { "id": 1 }
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
                        { "op": "set", "path": "site.name", "value": "New" },
                        { "op": "replace", "path": "site.name", "value": "Newest" },
                        { "op": "copy", "from": "site.name", "path": "site.displayName" },
                        { "op": "move", "from": "legacy.items", "path": "items" },
                        { "op": "insert", "path": "items", "index": 0, "value": { "id": 99 } },
                        { "op": "append", "path": "items", "value": { "id": 2 } },
                        { "op": "merge", "path": "site", "value": { "environment": "ci" } },
                        { "op": "remove", "path": "draft" }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success, result.Steps.Count > 0 ? result.Steps[0].Message : "pipeline failed");
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("model-transform ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            using var outputDoc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var outputRoot = outputDoc.RootElement;
            Assert.Equal("Newest", outputRoot.GetProperty("site").GetProperty("name").GetString());
            Assert.Equal("Newest", outputRoot.GetProperty("site").GetProperty("displayName").GetString());
            Assert.Equal("ci", outputRoot.GetProperty("site").GetProperty("environment").GetString());
            Assert.False(outputRoot.TryGetProperty("draft", out _));
            Assert.False(outputRoot.GetProperty("legacy").TryGetProperty("items", out _));
            Assert.Equal(3, outputRoot.GetProperty("items").GetArrayLength());
            Assert.Equal(99, outputRoot.GetProperty("items")[0].GetProperty("id").GetInt32());

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var reportRoot = reportDoc.RootElement;
            Assert.Equal(8, reportRoot.GetProperty("OperationsTotal").GetInt32());
            Assert.Equal(8, reportRoot.GetProperty("OperationsSucceeded").GetInt32());
            Assert.Equal(0, reportRoot.GetProperty("OperationsFailed").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_Insert_RequiresIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-insert-missing-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath,
                """
                {
                  "items": [
                    { "id": 1 }
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
                        { "op": "insert", "path": "items", "value": { "id": 2 } }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("requires non-negative index", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_Move_RequiresSourcePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-move-missing-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath, """{ "site": { "name": "Old" } }""");

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
                        { "op": "move", "path": "items" }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("requires from/source", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_StrictFalse_ContinuesOnOperationErrors()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-nonstrict-" + Guid.NewGuid().ToString("N"));
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
                  "site": {
                    "name": "Old"
                  }
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
                      "strict": false,
                      "operations": [
                        { "op": "remove", "path": "missing.path" },
                        { "op": "set", "path": "site.name", "value": "Updated" }
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
            Assert.Equal("Updated", outputDoc.RootElement.GetProperty("site").GetProperty("name").GetString());

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var reportRoot = reportDoc.RootElement;
            Assert.Equal(2, reportRoot.GetProperty("OperationsTotal").GetInt32());
            Assert.Equal(1, reportRoot.GetProperty("OperationsSucceeded").GetInt32());
            Assert.Equal(1, reportRoot.GetProperty("OperationsFailed").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_StrictTrue_FailsOnUnsupportedOperation()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-invalid-op-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath, """{ "site": { "name": "Old" } }""");

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
                        { "op": "unknown-op", "path": "site.name", "value": "New" }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("not supported", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_WildcardPathOperations_ApplyDeterministically()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-wildcard-path-" + Guid.NewGuid().ToString("N"));
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
                    { "title": "One", "tags": [] },
                    { "title": "Two", "tags": [ "existing" ] }
                  ],
                  "drafts": [
                    { "enabled": true, "name": "A" },
                    { "enabled": false, "name": "B" }
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
                        { "op": "set", "path": "items[*].title", "value": "Unified" },
                        { "op": "append", "path": "items[*].tags", "value": "new-tag" },
                        { "op": "remove", "path": "drafts[*].enabled" }
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
            Assert.Equal("Unified", outputRoot.GetProperty("items")[0].GetProperty("title").GetString());
            Assert.Equal("Unified", outputRoot.GetProperty("items")[1].GetProperty("title").GetString());
            Assert.Equal(1, outputRoot.GetProperty("items")[0].GetProperty("tags").GetArrayLength());
            Assert.Equal("new-tag", outputRoot.GetProperty("items")[0].GetProperty("tags")[0].GetString());
            Assert.Equal(2, outputRoot.GetProperty("items")[1].GetProperty("tags").GetArrayLength());
            Assert.Equal("new-tag", outputRoot.GetProperty("items")[1].GetProperty("tags")[1].GetString());
            Assert.False(outputRoot.GetProperty("drafts")[0].TryGetProperty("enabled", out _));
            Assert.False(outputRoot.GetProperty("drafts")[1].TryGetProperty("enabled", out _));

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var operations = reportDoc.RootElement.GetProperty("Operations");
            Assert.Equal(2, operations[0].GetProperty("TargetsApplied").GetInt32());
            Assert.Equal(2, operations[1].GetProperty("TargetsApplied").GetInt32());
            Assert.Equal(2, operations[2].GetProperty("TargetsApplied").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_WildcardCopyMove_PairsSourceAndTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-wildcard-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            var outputPath = Path.Combine(root, "_temp", "transformed.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath,
                """
                {
                  "sourceNames": [
                    { "value": "A" },
                    { "value": "B" }
                  ],
                  "targetNames": [
                    { "value": "" },
                    { "value": "" }
                  ],
                  "sourceValues": [10, 20],
                  "targetValues": [0, 0]
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
                        { "op": "copy", "from": "sourceNames[*].value", "path": "targetNames[*].value" },
                        { "op": "move", "from": "sourceValues[*]", "path": "targetValues[*]" }
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
            Assert.Equal("A", outputRoot.GetProperty("targetNames")[0].GetProperty("value").GetString());
            Assert.Equal("B", outputRoot.GetProperty("targetNames")[1].GetProperty("value").GetString());
            Assert.Equal(10, outputRoot.GetProperty("targetValues")[0].GetInt32());
            Assert.Equal(20, outputRoot.GetProperty("targetValues")[1].GetInt32());
            Assert.Equal(0, outputRoot.GetProperty("sourceValues").GetArrayLength());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_QuotedPropertyPaths_HandleSpecialKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-quoted-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            var outputPath = Path.Combine(root, "_temp", "transformed.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath,
                """
                {
                  "meta": {
                    "x.y": {
                      "z[0]": 1
                    },
                    "with space": "old"
                  },
                  "map": {
                    "a.b": 1
                  },
                  "arr": [
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
                        { "op": "set", "path": "meta['x.y']['z[0]']", "value": 2 },
                        { "op": "replace", "path": "meta['with space']", "value": "new" },
                        { "op": "copy", "from": "meta['x.y']['z[0]']", "path": "arr[0]['value.dot']" },
                        { "op": "remove", "path": "map['a.b']" }
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
            Assert.Equal(2, outputRoot.GetProperty("meta").GetProperty("x.y").GetProperty("z[0]").GetInt32());
            Assert.Equal("new", outputRoot.GetProperty("meta").GetProperty("with space").GetString());
            Assert.Equal(2, outputRoot.GetProperty("arr")[0].GetProperty("value.dot").GetInt32());
            Assert.False(outputRoot.GetProperty("map").TryGetProperty("a.b", out _));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_WildcardObjectKeys_SupportsQuotedResolvedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-wildcard-object-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            var outputPath = Path.Combine(root, "_temp", "transformed.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath,
                """
                {
                  "map": {
                    "a.b": {
                      "enabled": true
                    },
                    "plain": {
                      "enabled": false
                    }
                  }
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
                        { "op": "remove", "path": "map[*].enabled" }
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
            var outputRoot = outputDoc.RootElement.GetProperty("map");
            Assert.False(outputRoot.GetProperty("a.b").TryGetProperty("enabled", out _));
            Assert.False(outputRoot.GetProperty("plain").TryGetProperty("enabled", out _));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_RecursiveWildcard_UpdatesNestedMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-recursive-wildcard-" + Guid.NewGuid().ToString("N"));
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
                  "enabled": true,
                  "root": {
                    "enabled": true,
                    "child": {
                      "enabled": true
                    }
                  },
                  "items": [
                    { "enabled": true },
                    { "meta": { "enabled": true } }
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
                        { "op": "set", "path": "**.enabled", "value": false }
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
            Assert.False(outputRoot.GetProperty("enabled").GetBoolean());
            Assert.False(outputRoot.GetProperty("root").GetProperty("enabled").GetBoolean());
            Assert.False(outputRoot.GetProperty("root").GetProperty("child").GetProperty("enabled").GetBoolean());
            Assert.False(outputRoot.GetProperty("items")[0].GetProperty("enabled").GetBoolean());
            Assert.False(outputRoot.GetProperty("items")[1].GetProperty("enabled").GetBoolean());
            Assert.False(outputRoot.GetProperty("items")[1].GetProperty("meta").GetProperty("enabled").GetBoolean());

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(6, reportDoc.RootElement.GetProperty("Operations")[0].GetProperty("TargetsApplied").GetInt32());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_TargetGuards_EnforceMaximum()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-target-guard-max-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath,
                """
                {
                  "items": [
                    { "enabled": true },
                    { "enabled": true }
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
                        { "op": "set", "path": "items[*].enabled", "value": false, "maxTargets": 1 }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("expected at most 1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ModelTransform_TargetGuards_EnforceExactMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-model-transform-target-guard-exact-" + Guid.NewGuid().ToString("N"));
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
                    { "enabled": true }
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
                        { "op": "set", "path": "items[*].enabled", "value": false, "exactTargets": 2 }
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
            Assert.Equal(2, reportDoc.RootElement.GetProperty("Operations")[0].GetProperty("TargetsApplied").GetInt32());
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
