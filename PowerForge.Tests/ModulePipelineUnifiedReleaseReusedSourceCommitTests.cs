namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_OrdinaryReusedStagingRetainsPayloadWithoutInferringGitHubSource(bool unified)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var sourceRoot = Path.Combine(root.FullName, "source");
        var staging = Path.Combine(root.FullName, "staging");
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(sourceRoot, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(sourceRoot, "retained.txt"), "Original staged payload");
            InitializeModuleSourceGit(sourceRoot);
            var originalCommit = ReleaseSourceCommit.Capture(sourceRoot);
            Assert.NotNull(originalCommit);
            var requests = new List<GitHubReleasePublishRequest>();
            var hosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishAction = (publish, plan) =>
                    requests.Add(ModulePublisher.CreateGitHubReleaseRequest(publish, plan, Array.Empty<string>()))
            };
            var runner = CreateRunner(hosted,
                (_, _, _) => throw new Xunit.Sdk.XunitException("No package build was configured."),
                request =>
                {
                    requests.Add(request);
                    return new GitHubReleasePublishResult { Succeeded = true, ReleaseCreationSucceeded = true, ReleaseId = 42 };
                });
            var segments = new List<IConfigurationSegment>
            {
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration { Enabled = true, Path = Path.Combine(root.FullName, "artifacts") }
                },
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Enabled = true, Destination = PublishDestination.GitHub,
                        UserName = "EvotecIT", RepositoryName = moduleName, ApiKey = "fixture-token"
                    }
                }
            };
            if (unified)
                segments.Add(new ConfigurationReleaseSegment { Configuration = new ReleaseConfiguration() });
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName, SourcePath = sourceRoot, Version = "1.0.0", StagingPath = staging, KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            };

            var built = runner.Run(spec);
            Assert.Equal(originalCommit, built.Plan.SourceCommitSha);
            var firstRequest = Assert.Single(requests);
            Assert.Equal(originalCommit, firstRequest.Commitish);
            Assert.Equal(originalCommit, firstRequest.ExpectedTagCommitSha);
            File.WriteAllText(Path.Combine(sourceRoot, "retained.txt"), "New checkout payload");
            File.WriteAllText(Path.Combine(sourceRoot, "source-only.txt"), "Not part of existing staging");
            RunModuleSourceGit(sourceRoot, "add", "retained.txt", "source-only.txt");
            RunModuleSourceGit(sourceRoot, "commit", "--quiet", "-m", "Advance authored inputs after staging");
            Assert.NotEqual(originalCommit, ReleaseSourceCommit.Capture(sourceRoot));
            spec.Build.ReuseStaging = true;
            spec.Build.SkipDotNetBuild = true;

            var reused = runner.Run(spec);
            Assert.Equal("Original staged payload", File.ReadAllText(Path.Combine(staging, "retained.txt")));
            Assert.False(File.Exists(Path.Combine(staging, "source-only.txt")));
            Assert.Null(reused.Plan.SourceCommitSha);
            Assert.Equal("1.0.0", reused.Plan.ResolvedVersion);
            Assert.Equal(2, requests.Count);
            Assert.Null(requests[1].Commitish);
            Assert.Null(requests[1].ExpectedTagCommitSha);
        }
        finally
        {
            DeleteModuleSourceFixture(new DirectoryInfo(sourceRoot));
            if (Directory.Exists(root.FullName)) root.Delete(recursive: true);
        }
    }
}
