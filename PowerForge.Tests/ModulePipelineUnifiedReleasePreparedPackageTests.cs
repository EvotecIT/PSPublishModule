using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("package")]
    [InlineData("archive")]
    [InlineData("module")]
    [InlineData("source")]
    [InlineData("resume-disabled")]
    public void Run_ReusesPreparedPackagesWithoutCallingTheBuilderAndRejectsChangedPayloads(string damage)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string name = "TestModule";
            const string version = "2.0.11";
            WriteMinimalModule(root, name, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root, "project.build.json", name, publishNuGet: false);
            string configPath = Path.Combine(root, "Build", "project.build.json");
            var configuration = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
            configuration["RootPath"] = "..";
            configuration["OutputPath"] = "Artifacts/NuGet";
            configuration["ReleaseZipOutputPath"] = "Artifacts/Archives";
            File.WriteAllText(configPath, configuration.ToJsonString());
            int builds = 0;
            int publishes = 0;
            byte[]? originalPackage = null;
            byte[]? originalArchive = null;

            ProjectBuildHostExecutionResult Build(ProjectBuildHostRequest request, ProjectBuildConfiguration? config, string? path)
            {
                builds++;
                Assert.Equal(1, builds);
                var result = CreateProjectBuildResult(root, name, version, Path.Combine(root, "Artifacts", "NuGet"), request, path);
                var project = Assert.Single(result.Result.Release!.Projects);
                string package = Assert.Single(project.Packages);
                File.Delete(package);
                using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(archive.CreateEntry(name + ".nuspec").Open()))
                    writer.Write($"<package><metadata><id>{name}</id><version>{version}</version><authors>Test</authors><description>Prepared payload</description></metadata></package>");
                project.ReleaseZipPath = Path.Combine(root, "Artifacts", "Archives", name + "-" + version + ".zip");
                result.ReleaseZipOutputPath = Path.GetDirectoryName(project.ReleaseZipPath);
                Directory.CreateDirectory(Path.GetDirectoryName(project.ReleaseZipPath)!);
                File.WriteAllText(project.ReleaseZipPath, "original timestamped release archive");
                originalPackage = File.ReadAllBytes(package);
                originalArchive = File.ReadAllBytes(project.ReleaseZipPath);
                return result;
            }

            GitHubReleasePublishResult Publish(GitHubReleasePublishRequest request)
            {
                publishes++;
                if (publishes == 1)
                    throw new InvalidOperationException("Simulated remote outage");
                Assert.Equal(originalPackage, File.ReadAllBytes(Assert.Single(request.AssetFilePaths, path => path.EndsWith(".nupkg", StringComparison.Ordinal))));
                return new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    ReleaseCreationSucceeded = true,
                    AllAssetUploadsSucceeded = true,
                    HtmlUrl = "https://github.com/EvotecIT/TestModule/releases/tag/v2.0.11"
                };
            }

            FakeHostedOperations Hosted() => new(new List<string>())
            {
                ModuleAction = (action, context) => CreateActionResult(action, context, succeeded: true)
            };
            var first = CreateRunner(Hosted(), Build, Publish);
            var failure = Assert.Throws<InvalidOperationException>(() => first.Run(CreateGalleryAndGitHubReleaseSpec(root, staging, name)));
            Assert.Contains("Simulated remote outage", failure.Message);
            string cache = Assert.Single(Directory.GetDirectories(GetCoordinatedReleaseCheckpointRoot(root), "*.payload"));
            string? corrupt = damage switch
            {
                "package" => Assert.Single(Directory.GetFiles(cache, "*.nupkg", SearchOption.AllDirectories)),
                "archive" => Assert.Single(Directory.GetFiles(Path.Combine(cache, "package-lane"), "*.zip", SearchOption.AllDirectories)),
                "module" => Path.Combine(cache, "module", name + ".psm1"),
                "source" => Path.Combine(root, name + ".psm1"),
                _ => null
            };
            if (corrupt is not null)
                File.AppendAllText(corrupt, "\n# changed bytes\n");
            var resumed = CreateGalleryAndGitHubReleaseSpec(root, staging + "-resumed", name);
            var recovery = Assert.Single(resumed.Segments.OfType<ConfigurationReleaseSegment>()).Configuration;
            recovery.ReusePreparedPackages = true;
            recovery.ResumeIncompleteRelease = damage != "resume-disabled";
            var second = CreateRunner(Hosted(), Build, Publish);
            if (damage == "none")
            {
                var result = second.Run(resumed);
                Assert.Equal(version, result.Plan.ResolvedVersion);
                Assert.Equal(originalArchive, File.ReadAllBytes(Assert.Single(result.ProjectBuildResults).Result.Release!.Projects[0].ReleaseZipPath!));
                Assert.Equal(2, publishes);
                AssertNoCoordinatedReleaseCheckpoint(root);
            }
            else
            {
                var rejected = Assert.Throws<InvalidOperationException>(() => second.Run(resumed));
                string expectedFailure = damage switch
                {
                    "source" => "source differs",
                    "resume-disabled" => "requires ResumeIncompleteRelease",
                    _ => "missing or corrupt"
                };
                Assert.Contains(expectedFailure, rejected.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, publishes);
                Assert.True(Directory.Exists(cache));
            }
            Assert.Equal(1, builds);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (Directory.Exists(staging + "-resumed")) Directory.Delete(staging + "-resumed", recursive: true);
        }
    }

    [Fact]
    public void Run_RejectsPreparedPackageReuseWithoutAnIncompleteCheckpoint()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteMinimalModule(root, "TestModule", "2.0.10");
            WriteSynchronizedProjectBuildConfig(root, "project.build.json", "TestModule", publishNuGet: false);
            var spec = CreateGalleryAndGitHubReleaseSpec(root, staging, "TestModule");
            Assert.Single(spec.Segments.OfType<ConfigurationReleaseSegment>()).Configuration.ReusePreparedPackages = true;
            var runner = CreateRunner(new FakeHostedOperations(new List<string>()), (_, _, _) => throw new InvalidOperationException("Builder must not run"));
            var failure = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
            Assert.Contains("requires ResumeIncompleteRelease", failure.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
