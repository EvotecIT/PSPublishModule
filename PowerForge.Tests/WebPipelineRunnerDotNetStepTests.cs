using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerDotNetStepTests
{
    [Fact]
    public void RunPipeline_DotNetBuild_SkipsWhenProjectMissingAndEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-dotnet-build-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "dotnet-build",
                      "project": "./src/Missing/Missing.csproj",
                      "configuration": "Release",
                      "skipIfProjectMissing": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("dotnet build skipped", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("project path not found", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_DotNetPublish_SkipsWhenProjectMissingAndEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-dotnet-publish-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "dotnet-publish",
                      "project": "./src/Missing/Missing.csproj",
                      "out": "./Artifacts/publish",
                      "configuration": "Release",
                      "skipIfProjectMissing": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("dotnet publish skipped", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("project path not found", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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