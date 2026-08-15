using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void VerifySharedReleaseSourceCommit_accepts_only_validated_public_release_inputs()
    {
        var root = CreatePublicReleaseSourceSandbox(out var commit, out var configPath, out _);
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
        }
    }

    [Fact]
    public void VerifySharedReleaseSourceCommit_rejects_public_release_inputs_without_the_authorized_config_path()
    {
        var root = CreatePublicReleaseSourceSandbox(out var commit, out _, out _);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.VerifySharedReleaseSourceCommit(root, commit));

            Assert.Contains("clean", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void VerifySharedReleaseSourceCommit_rejects_forged_public_release_provenance()
    {
        var root = CreatePublicReleaseSourceSandbox(out var commit, out var configPath, out var provenancePath);
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
        }
    }

    [Theory]
    [InlineData("Build/release.authorized.123.json")]
    [InlineData("Build/.release.authorized.name.json")]
    [InlineData(".release.authorized.123.json")]
    public void VerifySharedReleaseSourceCommit_rejects_untrusted_authorization_config_locations(string relativeConfigPath)
    {
        var root = CreatePublicReleaseSourceSandbox(out var commit, out var validConfigPath, out _);
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
        }
    }

    private static string CreatePublicReleaseSourceSandbox(
        out string commit,
        out string configPath,
        out string provenancePath)
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

        configPath = Path.Combine(root, "Build", ".release.authorized.123.json");
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
            commit
        };
        File.WriteAllText(provenancePath, JsonSerializer.Serialize(provenance));
        return root;
    }
}
