using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_PerTargetToolReleasePinsCommitCapturedBeforeBuildMovesHead(bool dotNetTools)
    {
        var root = CreateSandbox();
        try
        {
            File.WriteAllText(Path.Combine(root, "source.txt"), "built source");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "config", "commit.gpgsign", "false");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "built source");
            var builtCommit = RunSnapshotGit(root, "rev-parse", "HEAD").Trim();
            var archive = Path.Combine(root, "tool.zip");
            GitHubReleasePublishRequest? captured = null;
            void CompleteBuild()
            {
                File.WriteAllText(Path.Combine(root, "source.txt"), "later source");
                RunSnapshotGit(root, "add", "source.txt");
                RunSnapshotGit(root, "commit", "--quiet", "-m", "branch moved during build");
                File.WriteAllText(archive, "built tool");
            }
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => new PowerForgeToolReleasePlan { ProjectRoot = root },
                runTools: _ =>
                {
                    CompleteBuild();
                    return ToolSourceLegacyResult(archive);
                },
                loadDotNetToolsSpec: (_, path) => (new DotNetPublishSpec(), path),
                planDotNetTools: (_, _, _, _) => ToolSourceDotNetPlan(root),
                runDotNetTools: _ =>
                {
                    CompleteBuild();
                    return ToolSourceDotNetResult(archive);
                },
                publishGitHubRelease: request =>
                {
                    captured = request;
                    return new GitHubReleasePublishResult { Succeeded = true };
                });
            var spec = ToolSourceSpec(dotNetTools);

            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "release.json"),
                ToolsOnly = true
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotEqual(builtCommit, RunSnapshotGit(root, "rev-parse", "HEAD").Trim());
            Assert.Equal(builtCommit, result.ToolSourceCommitSha);
            Assert.NotNull(captured);
            Assert.Equal(builtCommit, captured!.Commitish);
            Assert.Equal(builtCommit, captured.ExpectedTagCommitSha);
            Assert.Equal("Tool-v1.2.3", captured.TagName);
            Assert.Contains(archive, captured.AssetFilePaths);
        }
        finally { DeleteToolSourceSandbox(root); }
    }

    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(null, "0123456789abcdef0123456789abcdef01234567", "0123456789abcdef0123456789abcdef01234567", 0)]
    [InlineData("release-branch", "release-branch", null, 0)]
    [InlineData("abcdef0123456789abcdef0123456789abcdef01", "abcdef0123456789abcdef0123456789abcdef01", "abcdef0123456789abcdef0123456789abcdef01", 0)]
    [InlineData(null, "0123456789abcdef0123456789abcdef01234567", "0123456789abcdef0123456789abcdef01234567", 1)]
    [InlineData(null, "0123456789abcdef0123456789abcdef01234567", "0123456789abcdef0123456789abcdef01234567", 2)]
    public void PublishBuiltReleaseOutputs_PerTargetToolReleaseRetainsCheckpointAndExplicitOverride(
        string? configuredCommitish, string expectedCommitish, string? expectedTagCommit, int checkpointSource)
    {
        var root = CreateSandbox();
        try
        {
            var archive = Path.Combine(root, "tool.zip");
            File.WriteAllText(archive, "built tool");
            var built = new PowerForgeReleaseResult
            {
                Success = true,
                ToolSourceCommitSha = "0123456789abcdef0123456789abcdef01234567",
                DotNetToolPlan = ToolSourceDotNetPlan(root),
                DotNetTools = ToolSourceDotNetResult(archive)
            };
            if (checkpointSource != 0)
            {
                // Existing DotNet checkpoints already retain source identity without the new ordinary-tool field.
                built.ToolSourceCommitSha = null;
                if (checkpointSource == 1)
                    built.DotNetSourceCommitSha = "0123456789abcdef0123456789abcdef01234567";
                else
                    built.DotNetToolPlan.SourceRevision = "0123456789abcdef0123456789abcdef01234567";
            }
            // Deferred publication must use serialized build identity, not inspect a later checkout.
            var checkpoint = JsonSerializer.Deserialize<PowerForgeReleaseResult>(JsonSerializer.Serialize(built))!;
            GitHubReleasePublishRequest? captured = null;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools must not replan."),
                runTools: _ => throw new InvalidOperationException("Tools must not rebuild."),
                publishGitHubRelease: request =>
                {
                    captured = request;
                    return new GitHubReleasePublishResult { Succeeded = true };
                });
            var spec = ToolSourceSpec(true);
            spec.GitHub = new PowerForgeReleaseGitHubOptions { Commitish = configuredCommitish };

            var result = service.PublishBuiltReleaseOutputs(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "release.json"),
                ToolsOnly = true,
                PublishToolGitHub = true
            }, checkpoint);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(captured);
            Assert.Equal(expectedCommitish, captured!.Commitish);
            Assert.Equal(expectedTagCommit, captured.ExpectedTagCommitSha);
        }
        finally { DeleteToolSourceSandbox(root); }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Execute_PerTargetToolReleasePreservesNonGitBuilds()
    {
        var root = CreateSandbox();
        try
        {
            var archive = Path.Combine(root, "tool.zip");
            File.WriteAllText(archive, "built tool");
            GitHubReleasePublishRequest? captured = null;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => new PowerForgeToolReleasePlan { ProjectRoot = root },
                runTools: _ => ToolSourceLegacyResult(archive),
                publishGitHubRelease: request =>
                {
                    captured = request;
                    return new GitHubReleasePublishResult { Succeeded = true };
                });
            var result = service.Execute(ToolSourceSpec(false), new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "release.json"), ToolsOnly = true
            });
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Null(result.ToolSourceCommitSha);
            Assert.NotNull(captured);
            Assert.Null(captured!.Commitish);
            Assert.Null(captured.ExpectedTagCommitSha);
        }
        finally { DeleteToolSourceSandbox(root); }
    }

    private static void DeleteToolSourceSandbox(string root)
    {
        var objects = Path.Combine(root, ".git", "objects");
        if (Directory.Exists(objects))
            foreach (var path in Directory.EnumerateFiles(objects, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private static PowerForgeReleaseSpec ToolSourceSpec(bool dotNetTools) => new()
    {
        Tools = new PowerForgeToolReleaseSpec
        {
            DotNetPublish = dotNetTools ? new DotNetPublishSpec() : null,
            GitHub = new PowerForgeToolReleaseGitHubOptions
            {
                Publish = true, Owner = "EvotecIT", Repository = "PSPublishModule", TokenEnvName = "PATH"
            }
        }
    };

    private static PowerForgeToolReleaseResult ToolSourceLegacyResult(string archive) => new()
    {
        Success = true,
        Artefacts = [new PowerForgeToolReleaseArtifactResult { Target = "Tool", Version = "1.2.3", ZipPath = archive }]
    };

    private static DotNetPublishPlan ToolSourceDotNetPlan(string root) => new()
    {
        UseControlledSourceProvenance = true,
        ProjectRoot = root,
        Targets = [new DotNetPublishTargetPlan
        {
            Name = "Tool", Version = "1.2.3", Publish = new DotNetPublishPublishOptions { Zip = true }
        }]
    };

    private static DotNetPublishResult ToolSourceDotNetResult(string archive) => new()
    {
        Succeeded = true,
        Artefacts = [new DotNetPublishArtefactResult
        {
            Target = "Tool", Runtime = "win-x64", Framework = "net10.0",
            Style = DotNetPublishStyle.PortableCompat, ZipPath = archive
        }]
    };
}
