namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Run_BindsGitHubToExecutionSourceWhileAllowingDirtyAndNonGitBuilds(bool gitSource, bool unified)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var staging = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            if (!gitSource)
                Directory.Delete(Path.Combine(root.FullName, ".git"), recursive: true);
            GitHubReleasePublishRequest? captured = null;
            var runner = CreateRunner(new FakeHostedOperations(new List<string>()),
                (_, _, _) => throw new Xunit.Sdk.XunitException("No package build was configured."),
                request =>
                {
                    captured = request;
                    return new GitHubReleasePublishResult { Succeeded = true, ReleaseCreationSucceeded = true, ReleaseId = 42 };
                });
            var publishes = new PublishConfiguration
            {
                Enabled = true,
                Destination = PublishDestination.GitHub,
                UserName = "EvotecIT",
                RepositoryName = moduleName,
                ApiKey = "fixture-token"
            };
            var segments = new List<IConfigurationSegment>
            {
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration { Enabled = true, Path = Path.Combine(root.FullName, "Artifacts", "Module") }
                },
                new ConfigurationPublishSegment { Configuration = publishes }
            };
            if (unified)
                segments.Add(new ConfigurationReleaseSegment { Configuration = new ReleaseConfiguration() });
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.1", StagingPath = staging },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            };
            var precomputed = runner.Plan(spec);
            if (gitSource)
                InitializeModuleSourceGit(root.FullName);
            var sourceCommit = ReleaseSourceCommit.Capture(root.FullName);
            File.WriteAllText(Path.Combine(root.FullName, "working-tree-input.txt"), "Intentional local input");

            var result = runner.Run(spec, precomputed);
            Assert.Equal(sourceCommit, result.Plan.SourceCommitSha);
            Assert.Equal("1.0.1", result.Plan.ResolvedVersion);
            Assert.Contains("1.0.1", File.ReadAllText(Path.Combine(root.FullName, moduleName + ".psd1")), StringComparison.Ordinal);
            if (gitSource)
                RunModuleSourceGit(root.FullName, "commit", "--quiet", "--allow-empty", "-m", "Advance after build");
            if (!unified)
                captured = ModulePublisher.CreateGitHubReleaseRequest(publishes, result.Plan, Array.Empty<string>());
            Assert.NotNull(captured);
            Assert.Equal(sourceCommit, captured!.Commitish);
            Assert.Equal(sourceCommit, captured.ExpectedTagCommitSha);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
            DeleteModuleSourceFixture(root);
        }
    }

    private static void DeleteModuleSourceFixture(DirectoryInfo root)
    {
        if (!Directory.Exists(root.FullName))
            return;
        var objects = Path.Combine(root.FullName, ".git", "objects");
        if (Directory.Exists(objects))
        {
            // Git writes read-only objects on Windows. These files belong only to
            // this test's temporary repository and must not prevent fixture cleanup.
            foreach (var path in Directory.EnumerateFiles(objects, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
        root.Delete(recursive: true);
    }

    private static void InitializeModuleSourceGit(string root)
    {
        RunModuleSourceGit(root, "init", "--quiet");
        RunModuleSourceGit(root, "config", "user.name", "PowerForge Tests");
        RunModuleSourceGit(root, "config", "user.email", "powerforge-tests@example.invalid");
        RunModuleSourceGit(root, "add", ".");
        RunModuleSourceGit(root, "commit", "--quiet", "-m", "Module source");
    }

    private static void RunModuleSourceGit(string root, params string[] arguments)
    {
        var safeArguments = new[] { "-c", "commit.gpgsign=false", "-c", "core.hooksPath=" + (OperatingSystem.IsWindows() ? "NUL" : "/dev/null") }
            .Concat(arguments).ToArray();
        var result = new GitClient().RunRawAsync(root, safeArguments).GetAwaiter().GetResult();
        Assert.True(result.Succeeded, result.StdErr);
    }
}
