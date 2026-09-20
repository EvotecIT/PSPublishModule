using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("current-files")]
    public void VerifySharedReleaseSourceCommit_without_exact_opt_in_does_not_require_git(string? configuredCommit)
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "not-a-git-checkout");

        var result = PowerForgeReleaseService.VerifySharedReleaseSourceCommit(missingPath, configuredCommit);

        Assert.Null(result);
    }

    [Fact]
    public void VerifySharedReleaseSourceCommit_ResolvesHeadFromCleanCheckout()
    {
        var root = CreateSandbox();
        try
        {
            File.WriteAllText(Path.Combine(root, "source.cs"), "internal sealed class Source { }");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "exact source");
            var commit = RunSnapshotGit(root, "rev-parse", "HEAD").Trim();

            Assert.Equal(commit, PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, "HEAD"));
            File.WriteAllText(Path.Combine(root, "Injected.cs"), "internal sealed class Injected { }");
            Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, "HEAD"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void VerifySharedReleaseSourceCommit_RejectsChangesOutsideNestedConfigDirectory()
    {
        var root = CreateSandbox();
        try
        {
            var configDirectory = Path.Combine(root, "Build");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(root, "source.cs"), "internal sealed class Source { }");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "exact source");

            Assert.NotNull(PowerForgeReleaseService.VerifySharedReleaseSourceCommit(configDirectory, "HEAD"));
            File.WriteAllText(Path.Combine(root, "source.cs"), "internal sealed class Changed { }");
            Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.VerifySharedReleaseSourceCommit(configDirectory, "HEAD"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BindBuiltDotNetSourceCommit_RecoversExactHeadAcrossSerializedCheckpoint()
    {
        var root = CreateSandbox();
        try
        {
            File.WriteAllText(Path.Combine(root, "source.cs"), "internal sealed class Source { }");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "exact source");
            var sha = RunSnapshotGit(root, "rev-parse", "HEAD").Trim().ToLowerInvariant();
            var checkpoint = new PowerForgeReleaseResult
            {
                DotNetSourceCommitSha = sha,
                DotNetToolPlan = new DotNetPublishPlan { ProjectRoot = root, SourceRevision = sha }
            };
            var restored = JsonSerializer.Deserialize<PowerForgeReleaseResult>(JsonSerializer.Serialize(checkpoint))!;
            var spec = new PowerForgeReleaseSpec { GitHub = new PowerForgeReleaseGitHubOptions { Commitish = "HEAD" } };

            PowerForgeReleaseService.BindBuiltDotNetSourceCommit(spec, restored, Path.Combine(root, "release.json"));
            Assert.Equal(sha, spec.GitHub.Commitish);

            var missing = new PowerForgeReleaseSpec { GitHub = new PowerForgeReleaseGitHubOptions { Commitish = "HEAD" } };
            Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.BindBuiltDotNetSourceCommit(missing,
                    new PowerForgeReleaseResult { DotNetToolPlan = restored.DotNetToolPlan },
                    Path.Combine(root, "release.json")));
            var changedPlan = new PowerForgeReleaseSpec { GitHub = new PowerForgeReleaseGitHubOptions { Commitish = "HEAD" } };
            restored.DotNetToolPlan!.SourceRevision = new string('f', 40);
            Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.BindBuiltDotNetSourceCommit(changedPlan, restored,
                    Path.Combine(root, "release.json")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveDotNetSourceRootForPreflight_UsesProjectCheckoutWhenConfigIsExternal()
    {
        var root = CreateSandbox();
        var configRoot = CreateSandbox();
        try
        {
            File.WriteAllText(Path.Combine(root, "source.cs"), "internal sealed class Source { }");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "source");
            var configPath = Path.Combine(configRoot, "publish.json");
            var spec = new DotNetPublishSpec { DotNet = new DotNetPublishDotNetOptions { ProjectRoot = root } };
            var projectRoot = PowerForgeReleaseService.ResolveDotNetSourceRootForPreflight(spec, configPath);

            Assert.Equal(Path.GetFullPath(root), projectRoot);
            Assert.NotNull(PowerForgeReleaseService.VerifySharedReleaseSourceCommit(projectRoot, "HEAD", configPath));
            File.WriteAllText(Path.Combine(root, "source.cs"), "internal sealed class Modified { }");
            Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.VerifySharedReleaseSourceCommit(projectRoot, "HEAD", configPath));
        }
        finally
        {
            TryDelete(root);
            TryDelete(configRoot);
        }
    }

    [Fact]
    public void VerifySharedReleaseSourceCommit_accepts_only_validated_public_release_inputs()
    {
        var root = CreatePublicReleaseSourceSandbox(out var commit, out var configPath, out _, out _, out var evidenceRoot);
        try
        {
            var verified = PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, commit, configPath);

            Assert.Equal(commit, verified);

            File.WriteAllText(Path.Combine(root, "Injected.cs"), "internal sealed class Injected { }");
            var error = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, commit, configPath));
            Assert.Contains("clean", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
            TryDelete(evidenceRoot);
        }
    }

    [Fact]
    public void VerifySharedReleaseSourceCommit_accepts_authorized_external_config_with_head_marker()
    {
        var root = CreatePublicReleaseSourceSandbox(out var commit, out var configPath, out _, out _, out var evidenceRoot);
        try
        {
            var exactConfig = File.ReadAllText(configPath);
            var headConfig = exactConfig.Replace($"\"Commitish\":\"{commit}\"", "\"Commitish\":\"HEAD\"");
            Assert.NotEqual(exactConfig, headConfig);
            File.WriteAllText(configPath, headConfig);

            Assert.Equal(commit, PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, "HEAD", configPath));
            Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, commit, configPath));
        }
        finally
        {
            TryDelete(root);
            TryDelete(evidenceRoot);
        }
    }

    [Fact]
    public void ValidateHeadSourceSelection_RejectsPublishingWithoutSelectedDotNetCheckout()
    {
        var spec = new PowerForgeReleaseSpec { GitHub = new PowerForgeReleaseGitHubOptions { Commitish = "HEAD", Publish = true } };
        Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ValidateHeadSourceSelection(spec,
                willRunTools: false, publishUnifiedGitHub: true, dotNetSpec: null, dotNetConfigPath: null));
        PowerForgeReleaseService.ValidateHeadSourceSelection(spec,
            willRunTools: true, publishUnifiedGitHub: true,
            dotNetSpec: new DotNetPublishSpec(), dotNetConfigPath: "publish.json");
    }

    [Fact]
    public void ValidateDraftWingetSubmission_RejectsIncompatibleReleaseBeforePublication()
    {
        var spec = new PowerForgeReleaseSpec
        {
            GitHub = new PowerForgeReleaseGitHubOptions { IsDraft = true },
            Winget = new PowerForgeReleaseWingetOptions { Submit = true }
        };
        Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ValidateDraftWingetSubmission(spec, new PowerForgeReleaseRequest()));
        PowerForgeReleaseService.ValidateDraftWingetSubmission(spec,
            new PowerForgeReleaseRequest { SubmitWinget = false });
        spec.Winget!.Submit = false;
        spec.Winget.Submission.Enabled = true;
        Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ValidateDraftWingetSubmission(spec, new PowerForgeReleaseRequest()));
        spec.Winget = null;
        Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ValidateDraftWingetSubmission(spec,
                new PowerForgeReleaseRequest { SubmitWinget = true }));
    }

    [Fact]
    public void VerifySharedReleaseSourceCommit_rejects_public_release_inputs_without_the_authorized_config_path()
    {
        var root = CreatePublicReleaseSourceSandbox(out var commit, out _, out _, out _, out var evidenceRoot);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, commit));

            Assert.Contains("clean", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
            TryDelete(evidenceRoot);
        }
    }

    [Fact]
    public void VerifySharedReleaseSourceCommit_rejects_forged_public_release_provenance()
    {
        var root = CreatePublicReleaseSourceSandbox(out var commit, out var configPath, out var provenancePath, out _, out var evidenceRoot);
        try
        {
            var forged = new Dictionary<string, object?>
            {
                ["schemaVersion"] = 1,
                ["moduleName"] = "PSPublishModule",
                ["version"] = "3.0.110",
                ["repository"] = "https://github.com/EvotecIT/PSPublishModule",
                ["commit"] = new string('f', 40)
            };
            File.WriteAllText(provenancePath, JsonSerializer.Serialize(forged));

            var error = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, commit, configPath));

            Assert.Contains("commit", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
            TryDelete(evidenceRoot);
        }
    }

    [Theory]
    [InlineData("Build/release.authorized.3.0.110.json")]
    [InlineData("Build/.release.authorized.name.json")]
    [InlineData(".release.authorized.3.0.110.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json")]
    public void VerifySharedReleaseSourceCommit_rejects_untrusted_authorization_config_locations(string relativeConfigPath)
    {
        var root = CreatePublicReleaseSourceSandbox(out var commit, out var validConfigPath, out _, out _, out var evidenceRoot);
        try
        {
            var replacement = Path.Combine(root, relativeConfigPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(replacement)!);
            File.Copy(validConfigPath, replacement, overwrite: true);

            var error = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, commit, replacement));

            Assert.Contains("clean", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
            TryDelete(evidenceRoot);
        }
    }

    [Fact]
    public void VerifySharedReleaseSourceCommit_rejects_forged_signed_public_release_provenance()
    {
        var root = CreatePublicReleaseSourceSandbox(out var commit, out var configPath, out _, out var signedProvenancePath, out var evidenceRoot);
        try
        {
            File.WriteAllText(signedProvenancePath, "@{ SchemaVersion = '1'; ModuleName = 'PSPublishModule'; Version = '3.0.110'; SourceRevision = '" + new string('f', 40) + "'; SourceDirty = 'false' }");

            var error = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, commit, configPath));

            Assert.Contains("signed", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
            TryDelete(evidenceRoot);
        }
    }

    private static string CreatePublicReleaseSourceSandbox(
        out string commit,
        out string configPath,
        out string provenancePath,
        out string signedProvenancePath,
        out string evidenceRoot)
    {
        var root = CreateSandbox();
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        Directory.CreateDirectory(Path.Combine(root, "Module"));
        File.WriteAllText(Path.Combine(root, "source.cs"), "internal sealed class Source { }");
        RunSnapshotGit(root, "init", "--quiet");
        RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
        RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
        RunSnapshotGit(root, "add", ".");
        RunSnapshotGit(root, "commit", "--quiet", "-m", "exact source");
        commit = RunSnapshotGit(root, "rev-parse", "HEAD").Trim().ToLowerInvariant();

        evidenceRoot = CreateSandbox();
        configPath = Path.Combine(evidenceRoot, $".release.authorized.3.0.110.{commit}.json");
        var config = new
        {
            GitHub = new
            {
                Owner = "EvotecIT",
                Repository = "PSPublishModule",
                Commitish = commit
            },
            Module = new
            {
                ModuleName = "PSPublishModule",
                ModuleVersion = "3.0.110"
            }
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(config));

        provenancePath = Path.Combine(root, "Module", "PowerForge.ReleaseProvenance.json");
        var provenance = new
        {
            schemaVersion = 1,
            moduleName = "PSPublishModule",
            version = "3.0.110",
            repository = "https://github.com/EvotecIT/PSPublishModule",
            commit,
            sourceDirty = false
        };
        File.WriteAllText(provenancePath, JsonSerializer.Serialize(provenance));
        signedProvenancePath = Path.Combine(root, "Module", PowerForgeModuleSourceAttestationWriter.FileName);
        File.WriteAllText(
            signedProvenancePath,
            $"@{{ SchemaVersion = '1'; ModuleName = 'PSPublishModule'; Version = '3.0.110'; SourceRevision = '{commit}'; SourceDirty = 'false' }}");
        return root;
    }
}
