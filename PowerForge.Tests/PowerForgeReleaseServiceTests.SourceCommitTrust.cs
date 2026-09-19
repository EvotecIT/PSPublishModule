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
