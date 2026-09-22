using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void UnifiedGitHubRelease_PlanningDoesNotReadLockedArtifacts(bool planOnly, bool includesPackages)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var manifestPath = Path.Combine(root, "SampleModule.psd1");
            var artifact = Path.Combine(root, "SampleModule.v1.2.2.zip");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.2.3' }");
            File.WriteAllText(artifact, "stale artifact being replaced by another process");
            using var lockedArtifact = File.Open(artifact, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var executions = new List<ModuleExecutionSnapshot>();
            var publications = 0;
            var service = CreateReleaseService(root, executions,
                new PowerForgeToolReleaseResult { Success = true },
                publishGitHubRelease: _ =>
                {
                    publications++;
                    return new GitHubReleasePublishResult { Succeeded = true };
                });

            var result = service.Execute(new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions
                {
                    RepositoryRoot = root, ScriptPath = scriptPath,
                    ModuleName = "SampleModule", ManifestPath = manifestPath,
                    ModuleVersion = "1.2.3", ArtifactPaths = [artifact], IncludesPackages = includesPackages
                },
                GitHub = new PowerForgeReleaseGitHubOptions
                {
                    Publish = true, Owner = "EvotecIT", Repository = "example",
                    TokenEnvName = "PATH", VersionSource = PowerForgeReleaseVersionSource.Module
                }
            }, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "release.json"),
                ModuleRunMode = ConfigurationGateMode.Publish,
                PlanOnly = planOnly, ValidateOnly = !planOnly
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(result.ModulePlan);
            Assert.Empty(executions);
            Assert.Equal(0, publications);
            Assert.Empty(result.ModuleProducedAssets);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void UnifiedGitHubRelease_PublishesProducedModuleArchivesWithoutOptionalFeatures()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var manifestPath = Path.Combine(root, "SampleModule.psd1");
            var artifacts = Directory.CreateDirectory(Path.Combine(root, "packed")).FullName;
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.2.3' }");
            var current = Path.Combine(artifacts, "SampleModule.v1.2.3.zip");
            var full = Path.Combine(artifacts, "SampleModule.v1.2.3-FullPackage.zip");
            var stale = Path.Combine(artifacts, "SampleModule.v1.2.2.zip");
            WriteArchive(stale, "1.2.2");
            GitHubReleasePublishRequest? publication = null;
            var service = CreateReleaseService(root, new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                onModuleExecution: _ =>
                {
                    if (!File.Exists(current)) WriteArchive(current, "1.2.3");
                    if (!File.Exists(full)) WriteArchive(full, "1.2.3");
                },
                publishGitHubRelease: request =>
                {
                    publication = request;
                    return new GitHubReleasePublishResult { Succeeded = true };
                });
            var result = service.Execute(new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions
                {
                    RepositoryRoot = root, ScriptPath = scriptPath,
                    ModuleName = "SampleModule", ManifestPath = manifestPath,
                    ModuleVersion = "1.2.3", ArtifactPaths = [artifacts], IncludesPackages = false
                },
                GitHub = new PowerForgeReleaseGitHubOptions
                {
                    Publish = true, Owner = "EvotecIT", Repository = "example",
                    TokenEnvName = "PATH", VersionSource = PowerForgeReleaseVersionSource.Module
                }
            }, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "release.json"), ModuleRunMode = ConfigurationGateMode.Publish
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(publication);
            Assert.Equal(new[] { full, current }.Order(), result.ReleaseAssets.Order());
            Assert.Equal(2, result.ModuleProducedAssets.Length);
            Assert.DoesNotContain(stale, result.ReleaseAssets);
        }
        finally { TryDelete(root); }

        static void WriteArchive(string path, string version)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var writer = new StreamWriter(archive.CreateEntry("SampleModule/SampleModule.psd1").Open()))
                writer.Write($"@{{ RootModule = 'SampleModule.psm1'; ModuleVersion = '{version}' }}");
            archive.CreateEntry("SampleModule/SampleModule.psm1");
        }
    }
}
