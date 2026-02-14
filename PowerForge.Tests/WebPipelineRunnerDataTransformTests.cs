using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerDataTransformTests
{
    [Fact]
    public void RunPipeline_DataTransform_StdoutMode_WritesOutputAndReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-data-transform-stdout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            var outputPath = Path.Combine(root, "_temp", "transformed.json");
            var reportPath = Path.Combine(root, "_reports", "data-transform.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath, """{ "hello": "world" }""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "data-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/transformed.json",
                      "command": "dotnet",
                      "args": "--version",
                      "inputMode": "stdin",
                      "writeMode": "stdout",
                      "reportPath": "./_reports/data-transform.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("data-transform ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(outputPath));
            Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(outputPath)));

            Assert.True(File.Exists(reportPath));
            using var reportDoc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var reportRoot = reportDoc.RootElement;
            Assert.Equal("stdin", reportRoot.GetProperty("Mode").GetString());
            Assert.Equal("stdout", reportRoot.GetProperty("WriteMode").GetString());
            Assert.Equal(0, reportRoot.GetProperty("ExitCode").GetInt32());
            Assert.True(reportRoot.GetProperty("Changed").GetBoolean());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_DataTransform_AllowFailure_ReportsAllowedFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-data-transform-allow-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var inputPath = Path.Combine(root, "_temp", "source.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath, """{ "hello": "world" }""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "data-transform",
                      "input": "./_temp/source.json",
                      "out": "./_temp/transformed.json",
                      "command": "dotnet",
                      "args": "not-a-real-command-xyz",
                      "allowFailure": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("allowed failure", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_DataTransform_RequiresInput()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-data-transform-input-required-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "data-transform",
                      "out": "./_temp/transformed.json",
                      "command": "dotnet",
                      "args": "--version"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("requires input", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
