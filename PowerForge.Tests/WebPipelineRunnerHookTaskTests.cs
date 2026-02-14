using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerHookTaskTests
{
    [Fact]
    public void RunPipeline_HookTask_WritesContextAndStreams()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-hook-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contextPath = Path.Combine(root, "_reports", "hooks", "pre-build.context.json");
            var stdoutPath = Path.Combine(root, "_reports", "hooks", "pre-build.stdout.log");
            var stderrPath = Path.Combine(root, "_reports", "hooks", "pre-build.stderr.log");
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "hook",
                      "id": "pre-build-hook",
                      "event": "pre-build",
                      "command": "dotnet",
                      "args": "--version",
                      "contextPath": "./_reports/hooks/pre-build.context.json",
                      "stdoutPath": "./_reports/hooks/pre-build.stdout.log",
                      "stderrPath": "./_reports/hooks/pre-build.stderr.log",
                      "env": {
                        "PF_TEST_FLAG": "enabled"
                      }
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("hook ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("context=", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(contextPath));
            Assert.True(File.Exists(stdoutPath));
            Assert.True(File.Exists(stderrPath));

            var stdout = File.ReadAllText(stdoutPath);
            Assert.False(string.IsNullOrWhiteSpace(stdout));

            var contextJson = File.ReadAllText(contextPath);
            using var contextDoc = JsonDocument.Parse(contextJson);
            var contextRoot = contextDoc.RootElement;
            Assert.Equal("pre-build", contextRoot.GetProperty("Event").GetString());
            Assert.Equal("default", contextRoot.GetProperty("Mode").GetString());
            Assert.Equal("pre-build-hook", contextRoot.GetProperty("StepId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(contextRoot.GetProperty("Utc").GetString()));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_HookTask_CanAllowFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-hook-allow-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "hook",
                      "event": "pre-build",
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
    public void RunPipeline_HookTask_RequiresEvent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-hook-event-required-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "hook",
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
            Assert.Contains("hook requires event", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
