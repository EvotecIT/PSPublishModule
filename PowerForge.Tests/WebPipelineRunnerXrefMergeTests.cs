using System;
using System.IO;
using System.Linq;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerXrefMergeTests
{
    [Fact]
    public void RunPipeline_XrefMerge_WritesMergedMap()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-xref-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var first = Path.Combine(root, "first.json");
            var second = Path.Combine(root, "second.json");
            File.WriteAllText(first, """{"references":[{"uid":"docs.install","href":"/docs/install/"}]}""");
            File.WriteAllText(second, """{"references":[{"uid":"System.String","href":"/api/types/system-string.json"}]}""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "xref-merge",
                      "out": "./_temp/xrefmap.json",
                      "mapFiles": [ "./first.json", "./second.json" ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("Xref merge", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var output = Path.Combine(root, "_temp", "xrefmap.json");
            Assert.True(File.Exists(output));
            var json = File.ReadAllText(output);
            Assert.Contains("docs.install", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("System.String", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_XrefMerge_FailsOnWarningsWhenConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-xref-merge-warn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var valid = Path.Combine(root, "valid.json");
            File.WriteAllText(valid, """{"references":[{"uid":"docs.install","href":"/docs/install/"}]}""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "xref-merge",
                      "out": "./_temp/xrefmap.json",
                      "mapFiles": [ "./valid.json", "./missing.json" ],
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("input path was not found", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_XrefMerge_FailsWhenDuplicateBudgetExceeded()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-xref-merge-max-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var first = Path.Combine(root, "first.json");
            var second = Path.Combine(root, "second.json");
            var third = Path.Combine(root, "third.json");
            File.WriteAllText(first, """{"references":[{"uid":"sample.uid","href":"/a/"}]}""");
            File.WriteAllText(second, """{"references":[{"uid":"sample.uid","href":"/b/"}]}""");
            File.WriteAllText(third, """{"references":[{"uid":"sample.uid","href":"/c/"}]}""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "xref-merge",
                      "out": "./_temp/xrefmap.json",
                      "mapFiles": [ "./first.json", "./second.json", "./third.json" ],
                      "maxDuplicates": 1,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("maxDuplicates", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_XrefMerge_FailsWhenGrowthBudgetExceeded()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-xref-merge-max-growth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var tempDir = Path.Combine(root, "_temp");
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "xrefmap.json"), """{"references":[{"uid":"docs.old","href":"/docs/old/"}]}""");

            var first = Path.Combine(root, "first.json");
            var second = Path.Combine(root, "second.json");
            var third = Path.Combine(root, "third.json");
            File.WriteAllText(first, """{"references":[{"uid":"docs.one","href":"/docs/one/"}]}""");
            File.WriteAllText(second, """{"references":[{"uid":"docs.two","href":"/docs/two/"}]}""");
            File.WriteAllText(third, """{"references":[{"uid":"docs.three","href":"/docs/three/"}]}""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "xref-merge",
                      "out": "./_temp/xrefmap.json",
                      "mapFiles": [ "./first.json", "./second.json", "./third.json" ],
                      "maxReferenceGrowthCount": 1,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("maxReferenceGrowthCount", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
