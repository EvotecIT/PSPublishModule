namespace PowerForge.Tests;

/// <summary>
/// Contracts for saving release version changes to Git: only stamped files are committed,
/// the maintainer's own work is never touched, and nothing here can fail a release.
/// </summary>
public sealed class ReleaseVersionCommitTests
{
    [Fact]
    public void Release_CommitsAndPushesOnlyStampedFiles_AndLeavesOtherWorkAlone()
    {
        using var repo = GitFixture.Create();
        repo.Write("README.md", "# Sample\n\nWork in progress.\n");
        repo.Write("notes.txt", "untracked notes\n");
        repo.Write("Docs/Guide.md", "# Guide\n\nStaged edit.\n");
        repo.Git("add", "--", "Docs/Guide.md");
        // The maintainer's own version bump in a file the release does not touch.
        repo.Write("Module/Sample.psd1", Manifest("2.0.0"));

        var result = RunPackageRelease(repo, publishSucceeds: true, stamp: () =>
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            repo.Write("My Lib/My Lib.csproj", Csproj("1.0.1"));
            repo.Write("Module/Sample.psm1", "function Get-Sample { 'merged build output' }\n");
        });

        Assert.True(result.Success);
        var commit = Assert.IsType<ReleaseVersionCommitResult>(result.VersionCommit);
        Assert.Equal(ReleaseVersionCommitStatus.Pushed, commit.Status);
        Assert.Equal(new[] { "Lib/Lib.csproj", "My Lib/My Lib.csproj" }, commit.Files);
        Assert.Empty(commit.SkippedFiles);
        Assert.Equal(commit.CommitSha, repo.Git("rev-parse", "HEAD"));
        Assert.Equal(commit.CommitSha, repo.RemoteHead());
        Assert.Equal(new[] { "Lib/Lib.csproj", "My Lib/My Lib.csproj" }, repo.Git("-c", "core.quotePath=false", "show", "--name-only", "--format=", "HEAD").Split('\n'));
        Assert.Equal("Release Sample", repo.Git("log", "-1", "--format=%s"));

