using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    public void Run_SignedScriptArtefactKeepsDefaultReleaseMetadataOutsideFinalizedLayout()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string outputRoot = Path.Combine(root.FullName, "Artefacts");
            var hostedOperations = new FakeHostedOperations { AutoSuccessfulSigningResult = true };
            GitHubReleasePublishRequest? publishRequest = null;
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations,
                gitHubReleasePublisher: request =>
                {
                    publishRequest = request;
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        AllAssetUploadsSucceeded = true,
                        HtmlUrl = "https://example.invalid/release"
                    };
                });
            ModulePipelineSpec spec = CreateSignedPackedSpec(root.FullName, moduleName, outputRoot);
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            artefact.ArtefactType = ArtefactType.Script;
            artefact.Configuration!.ScriptName = "Invoke-TestModule.ps1";
            spec.UnifiedGitHubRelease = true;
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationReleaseSegment
                {
                    Configuration = new ReleaseConfiguration()
                },
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Enabled = true,
                        Destination = PublishDestination.GitHub,
                        UserName = "EvotecIT",
                        RepositoryName = moduleName,
                        ApiKey = "test-token"
                    }
                }
            }).ToArray();

            ModulePipelineResult result = runner.Run(spec, runner.Plan(spec));

            Assert.NotNull(publishRequest);
            Assert.NotNull(result.ReleaseCoordinationResult);
            string finalizedRoot = Path.GetFullPath(Assert.Single(result.ArtefactResults).OutputPath);
            string[] metadata = result.ReleaseCoordinationResult!.AssetPaths
                .Where(path => Path.GetFileName(path) is "release-manifest.json" or "SHA256SUMS.txt")
                .ToArray();
            Assert.Equal(2, metadata.Length);
            Assert.All(metadata, path => Assert.Contains(path, publishRequest!.AssetFilePaths));
            Assert.All(metadata, path =>
            {
                Assert.True(File.Exists(path));
                Assert.False(
                    Path.GetFullPath(path).StartsWith(
                        finalizedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase));
            });
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
