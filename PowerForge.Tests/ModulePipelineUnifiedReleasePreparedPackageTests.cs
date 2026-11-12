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
    [InlineData("dependency-order")]
    [InlineData("payload-free")]
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
            if (damage == "dependency-order")
                configuration["ExpectedVersionMap"]!["Dependency"] = "2.0.X";
            if (damage == "payload-free")
                configuration["ExpectedVersionMap"]!["NoPackage"] = "2.0.X";
            File.WriteAllText(configPath, configuration.ToJsonString());
            int builds = 0;
            int plans = 0;
            int publishes = 0;
            byte[]? originalPackage = null;
            byte[]? originalArchive = null;

            ProjectBuildHostExecutionResult Build(ProjectBuildHostRequest request, ProjectBuildConfiguration? config, string? path)
            {
                if (request.PlanOnly == true)
                {
                    plans++;
                    Assert.Equal("payload-free", damage);
                    Assert.False(request.ExecuteBuild);
                    Assert.False(request.Build);
                    Assert.False(request.UpdateVersions);
                    Assert.False(request.PublishNuget);
                    Assert.False(request.PublishGitHub);
                    Assert.NotNull(config);
                    Assert.Contains("NoPackage", config.ExpectedVersionMap!.Keys);
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = new DotNetRepositoryReleaseResult
                            {
                                IsPlan = true,
                                Projects = new()
                                {
                                    new DotNetRepositoryProjectResult { ProjectName = "NoPackage", PackageId = "Distinct.Identity", IsPackable = false }
                                }
                            }
                        }
                    };
                }
                builds++;
                Assert.Equal(1, builds);
                var result = CreateProjectBuildResult(root, name, version, Path.Combine(root, "Artifacts", "NuGet"), request, path);
                var project = Assert.Single(result.Result.Release!.Projects);
                string package = Assert.Single(project.Packages);
                File.Delete(package);
                WritePreparedRecoveryPackage(package, name, version, damage == "dependency-order" ? "Dependency" : null);
                if (damage == "dependency-order")
                {
                    string dependencyPath = Path.Combine(Path.GetDirectoryName(package)!, "Dependency." + version + ".nupkg");
                    WritePreparedRecoveryPackage(dependencyPath, "Dependency", version);
                    result.Result.Release.Projects.Add(new DotNetRepositoryProjectResult
                    {
                        ProjectName = "Dependency", PackageId = "Dependency", IsPackable = true,
                        NewVersion = version, Packages = new() { dependencyPath }
                    });
                    result.Result.Release.ResolvedVersionsByProject.Add("Dependency", version);
                }
                if (damage == "payload-free")
                {
                    result.Result.Release.Projects.Add(new DotNetRepositoryProjectResult
                    {
                        ProjectName = "NoPackage", PackageId = "Distinct.Identity", IsPackable = false
                    });
                }
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
                Assert.Equal(originalPackage, File.ReadAllBytes(Assert.Single(request.AssetFilePaths, path => Path.GetFileName(path) == name + "." + version + ".nupkg")));
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
            string checkpoint = Path.ChangeExtension(cache, ".json");
            byte[] originalCheckpoint = File.ReadAllBytes(checkpoint);
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
            if (damage is "none" or "dependency-order" or "payload-free")
            {
                var result = second.Run(resumed);
                Assert.Equal(version, result.Plan.ResolvedVersion);
                var projects = Assert.Single(result.ProjectBuildResults).Result.Release!.Projects;
                Assert.Equal(originalArchive, File.ReadAllBytes(Assert.Single(projects, project => project.ProjectName == name).ReleaseZipPath!));
                if (damage == "dependency-order")
                    Assert.Equal(new[] { "Dependency", name }, projects.Select(project => project.PackageId));
                if (damage == "payload-free")
                {
                    var payloadFree = Assert.Single(projects, project => project.ProjectName == "NoPackage");
                    Assert.False(payloadFree.IsPackable);
                    Assert.Null(payloadFree.NewVersion);
                }
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
                if (damage != "source")
                    Assert.Equal(originalCheckpoint, File.ReadAllBytes(checkpoint));
            }
            Assert.Equal(1, builds);
            Assert.Equal(damage == "payload-free" ? 1 : 0, plans);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (Directory.Exists(staging + "-resumed")) Directory.Delete(staging + "-resumed", recursive: true);
        }
    }

    private static void WritePreparedRecoveryPackage(string path, string id, string version, string? dependency = null)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(id + ".nuspec").Open());
        string dependencies = dependency is null ? string.Empty : $"<dependencies><dependency id='{dependency}' version='[{version}]'/></dependencies>";
        writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><authors>Test</authors><description>Prepared payload</description>{dependencies}</metadata></package>");
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
