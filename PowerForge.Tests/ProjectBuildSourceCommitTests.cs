using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildSourceCommitTests
{
    [Theory]
    [InlineData("Single")]
    [InlineData("PerProject")]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Publish_UsesBuiltCommitAfterBranchAdvancesAndResultIsRestored(string mode)
    {
        using var source = new SourceRepository();
        var builtCommit = source.Commit("built source");
        var asset = Path.Combine(source.Root, "release.zip");
        File.WriteAllText(asset, "built artifact");
        var built = new DotNetRepositoryReleaseResult
        {
            SourceCommitSha = ReleaseSourceCommit.Capture(source.Root),
            ResolvedVersion = "1.2.3",
            Projects = [new DotNetRepositoryProjectResult
            {
                ProjectName = "Example", IsPackable = true, NewVersion = "1.2.3",
                ReleaseZipPath = asset
            }]
        };
        var restored = JsonSerializer.Deserialize<DotNetRepositoryReleaseResult>(JsonSerializer.Serialize(built))!;
        var newerCommit = source.Commit("branch advances while publishing");
        Assert.NotEqual(builtCommit, newerCommit);
        GitHubReleasePublishRequest? observed = null;
        var publisher = new ProjectBuildGitHubPublisher(new NullLogger(), request =>
        {
            observed = request;
            return new GitHubReleasePublishResult { Succeeded = true, ReleaseId = 42 };
        });

        var summary = publisher.Publish(new ProjectBuildGitHubPublishRequest
        {
            Owner = "Example", Repository = "Example", Token = "test-only",
            ReleaseMode = mode, Release = restored
        });

        Assert.True(summary.Success, summary.ErrorMessage);
        Assert.NotNull(observed);
        Assert.Equal(builtCommit, observed.Commitish);
        Assert.Equal(builtCommit, observed.ExpectedTagCommitSha);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Execute_CapturesCommitBeforeSupportedVersionUpdatesWithoutRequiringCleanSource()
    {
        using var source = new SourceRepository();
        var project = Path.Combine(source.Root, "Example.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
              <TargetFramework>net8.0</TargetFramework><VersionPrefix>1.0.0</VersionPrefix>
            </PropertyGroup></Project>
            """);
        var commit = source.Commit("version one");
        File.WriteAllText(Path.Combine(source.Root, "notes.txt"), "ordinary uncommitted input");

        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
        {
            RootPath = source.Root, Pack = false, Publish = false,
            UpdateVersions = true, ExpectedVersion = "1.2.3"
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(commit, result.SourceCommitSha);
        Assert.Contains("1.2.3", File.ReadAllText(project));
        Assert.Equal(commit, ReleaseSourceCommit.Capture(source.Root));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Capture_NonGitInputRemainsSupportedAndNestedCheckoutUsesItsOwnHead()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-source-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Assert.Null(ReleaseSourceCommit.Capture(root));
            using var source = new SourceRepository();
            var commit = source.Commit("source");
            var nested = Directory.CreateDirectory(Path.Combine(source.Root, "nested")).FullName;
            Assert.Equal(commit, ReleaseSourceCommit.Capture(nested));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Capture_NewCheckoutHasNoCommitButBrokenCommittedRefIsRejected()
    {
        using var source = new SourceRepository();
        Assert.Null(ReleaseSourceCommit.Capture(source.Root));
        source.Commit("source");
        var head = File.ReadAllText(Path.Combine(source.Root, ".git", "HEAD")).Trim();
        var branch = head.Substring("ref: ".Length);
        File.WriteAllText(Path.Combine(source.Root, ".git", branch.Replace('/', Path.DirectorySeparatorChar)), new string('f', 40));
        Assert.Throws<InvalidOperationException>(() => ReleaseSourceCommit.Capture(source.Root));
    }

    private sealed class SourceRepository : IDisposable
    {
        internal string Root { get; } = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "pf-source-" + Guid.NewGuid().ToString("N"))).FullName;

        internal SourceRepository()
        {
            Git("init");
            Git("config", "user.name", "Release test");
            Git("config", "user.email", "release@example.test");
            Git("config", "commit.gpgsign", "false");
        }

        internal string Commit(string message)
        {
            File.WriteAllText(Path.Combine(Root, "input.txt"), message);
            Git("add", ".");
            Git("-c", "core.hooksPath=", "commit", "-m", message);
            return Git("rev-parse", "HEAD").Trim();
        }

        private string Git(params string[] arguments)
        {
            var result = GitClient.CreateIsolatedPathClient().RunRawAsync(Root, arguments).GetAwaiter().GetResult();
            Assert.Equal(0, result.ExitCode);
            return result.StdOut;
        }

        public void Dispose()
        {
            // Git marks loose objects read-only on Windows. These are exclusively
            // this disposable fixture's objects, never a user's source checkout.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }
}
