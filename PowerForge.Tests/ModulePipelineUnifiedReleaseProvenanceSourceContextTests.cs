using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, true, false)]
    public void Run_ReleaseProvenanceRequiresKnownMatchingReusedSource(bool compiled, bool unified, bool synchronized, bool nestedModule)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var checkoutRoot = Path.Combine(root.FullName, "source");
        var sourceRoot = synchronized && nestedModule ? Path.Combine(checkoutRoot, "module") : checkoutRoot;
        var staging = Path.Combine(root.FullName, "staging");
        var artifacts = Path.Combine(root.FullName, "artifacts");
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(sourceRoot, moduleName, "1.0.0");
            if (synchronized && nestedModule)
            {
                // Also cover coordinated-release state outside the nested module compilation inputs.
                Directory.Delete(Path.Combine(sourceRoot, ".git"), recursive: true);
                RunModuleSourceGit(checkoutRoot, "init", "--quiet");
            }
            var signing = CreateCompiledSourceCheckpointSigning(root.FullName);
            var protection = new ReleaseProtectionConfiguration();
            var publications = new List<(string? TagSource, string? ProvenanceSource)>();
            void RecordPublication(GitHubReleasePublishRequest request)
            {
                if (!protection.GenerateProvenance) return;
                using var archive = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(artifacts, "*.zip")));
                var provenance = Assert.Single(archive.Entries,
                    entry => entry.FullName.EndsWith("/PowerForge.ReleaseProvenance.json", StringComparison.Ordinal));
                using var content = provenance.Open();
                using var json = JsonDocument.Parse(content);
                publications.Add((request.Commitish, json.RootElement.GetProperty("commit").GetString()));
                if (synchronized && publications.Count == 1)
                    throw new InvalidOperationException("Simulated interruption after binding the coherent cached payload.");
            }
            var hosted = new FakeHostedOperations(new List<string>())
            {
                BinaryDependencyValidation = _ => { },
                ModuleSigningCompleted = (_, _) => { },
                ModulePublishAction = (publish, plan) =>
                    RecordPublication(ModulePublisher.CreateGitHubReleaseRequest(publish, plan, Array.Empty<string>()))
            };
            var runner = CreateRunner(hosted,
                (request, _, configPath) =>
                {
                    if (!synchronized)
                        throw new Xunit.Sdk.XunitException("No package build was configured.");
                    var result = CreateProjectBuildResult(sourceRoot, moduleName, "2.0.11",
                        Path.Combine(root.FullName, "packages"), request, configPath);
                    result.Result.Release!.SourceCommitSha = ReleaseSourceCommit.Capture(sourceRoot);
                    return result;
                },
                request =>
                {
                    RecordPublication(request);
                    return new GitHubReleasePublishResult { Succeeded = true, ReleaseCreationSucceeded = true, ReleaseId = 42 };
                });
            var segments = new List<IConfigurationSegment>
            {
                new ConfigurationOptionsSegment { Options = new ConfigurationOptions { Signing = signing } },
                new ConfigurationBuildSegment { BuildModule = new BuildModuleConfiguration { SignMerged = true } },
                new ConfigurationReleaseProtectionSegment { Configuration = protection },
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration { Enabled = true, Path = artifacts }
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
            if (synchronized)
            {
                WriteSynchronizedProjectBuildConfig(sourceRoot, "project.build.json", moduleName, publishNuGet: false);
                segments.Insert(0, CreateProjectBuildSegment(moduleName, enabled: true, buildBeforeModule: true));
            }
            if (unified)
            {
                segments.Add(new ConfigurationReleaseSegment
                {
                    Configuration = new ReleaseConfiguration
                    {
                        StageRoot = Path.Combine(root.FullName, "release"),
                        VersionSource = synchronized ? ReleaseVersionSource.ProjectBuild : ReleaseVersionSource.Module,
                        PrimaryProject = synchronized ? moduleName : null,
                        SynchronizeModuleVersion = synchronized,
                        ResumeIncompleteRelease = synchronized
                    }
                });
            }
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName, SourcePath = sourceRoot, Version = synchronized ? "2.0.11" : "1.0.0",
                    StagingPath = staging, KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            };
            // Normalize publisher-owned source metadata before recording the clean source commit.
            runner.Run(spec);
            Directory.Delete(staging, recursive: true);
            Directory.Delete(artifacts, recursive: true);
            InitializeModuleSourceGit(checkoutRoot);
            var originalCommit = ReleaseSourceCommit.Capture(sourceRoot);
            protection.GenerateProvenance = true;
            if (compiled)
            {
                spec.Build.PowerShellCompilation = new PowerShellModuleCompilationConfiguration
                {
                    Enabled = true, Mode = PowerShellCompilationMode.Hybrid, TargetFramework = "net10.0",
                    AllowUnreviewedDependencies = true
                };
            }

            if (synchronized)
            {
                var interrupted = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
                Assert.Contains("Simulated interruption", interrupted.Message, StringComparison.Ordinal);
            }
            else
            {
                runner.Run(spec);
            }
            Assert.Equal((originalCommit, originalCommit), Assert.Single(publications));
            spec.Build.ReuseStaging = true;
            spec.Build.SkipDotNetBuild = true;
            if (synchronized)
            {
                RunModuleSourceGit(sourceRoot, "add", ".");
                RunModuleSourceGit(sourceRoot, "commit", "--quiet", "--allow-empty", "-m", "Advance checkout before coherent cache resume");
                var currentCommit = ReleaseSourceCommit.Capture(sourceRoot);
                Assert.NotEqual(originalCommit, currentCommit);
                var restored = runner.Run(spec);
                Assert.Equal(currentCommit, restored.Plan.SourceRevision);
                Assert.Equal(originalCommit, restored.Plan.SourceCommitSha);
                Assert.Equal(originalCommit, Assert.Single(restored.ProjectBuildResults).Result.Release!.SourceCommitSha);
                Assert.Equal(2, publications.Count);
                Assert.All(publications, publication => Assert.Equal((originalCommit, originalCommit), publication));
                return;
            }
            if (!compiled)
            {
                var unknown = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
                Assert.Contains("source context is unknown", unknown.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Single(publications);
                return;
            }

            var unchanged = runner.Run(spec);
            Assert.Equal(originalCommit, unchanged.Plan.SourceCommitSha);
            Assert.Equal(2, publications.Count);
            Assert.All(publications, publication => Assert.Equal((originalCommit, originalCommit), publication));
            RunModuleSourceGit(sourceRoot, "add", ".");
            RunModuleSourceGit(sourceRoot, "commit", "--quiet", "--allow-empty", "-m", "Advance protected checkout after compilation");
            Assert.NotEqual(originalCommit, ReleaseSourceCommit.Capture(sourceRoot));
            var moved = Record.Exception(() => runner.Run(spec));
            Assert.True(moved is InvalidOperationException,
                "A moved checkout must not publish reused compiled payload with conflicting source context. " +
                string.Join("; ", publications.Select(publication => $"tag={publication.TagSource}, provenance={publication.ProvenanceSource}")));
            Assert.Contains("source context", moved!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rebuild", moved.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, publications.Count);

            var checkpointPath = Path.Combine(staging, moduleName + ".powerforge-module-compilation.json");
            var checkpoint = JsonNode.Parse(File.ReadAllText(checkpointPath))!.AsObject();
            Assert.True(checkpoint.Remove("sourceCommitSha"));
            File.WriteAllText(checkpointPath, checkpoint.ToJsonString());
            var signature = PowerShellCompilationEvidenceAuthenticator.Sign(File.ReadAllBytes(checkpointPath), signing);
            File.WriteAllBytes(Path.Combine(staging, moduleName + ".powerforge-module-compilation.p7s"), signature.Signature);
            var legacy = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
            Assert.Contains("source context is unknown", legacy.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, publications.Count);
        }
        finally
        {
            DeleteModuleSourceFixture(new DirectoryInfo(checkoutRoot));
            if (Directory.Exists(root.FullName)) root.Delete(recursive: true);
        }
    }
}
