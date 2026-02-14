using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public class WebPipelineRunnerGitSyncTests
{
    [Fact]
    public void RunPipeline_GitSync_ClonesRepository()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-clone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "hello from source");

            RunGit(source, "init");
            RunGit(source, "config", "user.email", "pf-tests@example.local");
            RunGit(source, "config", "user.name", "PowerForge Tests");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "init", "--quiet");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("git-sync ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var readme = Path.Combine(checkout, "README.md");
            Assert.True(File.Exists(readme));
            Assert.Equal("hello from source", File.ReadAllText(readme));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_UpdatesExistingCheckout()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "v1");

            RunGit(source, "init");
            RunGit(source, "config", "user.email", "pf-tests@example.local");
            RunGit(source, "config", "user.name", "PowerForge Tests");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "v1", "--quiet");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}"
                    }
                  ]
                }
                """);

            var first = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(first.Success);
            Assert.Equal("v1", File.ReadAllText(Path.Combine(checkout, "README.md")));

            File.WriteAllText(Path.Combine(source, "README.md"), "v2");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "v2", "--quiet");

            var second = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(second.Success);
            Assert.Equal("v2", File.ReadAllText(Path.Combine(checkout, "README.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_ChecksOutSpecifiedRef()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-ref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "main");

            RunGit(source, "init");
            RunGit(source, "config", "user.email", "pf-tests@example.local");
            RunGit(source, "config", "user.name", "PowerForge Tests");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "main", "--quiet");
            RunGit(source, "checkout", "-b", "feature");
            File.WriteAllText(Path.Combine(source, "README.md"), "feature");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "feature", "--quiet");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "ref": "feature"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Equal("feature", File.ReadAllText(Path.Combine(checkout, "README.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_TreatsExistingRelativePathAsRepositoryPath()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-relative-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "src", "repo");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "relative-path");

            RunGit(source, "init");
            RunGit(source, "config", "user.email", "pf-tests@example.local");
            RunGit(source, "config", "user.name", "PowerForge Tests");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "init", "--quiet");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "src/repo",
                      "destination": "./checkout"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Equal("relative-path", File.ReadAllText(Path.Combine(checkout, "README.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_SupportsBatchRepos()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourceA = Path.Combine(root, "source-a");
            var sourceB = Path.Combine(root, "source-b");
            Directory.CreateDirectory(sourceA);
            Directory.CreateDirectory(sourceB);
            File.WriteAllText(Path.Combine(sourceA, "README.md"), "repo-a");
            File.WriteAllText(Path.Combine(sourceB, "README.md"), "repo-b");

            InitializeRepository(sourceA, "init-a");
            InitializeRepository(sourceB, "init-b");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repos": [
                        {
                          "repo": "{{EscapeJson(sourceA)}}",
                          "destination": "./checkout/a"
                        },
                        {
                          "repo": "{{EscapeJson(sourceB)}}",
                          "destination": "./checkout/b"
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
            Assert.Contains("synchronized 2 repositories", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            Assert.Equal("repo-a", File.ReadAllText(Path.Combine(root, "checkout", "a", "README.md")));
            Assert.Equal("repo-b", File.ReadAllText(Path.Combine(root, "checkout", "b", "README.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_InitializesSubmodules()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-submodules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var childRepo = Path.Combine(root, "child");
            var sourceRepo = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(childRepo);
            Directory.CreateDirectory(sourceRepo);

            File.WriteAllText(Path.Combine(childRepo, "CHILD.md"), "child-content");
            InitializeRepository(childRepo, "child-init");

            File.WriteAllText(Path.Combine(sourceRepo, "README.md"), "parent-content");
            InitializeRepository(sourceRepo, "parent-init");

            RunGit(sourceRepo, "-c", "protocol.file.allow=always", "submodule", "add", childRepo, "modules/child");
            RunGit(sourceRepo, "commit", "-am", "add-submodule", "--quiet");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(sourceRepo)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "submodules": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            Assert.True(File.Exists(Path.Combine(checkout, "modules", "child", "CHILD.md")));
            Assert.Equal("child-content", File.ReadAllText(Path.Combine(checkout, "modules", "child", "CHILD.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_WritesManifest_WhenRequested()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            var manifest = Path.Combine(root, "_reports", "git-sync.json");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "manifest-test");
            InitializeRepository(source, "manifest-init");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "writeManifest": true,
                      "manifestPath": "./_reports/git-sync.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("manifest=", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(manifest));

            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            Assert.True(document.RootElement.TryGetProperty("generatedAtUtc", out var generatedAt));
            Assert.False(string.IsNullOrWhiteSpace(generatedAt.GetString()));
            var entries = document.RootElement.GetProperty("entries");
            Assert.Equal(JsonValueKind.Array, entries.ValueKind);
            Assert.Single(entries.EnumerateArray());
            var entry = entries[0];
            Assert.Equal(source, entry.GetProperty("repoInput").GetString());
            Assert.Equal(Path.GetFullPath(checkout), entry.GetProperty("destination").GetString());
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("resolvedRef").GetString()));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_ResolvesShorthandAgainstRepoBaseUrlPath()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-repo-base-url-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var mirrorRoot = Path.Combine(root, "mirror");
            var owner = "evotec";
            var repoName = "engine-improvement";
            var bareRepo = Path.Combine(mirrorRoot, owner, repoName + ".git");
            var checkout = Path.Combine(root, "checkout");

            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "repo-base-url-path");
            InitializeRepository(source, "repo-base-url-init");
            CreateBareRepositoryMirror(source, bareRepo, root);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repoBaseUrl": "./mirror",
                      "repo": "evotec/engine-improvement",
                      "destination": "./checkout"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Equal("repo-base-url-path", File.ReadAllText(Path.Combine(checkout, "README.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_ResolvesNestedShorthandAgainstRepoBaseUrlPath()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-repo-base-url-nested-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var mirrorRoot = Path.Combine(root, "mirror");
            var bareRepo = Path.Combine(mirrorRoot, "team", "platform", "engine.git");
            var checkout = Path.Combine(root, "checkout");

            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "repo-base-url-nested");
            InitializeRepository(source, "repo-base-url-nested-init");
            CreateBareRepositoryMirror(source, bareRepo, root);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repoBaseUrl": "./mirror",
                      "repo": "team/platform/engine",
                      "destination": "./checkout"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Equal("repo-base-url-nested", File.ReadAllText(Path.Combine(checkout, "README.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_AuthTypeToken_FailsWhenTokenMissing()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-auth-token-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originalToken = Environment.GetEnvironmentVariable("PFWEB_TEST_MISSING_TOKEN");
        Environment.SetEnvironmentVariable("PFWEB_TEST_MISSING_TOKEN", null);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "auth-token");
            InitializeRepository(source, "auth-token-init");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "authType": "token",
                      "tokenEnv": "PFWEB_TEST_MISSING_TOKEN"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("requires token", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PFWEB_TEST_MISSING_TOKEN", originalToken);
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_InlineToken_EmitsSecurityWarningInStepMessage()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-inline-token-warning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "inline-token-warning");
            InitializeRepository(source, "inline-token-warning-init");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "token": "do-not-inline-secrets"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("warning=[PFWEB.GITSYNC.SECURITY]", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_TokenEnvOnly_DoesNotEmitInlineTokenWarning()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-token-env-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "token-env-only");
            InitializeRepository(source, "token-env-only-init");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "tokenEnv": "GITHUB_TOKEN"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.DoesNotContain("PFWEB.GITSYNC.SECURITY", result.Steps[0].Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_AuthTypeAliasAuthentication_DisablesAuthHeader()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-auth-alias-none-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            var manifest = Path.Combine(root, "_reports", "git-sync.json");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "auth-none");
            InitializeRepository(source, "auth-none-init");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "authentication": "none",
                      "token": "ignored-token",
                      "writeManifest": true,
                      "manifestPath": "./_reports/git-sync.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Equal("auth-none", File.ReadAllText(Path.Combine(checkout, "README.md")));
            Assert.True(File.Exists(manifest));

            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            var entries = document.RootElement.GetProperty("entries");
            Assert.Single(entries.EnumerateArray());
            Assert.Equal("none", entries[0].GetProperty("authType").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_AuthTypeSsh_ResolvesShorthandToSshRemote()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-auth-ssh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            var manifest = Path.Combine(root, "_reports", "git-sync.json");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "auth-ssh");
            InitializeRepository(source, "auth-ssh-init");

            RunGit(root, "clone", source, checkout);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repoBaseUrl": "ghe.example.com/scm",
                      "repo": "team/engine",
                      "destination": "./checkout",
                      "authType": "ssh",
                      "writeManifest": true,
                      "manifestPath": "./_reports/git-sync.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.True(File.Exists(manifest));

            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            var entry = document.RootElement.GetProperty("entries")[0];
            Assert.Equal("ssh", entry.GetProperty("authType").GetString());
            Assert.Equal("git@ghe.example.com:scm/team/engine.git", entry.GetProperty("repo").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_AuthTypeInvalid_Fails()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-auth-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "auth-invalid");
            InitializeRepository(source, "auth-invalid-init");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "authType": "kerberos"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("unsupported authType", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_LockModeUpdate_WritesCommitLock()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-lock-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            var lockPath = Path.Combine(root, "_reports", "git-sync-lock.json");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "lock-update");
            InitializeRepository(source, "lock-update-init");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "lockMode": "update",
                      "lockPath": "./_reports/git-sync-lock.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("mode=update", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(lockPath));

            using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
            var entries = document.RootElement.GetProperty("entries");
            Assert.Single(entries.EnumerateArray());
            var entry = entries[0];
            var lockedCommit = entry.GetProperty("commit").GetString();
            Assert.False(string.IsNullOrWhiteSpace(lockedCommit));
            Assert.Equal(Path.GetFullPath(checkout), entry.GetProperty("destination").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_GitSync_LockModeVerify_UsesLockedCommit()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-git-sync-lock-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var source = Path.Combine(root, "source");
            var checkout = Path.Combine(root, "checkout");
            var lockPath = Path.Combine(root, "_reports", "git-sync-lock.json");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "v1");
            InitializeRepository(source, "v1");

            var updatePipelinePath = Path.Combine(root, "pipeline-update.json");
            File.WriteAllText(updatePipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "lockMode": "update",
                      "lockPath": "./_reports/git-sync-lock.json"
                    }
                  ]
                }
                """);

            var updateResult = WebPipelineRunner.RunPipeline(updatePipelinePath, logger: null);
            Assert.True(updateResult.Success);

            File.WriteAllText(Path.Combine(source, "README.md"), "v2");
            RunGit(source, "add", ".");
            RunGit(source, "commit", "-m", "v2", "--quiet");

            var verifyPipelinePath = Path.Combine(root, "pipeline-verify.json");
            File.WriteAllText(verifyPipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "git-sync",
                      "repo": "{{EscapeJson(source)}}",
                      "destination": "{{EscapeJson(checkout)}}",
                      "lockMode": "verify",
                      "lockPath": "./_reports/git-sync-lock.json"
                    }
                  ]
                }
                """);

            var verifyResult = WebPipelineRunner.RunPipeline(verifyPipelinePath, logger: null);
            Assert.True(verifyResult.Success);
            Assert.Single(verifyResult.Steps);
            Assert.True(verifyResult.Steps[0].Success);
            Assert.Contains("mode=verify", verifyResult.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("v1", File.ReadAllText(Path.Combine(checkout, "README.md")));
            Assert.True(File.Exists(lockPath));
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
            var result = RunGit(Environment.CurrentDirectory, throwOnError: false, "--version");
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunGit(string workingDirectory, params string[] args)
        => RunGit(workingDirectory, throwOnError: true, args);

    private static (int ExitCode, string StdOut, string StdErr) RunGit(string workingDirectory, bool throwOnError, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git process.");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (throwOnError && process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed (exit {process.ExitCode}): {stdErr}");

        return (process.ExitCode, stdOut, stdErr);
    }

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\");

    private static void InitializeRepository(string path, string commitMessage)
    {
        RunGit(path, "init");
        RunGit(path, "config", "user.email", "pf-tests@example.local");
        RunGit(path, "config", "user.name", "PowerForge Tests");
        RunGit(path, "add", ".");
        RunGit(path, "commit", "-m", commitMessage, "--quiet");
    }

    private static void CreateBareRepositoryMirror(string sourcePath, string bareRepoPath, string workingDirectory)
    {
        var parent = Path.GetDirectoryName(bareRepoPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
        RunGit(workingDirectory, "clone", "--bare", sourcePath, bareRepoPath);
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
