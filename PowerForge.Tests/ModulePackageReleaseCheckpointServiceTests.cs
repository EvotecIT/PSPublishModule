namespace PowerForge.Tests;

public sealed class ModulePackageReleaseCheckpointServiceTests
{
    [Fact]
    public void ResolveLanes_treats_script_backed_package_ownership_as_legacy_module_work()
    {
        var lanes = ModulePackageReleaseCheckpointService.ResolveLanes(
            Path.Combine(Path.GetTempPath(), "release.json"),
            new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions
                {
                    ScriptPath = "Build-Module.ps1",
                    IncludesPackages = true
                }
            });

        Assert.Empty(lanes);
    }

    [Fact]
    public void Capture_resolves_inline_package_paths_from_the_module_project_root()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Module");
        Directory.CreateDirectory(moduleRoot);
        try
        {
            var projectPath = Path.Combine(moduleRoot, "Sample.Library.csproj");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Version>1.0.0</Version>
                    <PackageId>Sample.Library</PackageId>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
            var moduleConfig = Path.Combine(root, "powerforge.json");
            File.WriteAllText(
                moduleConfig,
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "Module" },
                  "Segments": [
                    {
                      "Type": "PackageBuild",
                      "Configuration": {
                        "RootPath": ".",
                        "IncludeProjects": [ "Sample.Library" ],
                        "UpdateVersions": false,
                        "Build": false,
                        "PublishNuget": true
                      }
                    }
                  ]
                }
                """);
            var releaseConfig = Path.Combine(root, "release.json");
            var spec = new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions
                {
                    RepositoryRoot = ".",
                    ConfigPath = "powerforge.json",
                    IncludesPackages = true
                }
            };

            var checkpoint = Assert.Single(
                new ModulePackageReleaseCheckpointService().Capture(releaseConfig, spec));
            var project = Assert.Single(checkpoint.Release.Projects);

            Assert.Equal(Path.GetFullPath(projectPath), Path.GetFullPath(project.CsprojPath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

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
