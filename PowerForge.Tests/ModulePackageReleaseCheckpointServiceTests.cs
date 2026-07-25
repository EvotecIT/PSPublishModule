namespace PowerForge.Tests;

public sealed class ModulePackageReleaseCheckpointServiceTests
{
    [Fact]
    public void Restore_uses_unique_segment_keys_for_unnamed_inline_lanes()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var moduleConfig = Path.Combine(root, "powerforge.json");
            File.WriteAllText(
                moduleConfig,
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "." },
                  "Segments": [
                    {
                      "Type": "PackageBuild",
                      "Configuration": { "RootPath": ".", "PublishNuget": true }
                    },
                    {
                      "Type": "PackageBuild",
                      "Configuration": { "RootPath": ".", "PublishNuget": true }
                    }
                  ]
                }
                """);
            var releaseConfig = Path.Combine(root, "release.json");
            var lanes = ModulePackageReleaseCheckpointService.ResolveLanes(
                releaseConfig,
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        ConfigPath = "powerforge.json",
                        IncludesPackages = true
                    }
                });

            Assert.Equal(2, lanes.Count);
            Assert.Equal(lanes[0].Name, lanes[1].Name);
            Assert.Equal(lanes[0].ConfigPath, lanes[1].ConfigPath);
            Assert.NotEqual(lanes[0].Key, lanes[1].Key);

            var firstRelease = new DotNetRepositoryReleaseResult();
            var secondRelease = new DotNetRepositoryReleaseResult();
            var checkpoints = new[]
            {
                new PowerForgeModulePackageReleaseCheckpoint
                {
                    Key = lanes[0].Key,
                    Name = lanes[0].Name,
                    ConfigPath = lanes[0].ConfigPath,
                    Release = firstRelease
                },
                new PowerForgeModulePackageReleaseCheckpoint
                {
                    Key = lanes[1].Key,
                    Name = lanes[1].Name,
                    ConfigPath = lanes[1].ConfigPath,
                    Release = secondRelease
                }
            };

            Assert.Same(
                firstRelease,
                ModulePackageReleaseCheckpointService.Restore(lanes[0], checkpoints).Release);
            Assert.Same(
                secondRelease,
                ModulePackageReleaseCheckpointService.Restore(lanes[1], checkpoints).Release);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
