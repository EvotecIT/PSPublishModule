using System;
using System.Diagnostics;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public class WebPipelineRunnerSourcesSyncTests
{
    [Fact]
    public void RunPipeline_SourcesSync_ClonesRepoFromSiteSpec()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-sources-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "hello from source");
            RunGit(source, "init");
            RunGit(source, "config", "user.email", "pf-tests@example.local");
            RunGit(source, "config", "user.name", "PowerForge Tests");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "init", "--quiet");

            Directory.CreateDirectory(Path.Combine(root, "content", "pages"));
            File.WriteAllText(Path.Combine(root, "content", "pages", "index.md"),
                """
                ---
                title: Home
                slug: /
                ---

                # Home
                """);

            File.WriteAllText(Path.Combine(root, "site.json"),
                $$"""
                {
                  "Name": "Pipeline Sources Sync Test",
                  "BaseUrl": "https://example.test",
                  "ContentRoot": "content",
                  "ProjectsRoot": "projects",
                  "Collections": [
                    { "Name": "pages", "Input": "content/pages", "Output": "/" }
                  ],
                  "Sources": [
                    { "Repo": "{{EscapeJson(source)}}", "Slug": "demo-project" }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    { "task": "sources-sync", "config": "./site.json" }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            Assert.Equal("hello from source", File.ReadAllText(Path.Combine(root, "projects", "demo-project", "README.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static bool IsGitAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}{Environment.NewLine}{stdout}");
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

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

