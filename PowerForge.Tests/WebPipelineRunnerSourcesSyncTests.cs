using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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

    [Fact]
    public void RunPipeline_SourcesSync_CleanStepDefault_ReplacesExistingDestination()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-sources-sync-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "clean source");
            RunGit(source, "init");
            RunGit(source, "config", "user.email", "pf-tests@example.local");
            RunGit(source, "config", "user.name", "PowerForge Tests");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "init", "--quiet");

            var staleDestination = Path.Combine(root, "projects", "demo-project");
            Directory.CreateDirectory(staleDestination);
            File.WriteAllText(Path.Combine(staleDestination, "stale.txt"), "stale");

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
                  "Name": "Pipeline Sources Sync Clean Test",
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
                    { "task": "sources-sync", "config": "./site.json", "clean": true }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            Assert.Equal("clean source", File.ReadAllText(Path.Combine(staleDestination, "README.md")));
            Assert.False(File.Exists(Path.Combine(staleDestination, "stale.txt")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_SourcesSync_PassesStepDefaultsToGitSync()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-sources-sync-defaults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "defaults source");
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
                  "Name": "Pipeline Sources Sync Defaults Test",
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

            var manifestPath = Path.Combine(root, "_reports", "git-sync.json");
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "sources-sync",
                      "config": "./site.json",
                      "authType": "none",
                      "retry": 2,
                      "retryDelayMs": 25,
                      "writeManifest": true,
                      "manifestPath": "./_reports/git-sync.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.True(File.Exists(manifestPath));

            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var entry = manifest.RootElement.GetProperty("entries")[0];
            Assert.Equal("none", entry.GetProperty("authType").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_SourcesSync_FiltersSourcesWhenProjectsConfigured()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-sources-sync-filter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var alphaSource = CreateGitSource(root, "alpha-source", "README.md", "alpha source");
            var betaSource = CreateGitSource(root, "beta-source", "README.md", "beta source");

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
                  "Name": "Pipeline Sources Sync Filter Test",
                  "BaseUrl": "https://example.test",
                  "ContentRoot": "content",
                  "ProjectsRoot": "projects",
                  "Collections": [
                    { "Name": "pages", "Input": "content/pages", "Output": "/" }
                  ],
                  "Sources": [
                    { "Repo": "{{EscapeJson(alphaSource)}}", "Slug": "alpha" },
                    { "Repo": "{{EscapeJson(betaSource)}}", "Slug": "beta" }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    { "task": "sources-sync", "config": "./site.json", "projects": ["beta"] }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(root, "projects", "beta", "README.md")));
            Assert.False(Directory.Exists(Path.Combine(root, "projects", "alpha")));
            Assert.Equal("beta source", File.ReadAllText(Path.Combine(root, "projects", "beta", "README.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_SourcesSync_RejectsFilteredLockUpdate()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-sources-sync-filter-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
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
                """
                {
                  "Name": "Pipeline Sources Sync Filter Update Test",
                  "BaseUrl": "https://example.test",
                  "ContentRoot": "content",
                  "ProjectsRoot": "projects",
                  "Collections": [
                    { "Name": "pages", "Input": "content/pages", "Output": "/" }
                  ],
                  "Sources": [
                    { "Repo": "https://example.test/alpha.git", "Slug": "alpha" }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    { "task": "sources-sync", "config": "./site.json", "projects": "alpha", "lockMode": "update" }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Contains("filtered lock update", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateGitSource(string root, string name, string fileName, string content)
    {
        var source = Path.Combine(root, name);
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, fileName), content);
        RunGit(source, "init");
        RunGit(source, "config", "user.email", "pf-tests@example.local");
        RunGit(source, "config", "user.name", "PowerForge Tests");
        RunGit(source, "add", ".");
        RunGit(source, "commit", "-m", "init", "--quiet");
        return source;
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
