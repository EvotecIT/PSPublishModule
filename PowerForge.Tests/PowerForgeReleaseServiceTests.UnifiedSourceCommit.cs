using System.IO.Compression;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData("tool")]
    [InlineData("module")]
    [InlineData("package")]
    [InlineData("module-package")]
    public void PublishBuiltReleaseOutputs_UnifiedReleaseUsesSerializedBuildSource(string lane)
    {
        var root = CreateSandbox();
        try
        {
            const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
            var asset = Path.Combine(root, "built.zip");
            File.WriteAllText(asset, "built artifact");
            var built = UnifiedSourceCheckpoint(asset);
            switch (lane)
            {
                case "tool": built.ToolSourceCommitSha = sourceCommit; break;
                case "module": built.ModuleSourceCommitSha = sourceCommit; break;
                case "package":
                    built.Packages = new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = new DotNetRepositoryReleaseResult { Success = true, SourceCommitSha = sourceCommit }
                        }
                    };
                    break;
                case "module-package":
                    built.ModulePackagePlans = [new PowerForgeModulePackageReleaseCheckpoint
                    {
                        Release = new DotNetRepositoryReleaseResult { Success = true, SourceCommitSha = sourceCommit }
                    }];
                    break;
            }
            built = JsonSerializer.Deserialize<PowerForgeReleaseResult>(JsonSerializer.Serialize(built))!;
            var published = new List<GitHubReleasePublishRequest>();
            var result = UnifiedSourceCheckpointService(published).PublishBuiltReleaseOutputs(
                UnifiedSourceSpec(), new PowerForgeReleaseRequest { ConfigPath = Path.Combine(root, "release.json") }, built);

            Assert.True(result.Success, result.ErrorMessage);
            var request = Assert.Single(published);
            Assert.Equal(sourceCommit, request.Commitish);
            Assert.Equal(sourceCommit, request.ExpectedTagCommitSha);
            Assert.Equal("v1.2.3", request.TagName);
            Assert.Equal("built artifact", File.ReadAllText(asset));
        }
        finally { DeleteToolSourceSandbox(root); }
    }

    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishBuiltReleaseOutputs_UnifiedMixedSourcesRequireExplicitTagSource(bool explicitTagSource)
    {
        var root = CreateSandbox();
        try
        {
            var asset = Path.Combine(root, "built.zip");
            File.WriteAllText(asset, "built artifact");
            var built = UnifiedSourceCheckpoint(asset);
            built.ToolSourceCommitSha = new string('a', 40);
            built.ModuleSourceCommitSha = new string('b', 40);
            var spec = UnifiedSourceSpec();
            if (explicitTagSource)
                spec.GitHub!.Commitish = new string('a', 40);
            var published = new List<GitHubReleasePublishRequest>();

            var result = UnifiedSourceCheckpointService(published).PublishBuiltReleaseOutputs(
                spec, new PowerForgeReleaseRequest { ConfigPath = Path.Combine(root, "release.json") }, built);

            Assert.Equal(explicitTagSource, result.Success);
            if (explicitTagSource)
            {
                var request = Assert.Single(published);
                Assert.Equal(new string('a', 40), request.Commitish);
                Assert.Equal(new string('a', 40), request.ExpectedTagCommitSha);
            }
            else
            {
                Assert.Empty(published);
                Assert.Contains("different source commits", result.ErrorMessage);
                Assert.Contains("GitHub.Commitish", result.ErrorMessage);
            }
            Assert.Equal("built artifact", File.ReadAllText(asset));
        }
        finally { DeleteToolSourceSandbox(root); }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Execute_UnifiedModuleReleaseCapturesSourceBeforeModuleBuildMovesHead()
    {
        var root = CreateSandbox();
        try
        {
            var script = Path.Combine(root, "Build-Module.ps1");
            File.WriteAllText(script, "# module build fixture");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "config", "commit.gpgsign", "false");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "module build source");
            var sourceCommit = RunSnapshotGit(root, "rev-parse", "HEAD").Trim();
            var asset = Path.Combine(root, "Company.Tools.1.2.3.zip");
            var published = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools must not plan."),
                runTools: _ => throw new InvalidOperationException("Tools must not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools must not load."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools must not plan."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools must not run."),
                executeModuleBuild: (_, _) =>
                {
                    File.WriteAllText(script, "# later source");
                    RunSnapshotGit(root, "add", "Build-Module.ps1");
                    RunSnapshotGit(root, "commit", "--quiet", "-m", "branch moved during module build");
                    using var archive = ZipFile.Open(asset, ZipArchiveMode.Create);
                    using var writer = new StreamWriter(archive.CreateEntry("Company.Tools/Company.Tools.psm1").Open());
                    writer.Write("# module built from captured source");
                    return new ModuleBuildHostExecutionResult { ExitCode = 0 };
                },
                publishGitHubRelease: request =>
                {
                    published.Add(request);
                    return new GitHubReleasePublishResult { Succeeded = true };
                });
            var spec = UnifiedSourceSpec();
            spec.Module = new PowerForgeModuleReleaseOptions
            {
                RepositoryRoot = root,
                ScriptPath = script,
                ModuleName = "Company.Tools",
                ModuleVersion = "1.2.3",
                ArtifactPaths = [asset]
            };

            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "release.json"), ModuleRunMode = ConfigurationGateMode.Publish
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotEqual(sourceCommit, RunSnapshotGit(root, "rev-parse", "HEAD").Trim());
            Assert.Equal(sourceCommit, result.ModuleSourceCommitSha);
            var request = Assert.Single(published);
            Assert.Equal(sourceCommit, request.Commitish);
            Assert.Equal(sourceCommit, request.ExpectedTagCommitSha);
        }
        finally { DeleteToolSourceSandbox(root); }
    }

    private static PowerForgeReleaseResult UnifiedSourceCheckpoint(string asset) => new()
    {
        Success = true,
        ReleaseAssets = [asset],
        ReleaseAssetEntries = [new PowerForgeReleaseAssetEntry { Path = asset, Version = "1.2.3" }]
    };

    private static PowerForgeReleaseSpec UnifiedSourceSpec() => new()
    {
        GitHub = new PowerForgeReleaseGitHubOptions
        {
            Publish = true, VersionSource = PowerForgeReleaseVersionSource.Assets,
            Owner = "EvotecIT", Repository = "Sample", TokenEnvName = "PATH"
        }
    };

    private static PowerForgeReleaseService UnifiedSourceCheckpointService(List<GitHubReleasePublishRequest> published) => new(
        new NullLogger(),
        executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
        planTools: (_, _, _) => throw new InvalidOperationException("Tools must not plan."),
        runTools: _ => throw new InvalidOperationException("Tools must not run."),
        publishGitHubRelease: request =>
        {
            published.Add(request);
            return new GitHubReleasePublishResult { Succeeded = true };
        });
}