        var status = repo.Git("status", "--porcelain=v1", "--untracked-files=all").Split('\n');
        Assert.Contains(" M README.md", status);
        Assert.Contains(" M Module/Sample.psm1", status);
        Assert.Contains("M  Docs/Guide.md", status);
        Assert.Contains("?? notes.txt", status);
        Assert.Contains(" M Module/Sample.psd1", status);
        Assert.DoesNotContain(status, line => line.Contains("Lib.csproj"));
    }

    [Fact]
    public void Release_WithFailedPublish_CommitsNothingAndKeepsStampedFiles()
    {
        using var repo = GitFixture.Create();
        var head = repo.Git("rev-parse", "HEAD");

        var result = RunPackageRelease(repo, publishSucceeds: false, stamp: () => repo.Write("Lib/Lib.csproj", Csproj("1.0.1")));

        Assert.False(result.Success);
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Assert.Equal(head, repo.RemoteHead());
        Assert.Contains(" M Lib/Lib.csproj", repo.Git("status", "--porcelain=v1").Split('\n'));
        var commit = Assert.IsType<ReleaseVersionCommitResult>(result.VersionCommit);
        Assert.Equal(ReleaseVersionCommitStatus.Incomplete, commit.Status);
        Assert.Null(commit.CommitSha);
        Assert.Contains("did not finish", commit.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Release_ThatDoesNotPublish_NeverCommits(bool planOnly, bool publishNuget)
    {
        using var repo = GitFixture.Create();
        var head = repo.Git("rev-parse", "HEAD");

        var result = RunPackageRelease(
            repo,
            publishSucceeds: true,
            stamp: () => repo.Write("Lib/Lib.csproj", Csproj("1.0.1")),
            planOnly: planOnly,
            publishNuget: publishNuget);

        Assert.Null(result.VersionCommit);
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Assert.Contains(" M Lib/Lib.csproj", repo.Git("status", "--porcelain=v1").Split('\n'));
    }

    [Fact]
    public void Release_WithRejectedPush_KeepsLocalCommitWarnsAndStillSucceeds()
    {
        using var repo = GitFixture.Create();
        repo.PushUnrelatedRemoteCommit();
        // Like -WarningAction Stop: an ordinary warning would throw.
        var logger = new RecordingLogger { ThrowOnWarn = true };

        var result = RunPackageRelease(repo, publishSucceeds: true, stamp: () => repo.Write("Lib/Lib.csproj", Csproj("1.0.1")), logger: logger);

        Assert.True(result.Success);
        var commit = Assert.IsType<ReleaseVersionCommitResult>(result.VersionCommit);
        Assert.Equal(ReleaseVersionCommitStatus.Incomplete, commit.Status);
        Assert.Equal(commit.CommitSha, repo.Git("rev-parse", "HEAD"));
        Assert.NotEqual(commit.CommitSha, repo.RemoteHead());
        Assert.Equal($"git push --no-follow-tags origin {commit.CommitSha}:refs/heads/main", commit.FollowUpCommand);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains(commit.FollowUpCommand!, warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_WithoutUpstream_KeepsCommitLocalAndCreatesNoRemoteBranch()
    {
        using var repo = GitFixture.Create();
        repo.Git("switch", "--quiet", "-c", "feature/no-upstream");
        var logger = new RecordingLogger();

        using (var session = ReleaseVersionCommitSession.Begin(logger, repo.Root, ReleaseVersionCommitMode.Push, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            var commit = session.Complete(releaseSucceeded: true, "Sample", "1.0.1")!;

            Assert.Equal(ReleaseVersionCommitStatus.Incomplete, commit.Status);
            Assert.Equal(commit.CommitSha, repo.Git("rev-parse", "HEAD"));
            Assert.Contains("--set-upstream", commit.FollowUpCommand, StringComparison.Ordinal);
        }

        Assert.Empty(repo.Git("ls-remote", "--heads", "origin", "feature/no-upstream"));
        Assert.Contains("no upstream", Assert.Single(logger.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Session_SkipsStampedFileThatAlreadyHadLocalEdits_AndCommitsTheRest()
    {
        using var repo = GitFixture.Create();
        repo.Write("Lib/Lib.csproj", Csproj("1.0.0", extra: "    <Description>Draft description</Description>\n"));
        var logger = new RecordingLogger();

        using (var session = ReleaseVersionCommitSession.Begin(logger, repo.Root, ReleaseVersionCommitMode.Commit, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1", extra: "    <Description>Draft description</Description>\n"));
            repo.Write("Module/Sample.psd1", Manifest("1.0.1"));

            var commit = session.Complete(releaseSucceeded: true, "Sample", "1.0.1")!;

            Assert.Equal(ReleaseVersionCommitStatus.Committed, commit.Status);
            Assert.Equal(new[] { "Module/Sample.psd1" }, commit.Files);
            Assert.Equal(new[] { "Lib/Lib.csproj" }, commit.SkippedFiles);
        }

        Assert.Equal("Release Sample 1.0.1", repo.Git("log", "-1", "--format=%s"));
        Assert.Equal(new[] { "Module/Sample.psd1" }, repo.Git("show", "--name-only", "--format=", "HEAD").Split('\n'));
        Assert.Contains(" M Lib/Lib.csproj", repo.Git("status", "--porcelain=v1").Split('\n'));
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("Lib/Lib.csproj", warning, StringComparison.Ordinal);
        Assert.Contains("local edits", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_ResumedRelease_CommitsLeftoverStampsOnceAndNeverTwice()
    {
        using var repo = GitFixture.Create();
        var head = repo.Git("rev-parse", "HEAD");

        // First attempt stamps the version and then fails to publish.
        using (var failed = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Push, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            failed.Complete(releaseSucceeded: false, "Sample", "1.0.1");
        }

        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));

        // The resumed run stamps the same version again and succeeds.
        string? committed;
        using (var resumed = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Push, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            var commit = resumed.Complete(releaseSucceeded: true, "Sample", "1.0.1")!;
            Assert.Equal(ReleaseVersionCommitStatus.Pushed, commit.Status);
            Assert.Equal(new[] { "Lib/Lib.csproj" }, commit.Files);
            committed = commit.CommitSha;
        }

        // Resuming once more finds nothing left to save.
        using (var again = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Push, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            var commit = again.Complete(releaseSucceeded: true, "Sample", "1.0.1")!;
            Assert.Equal(ReleaseVersionCommitStatus.NothingToCommit, commit.Status);
        }

        Assert.Equal(committed, repo.Git("rev-parse", "HEAD"));
        Assert.Equal(head, repo.Git("rev-parse", "HEAD~1"));
        Assert.Equal(committed, repo.RemoteHead());
    }

    [Fact]
    public void Session_OnBranchSharingATagName_KeepsAStagedModeChange()
    {
        using var repo = GitFixture.Create();
        repo.Git("tag", "main");
        // As on Linux: the working tree keeps its mode, so only the index holds the staged change.
        repo.Git("config", "core.fileMode", "true");
        repo.Git("update-index", "--chmod=+x", "--", "Lib/Lib.csproj");

        using (var session = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Commit, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            var commit = session.Complete(releaseSucceeded: true, "Sample", "1.0.1")!;
            Assert.Equal(ReleaseVersionCommitStatus.Committed, commit.Status);
        }

        var committedBlob = repo.Git("rev-parse", "HEAD:Lib/Lib.csproj");
        var index = repo.Git("ls-files", "-s", "--", "Lib/Lib.csproj").Split(' ', '\t');
        Assert.Equal("100755", index[0]);
        Assert.Equal(committedBlob, index[1]);
        Assert.Equal("100644", repo.Git("ls-tree", "HEAD", "--", "Lib/Lib.csproj").Split(' ')[0]);
    }

    [Fact]
    public void Session_LeavesDottedNumbersThatAreNotReleaseVersions()
    {
        using var repo = GitFixture.Create();
        repo.Write("Docs/Guide.md", "# Guide\n\nMeasured with 2026.11.18 data.\n");
        repo.Git("commit", "--quiet", "-am", "Add measurement");

        using (var session = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Commit, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            repo.Write("Docs/Guide.md", "# Guide\n\nMeasured with 2026.11.19 data.\n");
            var commit = session.Complete(releaseSucceeded: true, "Sample", "1.0.1", new[] { "1.0.1" })!;
            Assert.Equal(new[] { "Lib/Lib.csproj" }, commit.Files);
        }

        Assert.Contains(" M Docs/Guide.md", repo.Git("status", "--porcelain=v1").Split('\n'));
    }

    [Fact]
    public void Session_IgnoresStampsRecordedByAnotherRelease()
    {
        using var repo = GitFixture.Create();
        var head = repo.Git("rev-parse", "HEAD");
        using (var failed = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Commit, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            failed.Complete(releaseSucceeded: false, "Sample A", "1.0.1");
        }

        using (var other = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Commit, publishing: true))
        {
            var commit = other.Complete(releaseSucceeded: true, "Sample B", "2.0.0")!;
            Assert.Equal(ReleaseVersionCommitStatus.NothingToCommit, commit.Status);
        }

        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Assert.Contains(" M Lib/Lib.csproj", repo.Git("status", "--porcelain=v1").Split('\n'));
    }

    [Fact]
    public void Session_CommitsAVersionPropertyTheReleaseInserted()
    {
        using var repo = GitFixture.Create();
        repo.Write("Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <VersionPrefix>1.0.0</VersionPrefix>\n  </PropertyGroup>\n</Project>\n");
        repo.Git("commit", "--quiet", "-am", "Project without PackageVersion");

        using var session = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Commit, publishing: true);
        // What the project version editor writes when it has to add PackageVersion.
        repo.Write("Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <PackageVersion>1.0.1</PackageVersion>\n    <VersionPrefix>1.0.1</VersionPrefix>\n  </PropertyGroup>\n</Project>\n");
        var commit = session.Complete(releaseSucceeded: true, "Sample", "1.0.1", new[] { "1.0.1" })!;

        Assert.Equal(ReleaseVersionCommitStatus.Committed, commit.Status);
        Assert.Equal(new[] { "Lib/Lib.csproj" }, commit.Files);
    }

    [Theory]
    [InlineData("1.0.1-rc.2", "rc.2", "rc.2", true)]
    [InlineData("1.0.1", "", "", true)]
    [InlineData("1.0.1-rc.2", "beta", "rc.2", false)]
    public void Session_TreatsTheReleasesPrereleaseLabelsAsStamps(string release, string suffix, string manifestLabel, bool committed)
    {
        using var repo = GitFixture.Create();
        repo.Write("Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <VersionPrefix>1.0.0</VersionPrefix>\n    <VersionSuffix>rc.1</VersionSuffix>\n  </PropertyGroup>\n</Project>\n");
        repo.Write("Module/Sample.psd1", "@{\n    ModuleVersion = '1.0.0'\n    PrivateData = @{ PSData = @{\n        Prerelease = 'rc1'\n    } }\n}\n");
        repo.Git("commit", "--quiet", "-am", "Prerelease projects");

        using var session = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Commit, publishing: true);
        repo.Write("Lib/Lib.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <VersionPrefix>1.0.1</VersionPrefix>\n    <VersionSuffix>{suffix}</VersionSuffix>\n  </PropertyGroup>\n</Project>\n");
        repo.Write("Module/Sample.psd1", $"@{{\n    ModuleVersion = '1.0.1'\n    PrivateData = @{{ PSData = @{{\n        Prerelease = '{manifestLabel}'\n    }} }}\n}}\n");
        var commit = session.Complete(releaseSucceeded: true, "Sample", release, new[] { release })!;

        Assert.Contains("Module/Sample.psd1", commit.Files);
        Assert.Equal(committed, commit.Files.Contains("Lib/Lib.csproj"));
    }

    [Fact]
    public void Session_AcceptsNumericLabelsAndCommentedPrereleaseLines()
    {
        using var repo = GitFixture.Create();
        repo.Write("Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <VersionPrefix>2.0.0</VersionPrefix>\n    <VersionSuffix>1.2.2</VersionSuffix>\n  </PropertyGroup>\n</Project>\n");
        repo.Write("Module/Sample.psd1", "@{\n    ModuleVersion = '2.0.0'\n    PrivateData = @{ PSData = @{\n        Prerelease = 'alpha' # channel\n    } }\n}\n");
        repo.Git("commit", "--quiet", "-am", "Numeric prerelease");

        using var session = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Commit, publishing: true);
        repo.Write("Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <VersionPrefix>2.0.0</VersionPrefix>\n    <VersionSuffix>1.2.3</VersionSuffix>\n  </PropertyGroup>\n</Project>\n");
        repo.Write("Module/Sample.psd1", "@{\n    ModuleVersion = '2.0.0'\n    PrivateData = @{ PSData = @{\n        Prerelease = '1.2.3' # channel\n    } }\n}\n");
        var commit = session.Complete(releaseSucceeded: true, "Sample", "2.0.0-1.2.3", new[] { "2.0.0-1.2.3" })!;

        Assert.Equal(new[] { "Lib/Lib.csproj", "Module/Sample.psd1" }, commit.Files);
    }

    [Fact]
    public void Session_KeepsEachReleasesRecordedStampsApart()
    {
        using var repo = GitFixture.Create();
        using (var releaseA = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Commit, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            releaseA.Complete(releaseSucceeded: false, "Sample A", "1.0.1");
        }

        using (var releaseB = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Commit, publishing: true))
        {
            repo.Write("My Lib/My Lib.csproj", Csproj("2.0.1"));
            releaseB.Complete(releaseSucceeded: false, "Sample B", "2.0.1");
        }

        using (var rerunA = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Commit, publishing: true))
        {
            var commit = rerunA.Complete(releaseSucceeded: true, "Sample A", "1.0.1")!;
            Assert.Equal(new[] { "Lib/Lib.csproj" }, commit.Files);
        }

        Assert.Equal("My Lib/My Lib.csproj", repo.Git("-c", "core.quotePath=false", "diff", "--name-only"));
    }

    [Fact]
    public void Session_TwoPhaseRelease_CommitsRecordedStampsAfterPublication()
    {
        using var repo = GitFixture.Create();
        var head = repo.Git("rev-parse", "HEAD");
        using (var build = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Push, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            var deferred = build.Complete(releaseSucceeded: true, "Sample", "1.0.1", deferUntilPublished: true)!;
            Assert.Equal(ReleaseVersionCommitStatus.Deferred, deferred.Status);
        }

        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));

        using (var publish = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Push, publishing: true))
        {
            var commit = publish.Complete(releaseSucceeded: true, "Sample", "1.0.1")!;
            Assert.Equal(ReleaseVersionCommitStatus.Pushed, commit.Status);
            Assert.Equal(new[] { "Lib/Lib.csproj" }, commit.Files);
            Assert.Equal(commit.CommitSha, repo.RemoteHead());
        }
    }

    [Fact]
    public void Session_WithCommitSigningEnabled_RequestsASignature()
    {
        using var repo = GitFixture.Create();
        var head = repo.Git("rev-parse", "HEAD");
        // A signer that cannot start proves a signature was requested, without touching real keys.
        repo.Git("config", "gpg.format", "openpgp");
        repo.Git("config", "commit.gpgSign", "true");
        repo.Git("config", "gpg.program", Path.Combine(repo.Root, "missing-signer.exe"));
        var logger = new RecordingLogger();

        using (var session = ReleaseVersionCommitSession.Begin(logger, repo.Root, ReleaseVersionCommitMode.Commit, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            var commit = session.Complete(releaseSucceeded: true, "Sample", "1.0.1")!;
            Assert.Equal(ReleaseVersionCommitStatus.Incomplete, commit.Status);
            Assert.Null(commit.CommitSha);
        }

        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Assert.Contains("git --literal-pathspecs commit", Assert.Single(logger.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Session_InsideAnotherRelease_LeavesTheCommitToTheOuterRelease()
    {
        using var repo = GitFixture.Create();
        var head = repo.Git("rev-parse", "HEAD");

        using (ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Off, publishing: true))
        using (var inner = ReleaseVersionCommitSession.Begin(new RecordingLogger(), repo.Root, ReleaseVersionCommitMode.Push, publishing: true))
        {
            repo.Write("Lib/Lib.csproj", Csproj("1.0.1"));
            Assert.Null(inner.Complete(releaseSucceeded: true, "Sample", "1.0.1"));
            Assert.Equal("1", ReleaseVersionCommitSession.AddOwnerEnvironment(null)?[ReleaseVersionCommitSession.OwnerEnvironmentVariable]);
        }

        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Assert.Null(ReleaseVersionCommitSession.AddOwnerEnvironment(null));
    }

    [Fact]
    public void Configuration_AcceptsCommitVersionChangesAsOneLine()
    {
        var release = PowerForgeReleaseService.LoadConfigurationContent(
            "{ \"CommitVersionChanges\": \"Push\", \"Packages\": { \"RootPath\": \".\" } }",
            "release.json");
        Assert.Equal(ReleaseVersionCommitMode.Push, release.CommitVersionChanges);
        Assert.Equal(ReleaseVersionCommitMode.Off, PowerForgeReleaseService.LoadConfigurationContent("{ }", "release.json").CommitVersionChanges);

        using var repo = GitFixture.Create();
        repo.Write("project.build.json", "{ \"CommitVersionChanges\": \"commit\" }");
        var project = new ProjectBuildSupportService(new RecordingLogger()).LoadConfig(Path.Combine(repo.Root, "project.build.json"));
        Assert.Equal(ReleaseVersionCommitMode.Commit, project.CommitVersionChanges);
    }

    private static PowerForgeReleaseResult RunPackageRelease(
        GitFixture repo,
        bool publishSucceeds,
        Action stamp,
        bool planOnly = false,
        bool publishNuget = true,
        RecordingLogger? logger = null)
    {
        var service = new PowerForgeReleaseService(
            logger ?? new RecordingLogger(),
            executePackages: (_, _, _) =>
            {
                stamp();
                return new ProjectBuildHostExecutionResult
                {
                    Success = publishSucceeds,
                    ErrorMessage = publishSucceeds ? null : "NuGet push failed.",
                    ConfigPath = "release.json",
                    PlanOutputPath = "plan.json",
                    Result = new ProjectBuildResult { Success = publishSucceeds }
                };
            },
            planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
            runTools: _ => throw new InvalidOperationException("Tools should not run."),
            publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

        return service.Execute(
            new PowerForgeReleaseSpec
            {
                CommitVersionChanges = ReleaseVersionCommitMode.Push,
                Packages = new ProjectBuildConfiguration
                {
                    RootPath = "..",
                    Configuration = "Release",
                    PublishNuget = publishNuget,
                    GitHubRepositoryName = "Sample"
                }
            },
            new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(repo.Root, "Build", "release.json"),
                PackagesOnly = true,
                PlanOnly = planOnly
            });
    }

    private static string Csproj(string version, string extra = "")
        => "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n" +
           $"    <VersionPrefix>{version}</VersionPrefix>\n    <PackageVersion>{version}</PackageVersion>\n" +
           extra +
           "  </PropertyGroup>\n</Project>\n";

    private static string Manifest(string version)
        => $"@{{\n    RootModule = 'Sample.psm1'\n    ModuleVersion = '{version}'\n}}\n";

    private sealed class GitFixture : IDisposable
    {
        private GitFixture(string root, string remote)
        {
            Root = root;
            Remote = remote;
        }

        internal string Root { get; }

        private string Remote { get; }

        private string Base => Path.GetDirectoryName(Root)!;

        internal static GitFixture Create()
        {
            var baseDirectory = Path.Combine(Path.GetTempPath(), "PowerForge.ReleaseVersionCommit", Guid.NewGuid().ToString("N"));
            var root = Path.Combine(baseDirectory, "work");
            var remote = Path.Combine(baseDirectory, "remote.git");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "Build"));
            var fixture = new GitFixture(root, remote);
            RunGit(baseDirectory, "init", "--bare", "--initial-branch=main", remote);
            RunGit(baseDirectory, "--git-dir", remote, "config", "core.hooksPath", Path.Combine(baseDirectory, "no-hooks"));
            fixture.Git("init", "--initial-branch=main");
            fixture.ConfigureIsolatedIdentity(root);
            fixture.Write("Lib/Lib.csproj", Csproj("1.0.0"));
            fixture.Write("My Lib/My Lib.csproj", Csproj("1.0.0"));
            fixture.Write("Module/Sample.psd1", Manifest("1.0.0"));
            fixture.Write("Module/Sample.psm1", "function Get-Sample { 'source' }\n");
            fixture.Write("README.md", "# Sample\n");
            fixture.Write("Docs/Guide.md", "# Guide\n");
            fixture.Write("Build/release.json", "{ }\n");
            fixture.Git("add", ".");
            fixture.Git("commit", "-m", "Initial commit");
            fixture.Git("remote", "add", "origin", remote);
            fixture.Git("push", "--quiet", "-u", "origin", "main");
            return fixture;
        }

        internal void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        internal string Git(params string[] arguments) => RunGit(Root, arguments);

        internal string RemoteHead() => RunGit(Base, "--git-dir", Remote, "rev-parse", "refs/heads/main");

        internal void PushUnrelatedRemoteCommit()
        {
            var other = Path.Combine(Base, "other");
            RunGit(Base, "clone", "--quiet", Remote, other);
            ConfigureIsolatedIdentity(other);
            File.WriteAllText(Path.Combine(other, "CHANGELOG.md"), "Someone else's change\n");
            RunGit(other, "add", ".");
            RunGit(other, "commit", "-m", "Concurrent change");
            RunGit(other, "push", "--quiet", "origin", "main");
        }

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Base, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Base, recursive: true);
            }
            catch
            {
                // Best effort cleanup of the temporary repositories.
            }
        }

        private void ConfigureIsolatedIdentity(string repository)
        {
            // Never touch the developer's signing keys or hooks from a test repository.
            RunGit(repository, "config", "user.name", "PowerForge Tests");
            RunGit(repository, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(repository, "config", "commit.gpgSign", "false");
            RunGit(repository, "config", "tag.gpgSign", "false");
            RunGit(repository, "config", "core.hooksPath", Path.Combine(Base, "no-hooks"));
            RunGit(repository, "config", "core.autocrlf", "false");
        }

        private static string RunGit(string workingDirectory, params string[] arguments)
        {
            var result = new GitClient(defaultTimeout: TimeSpan.FromSeconds(60))
                .RunRawAsync(workingDirectory, arguments, TimeSpan.FromSeconds(60))
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            if (!result.Succeeded)
                throw new InvalidOperationException($"git {string.Join(" ", arguments)} failed: {result.StdErr}");
            return result.StdOut.Replace("\r\n", "\n").TrimEnd();
        }
    }

    private sealed class RecordingLogger : ILogger, IAdvisoryWarningLogger
    {
        internal List<string> Warnings { get; } = new();

        internal bool ThrowOnWarn { get; set; }

        public bool IsVerbose => false;

        public void Info(string message) { }

        public void Success(string message) { }

        public void Warn(string message)
        {
            if (ThrowOnWarn)
                throw new InvalidOperationException("Warnings stop this run.");
            Warnings.Add(message);
        }

        public void WarnAdvisory(string message) => Warnings.Add(message);

        public void Error(string message) { }

        public void Verbose(string message) { }
    }
}
