using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerEngineLockTests
{
    [Fact]
    public void RunPipeline_EngineLock_Update_WritesLockAndArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-engine-lock-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "engine-lock",
                      "operation": "update",
                      "path": "./.powerforge/engine-lock.json",
                      "repository": "EvotecIT/PSPublishModule",
                      "ref": "0123456789abcdef",
                      "channel": "candidate",
                      "reportPath": "./_reports/engine-lock.json",
                      "summaryPath": "./_reports/engine-lock.md"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);

            var lockPath = Path.Combine(root, ".powerforge", "engine-lock.json");
            Assert.True(File.Exists(lockPath));
            using var lockDoc = JsonDocument.Parse(File.ReadAllText(lockPath));
            Assert.Equal("EvotecIT/PSPublishModule", lockDoc.RootElement.GetProperty("repository").GetString());
            Assert.Equal("0123456789abcdef", lockDoc.RootElement.GetProperty("ref").GetString());
            Assert.Equal("candidate", lockDoc.RootElement.GetProperty("channel").GetString());

            Assert.True(File.Exists(Path.Combine(root, "_reports", "engine-lock.json")));
            Assert.True(File.Exists(Path.Combine(root, "_reports", "engine-lock.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_EngineLock_Verify_FailsOnDrift()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-engine-lock-verify-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var lockPath = Path.Combine(root, ".powerforge", "engine-lock.json");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            File.WriteAllText(lockPath,
                """
                {
                  "repository": "EvotecIT/PSPublishModule",
                  "ref": "deadbeef",
                  "channel": "stable",
                  "updatedUtc": "2026-02-19T00:00:00.0000000+00:00"
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "engine-lock",
                      "operation": "verify",
                      "path": "./.powerforge/engine-lock.json",
                      "expectedRepository": "EvotecIT/PSPublishModule",
                      "expectedRef": "cafebabe",
                      "reportPath": "./_reports/engine-lock.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("drift", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, "_reports", "engine-lock.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_EngineLock_Verify_CanAllowDrift()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-engine-lock-verify-allow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var lockPath = Path.Combine(root, ".powerforge", "engine-lock.json");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            File.WriteAllText(lockPath,
                """
                {
                  "repository": "EvotecIT/PSPublishModule",
                  "ref": "deadbeef",
                  "channel": "stable",
                  "updatedUtc": "2026-02-19T00:00:00.0000000+00:00"
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "engine-lock",
                      "operation": "verify",
                      "path": "./.powerforge/engine-lock.json",
                      "expectedRef": "cafebabe",
                      "failOnDrift": false
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
            Assert.Contains("drift", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_EngineLock_Verify_RequireImmutableRef_FailsForNonShaRef()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-engine-lock-verify-immutable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var lockPath = Path.Combine(root, ".powerforge", "engine-lock.json");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            File.WriteAllText(lockPath,
                """
                {
                  "repository": "EvotecIT/PSPublishModule",
                  "ref": "main",
                  "channel": "stable",
                  "updatedUtc": "2026-02-19T00:00:00.0000000+00:00"
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "engine-lock",
                      "operation": "verify",
                      "path": "./.powerforge/engine-lock.json",
                      "requireImmutableRef": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("immutable", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
