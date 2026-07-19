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
    public void SeparateClones_CannotReserveTheSameMsiVersion_AndReplanAdvances()
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

            DotNetPublishPipelineRunner.ReserveMsiVersionState(
                versionA,
                "publisher A",
                "publisher-a");
            var collision = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.ReserveMsiVersionState(
                    versionB,
                    "publisher B",
                    "publisher-b"));
            Assert.Contains("already reserved", collision.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(DotNetPublishPipelineRunner.ReleaseMsiVersionStateReservation(versionA, "publisher-a"));

            var replanned = runner.Plan(CreateSpec(cloneB), Path.Combine(cloneB, "powerforge.json"));
            Assert.Equal("26.6.9678", Assert.Single(replanned.MsiVersions.Values).Version);
            var tags = RunGit(root, "ls-remote", "--refs", "--tags", remote, "refs/tags/powerforge-msi/syncse/*");
            Assert.Single(tags.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            Assert.Contains("refs/tags/powerforge-msi/syncse/26.6.9677", tags, StringComparison.Ordinal);
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
