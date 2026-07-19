using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge.Tests;

public sealed class DotNetPublishMsiGitAuthorityTests
{
    [Fact]
    public void JsonContract_DeserializesGitTagAuthorityOptions()
    {
        const string json = """
            {
              "Enabled": true,
              "Monotonic": true,
              "StatePath": "Build/versioning/app.json",
              "Authority": "GitTags",
              "AuthorityKey": "syncse",
              "GitRemote": "origin",
              "GitTagPrefix": "powerforge-msi"
            }
            """;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());

        var versioning = JsonSerializer.Deserialize<DotNetPublishMsiVersionOptions>(json, options);

        Assert.NotNull(versioning);
        Assert.Equal(DotNetPublishMsiVersionAuthorityKind.GitTags, versioning!.Authority);
        Assert.Equal("syncse", versioning.AuthorityKey);
        Assert.Equal("origin", versioning.GitRemote);
        Assert.Equal("powerforge-msi", versioning.GitTagPrefix);
    }

    [Fact]
    public async Task SeparateClones_CannotReserveTheSameMsiVersion_AndDoNotTransferSourceCommits()
    {
        var root = CreateTempRoot();
        try
        {
            var remote = Path.Combine(root, "authority.git");
            var seed = Path.Combine(root, "seed");
            var cloneA = Path.Combine(root, "clone-a");
            var cloneB = Path.Combine(root, "clone-b");
            InitializeRepository(remote, seed);
            RunGit(root, "clone", remote, cloneA);
            RunGit(root, "clone", remote, cloneB);

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var planA = runner.Plan(CreateSpec(cloneA), Path.Combine(cloneA, "powerforge.json"));
            var planB = runner.Plan(CreateSpec(cloneB), Path.Combine(cloneB, "powerforge.json"));
            var versionA = Assert.Single(planA.MsiVersions.Values);
            var versionB = Assert.Single(planB.MsiVersions.Values);
            Assert.Equal("26.6.9677", versionA.Version);
            Assert.Equal(versionA.Version, versionB.Version);

            File.WriteAllText(Path.Combine(cloneA, "local-only-secret.txt"), "must not reach the remote");
            RunGit(cloneA, "config", "user.name", "PowerForge Tests");
            RunGit(cloneA, "config", "user.email", "powerforge-tests@invalid.local");
            RunGit(cloneA, "add", "local-only-secret.txt");
            RunGit(cloneA, "commit", "-m", "local only source commit");
            var localOnlySourceCommit = RunGit(cloneA, "rev-parse", "HEAD");

            using var start = new ManualResetEventSlim(initialState: false);
            Exception? failureA = null;
            Exception? failureB = null;
            var taskA = Task.Run(() =>
            {
                start.Wait();
                try
                {
                    DotNetPublishPipelineRunner.ReserveMsiVersionState(
                        versionA,
                        "publisher A",
                        "publisher-a");
                }
                catch (Exception ex)
                {
                    failureA = ex;
                }
            });
            var taskB = Task.Run(() =>
            {
                start.Wait();
                try
                {
                    DotNetPublishPipelineRunner.ReserveMsiVersionState(
                        versionB,
                        "publisher B",
                        "publisher-b");
                }
                catch (Exception ex)
                {
                    failureB = ex;
                }
            });
            start.Set();
            await Task.WhenAll(taskA, taskB);

            var collision = Assert.Single(new[] { failureA, failureB }.OfType<Exception>());
            Assert.Contains("already reserved", collision.Message, StringComparison.OrdinalIgnoreCase);
            if (failureA is null)
                Assert.True(DotNetPublishPipelineRunner.ReleaseMsiVersionStateReservation(versionA, "publisher-a"));
            if (failureB is null)
                Assert.True(DotNetPublishPipelineRunner.ReleaseMsiVersionStateReservation(versionB, "publisher-b"));

            var replanned = runner.Plan(CreateSpec(cloneB), Path.Combine(cloneB, "powerforge.json"));
            Assert.Equal("26.6.9678", Assert.Single(replanned.MsiVersions.Values).Version);
            var tags = RunGit(root, "ls-remote", "--refs", "--tags", remote, "refs/tags/powerforge-msi/syncse/*");
            Assert.Single(tags.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            Assert.Contains("refs/tags/powerforge-msi/syncse/26.6.9677", tags, StringComparison.Ordinal);
            Assert.NotEqual(0, RunGitExitCode(remote, "cat-file", "-e", localOnlySourceCommit + "^{commit}"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void GitTagAuthority_RejectsMutableOverwriteIdentity()
    {
        var root = CreateTempRoot();
        try
        {
            var remote = Path.Combine(root, "authority.git");
            var seed = Path.Combine(root, "seed");
            InitializeRepository(remote, seed);
            var spec = CreateSpec(seed);
            spec.Installers[0].Versioning!.AllowOutputOverwrite = true;

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new DotNetPublishPipelineRunner(new NullLogger()).Plan(
                    spec,
                    Path.Combine(seed, "powerforge.json")));

            Assert.Contains("Shared release identities are immutable", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Authority_RejectsMajorMinorRegression()
    {
        var root = CreateTempRoot();
        try
        {
            var remote = Path.Combine(root, "authority.git");
            var seed = Path.Combine(root, "seed");
            InitializeRepository(remote, seed);
            var spec = CreateSpec(seed);
            spec.Installers[0].Versioning!.Major = 25;

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new DotNetPublishPipelineRunner(new NullLogger()).Plan(
                    spec,
                    Path.Combine(seed, "powerforge.json")));

            Assert.Contains("instead of regressing", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(9677)]
    [InlineData(9000)]
    public void Authority_RejectsPatchCapThatCannotAdvance(int patchCap)
    {
        var root = CreateTempRoot();
        try
        {
            var remote = Path.Combine(root, "authority.git");
            var seed = Path.Combine(root, "seed");
            var clone = Path.Combine(root, "clone");
            InitializeRepository(remote, seed);
            RunGit(root, "clone", remote, clone);

            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var initial = runner.Plan(CreateSpec(seed), Path.Combine(seed, "powerforge.json"));
            var initialVersion = Assert.Single(initial.MsiVersions.Values);
            DotNetPublishPipelineRunner.ReserveMsiVersionState(initialVersion, "initial publisher", "initial-owner");
            Assert.True(DotNetPublishPipelineRunner.ReleaseMsiVersionStateReservation(initialVersion, "initial-owner"));

            var regressing = CreateSpec(clone);
            regressing.Installers[0].Versioning!.PatchCap = patchCap;
            var exception = Assert.Throws<InvalidOperationException>(() =>
                runner.Plan(regressing, Path.Combine(clone, "powerforge.json")));

            Assert.Contains("cannot advance", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Authority_RejectsCredentialBearingRemoteWithoutDisclosingTheSecret()
    {
        var root = CreateTempRoot();
        try
        {
            var remote = Path.Combine(root, "authority.git");
            var seed = Path.Combine(root, "seed");
            InitializeRepository(remote, seed);
            var spec = CreateSpec(seed);
            const string secret = "super-secret-value";
            spec.Installers[0].Versioning!.GitRemote = $"https://x-access-token:{secret}@example.invalid/repo.git";

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new DotNetPublishPipelineRunner(new NullLogger()).Plan(
                    spec,
                    Path.Combine(seed, "powerforge.json")));

            Assert.Contains("embedded credentials", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("x-access-token", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Authority_RejectsAuthorityKeyThatCannotFormAValidGitRef()
    {
        var root = CreateTempRoot();
        try
        {
            var remote = Path.Combine(root, "authority.git");
            var seed = Path.Combine(root, "seed");
            InitializeRepository(remote, seed);
            var spec = CreateSpec(seed);
            spec.Installers[0].Versioning!.AuthorityKey = "syncse..release";

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new DotNetPublishPipelineRunner(new NullLogger()).Plan(
                    spec,
                    Path.Combine(seed, "powerforge.json")));

            Assert.Contains("not a valid Git ref", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetPublishSpec CreateSpec(string projectRoot)
    {
        return new DotNetPublishSpec
        {
            DotNet = new DotNetPublishDotNetOptions
            {
                ProjectRoot = projectRoot,
                Configuration = "Release",
                Restore = false,
                Build = false,
                Runtimes = new[] { "win-x64" }
            },
            Targets = new[]
            {
                new DotNetPublishTarget
                {
                    Name = "app",
                    ProjectPath = "App.csproj",
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0",
                        Runtimes = new[] { "win-x64" },
                        Style = DotNetPublishStyle.PortableCompat
                    }
                }
            },
            Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "syncse",
                    PrepareFromTarget = "app",
                    InstallerProjectPath = "App.csproj",
                    Versioning = new DotNetPublishMsiVersionOptions
                    {
                        Enabled = true,
                        Major = 26,
                        Minor = 6,
                        FloorDateUtc = "2026-01-01",
                        Monotonic = true,
                        StatePath = "Build/versioning/app.msi.state.json",
                        Authority = DotNetPublishMsiVersionAuthorityKind.GitTags,
                        AuthorityKey = "syncse",
                        GitRemote = "origin",
                        GitTagPrefix = "powerforge-msi",
                        ApplyToPublish = true
                    }
                }
            }
        };
    }

    private static void InitializeRepository(string remote, string seed)
    {
        Directory.CreateDirectory(remote);
        RunGit(remote, "init", "--bare");
        Directory.CreateDirectory(seed);
        RunGit(seed, "init");
        RunGit(seed, "config", "user.name", "PowerForge Tests");
        RunGit(seed, "config", "user.email", "powerforge-tests@invalid.local");
        File.WriteAllText(
            Path.Combine(seed, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var stateDirectory = Path.Combine(seed, "Build", "versioning");
        Directory.CreateDirectory(stateDirectory);
        File.WriteAllText(
            Path.Combine(stateDirectory, "app.msi.state.json"),
            "{\"LastPatch\":9676,\"Version\":\"26.6.9676\"}");
        RunGit(seed, "add", ".");
        RunGit(seed, "commit", "-m", "seed");
        RunGit(seed, "branch", "-M", "main");
        RunGit(seed, "remote", "add", "origin", remote);
        RunGit(seed, "push", "-u", "origin", "main");
        RunGit(remote, "symbolic-ref", "HEAD", "refs/heads/main");
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
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
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");

        return output.Trim();
    }

    private static int RunGitExitCode(string workingDirectory, params string[] arguments)
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
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        _ = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.MsiGitAuthority.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }
}
