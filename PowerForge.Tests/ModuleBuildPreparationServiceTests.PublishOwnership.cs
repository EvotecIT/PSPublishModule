namespace PowerForge.Tests;

public sealed partial class ModuleBuildPreparationServiceTests
{
    [Fact]
    public void Prepare_outer_package_owner_disables_removed_package_version_synchronization()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-module-package-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Module"));
        try
        {
            var configPath = Path.Combine(root, "powerforge.json");
            File.WriteAllText(
                configPath,
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "Module" },
                  "Segments": [
                    {
                      "Type": "PackageBuild",
                      "Configuration": {
                        "RootPath": ".",
                        "UseAsReleaseVersionSource": true,
                        "BuildBeforeModule": true
                      }
                    },
                    {
                      "Type": "Release",
                      "Configuration": {
                        "VersionSource": "PackageBuild",
                        "PrimaryProject": "Sample",
                        "SynchronizeModuleVersion": true
                      }
                    }
                  ]
                }
                """);

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Config",
                ConfigPath = configPath,
                CurrentPath = root,
                IncludeProjectPackages = false,
                ResolvePath = path => Path.GetFullPath(Path.Combine(root, path))
            });

            Assert.Empty(prepared.PipelineSpec.Segments.OfType<ConfigurationPackageBuildSegment>());
            var release = Assert.Single(
                prepared.PipelineSpec.Segments.OfType<ConfigurationReleaseSegment>());
            Assert.False(release.Configuration.SynchronizeModuleVersion);
            Assert.Equal(ReleaseVersionSource.Module, release.Configuration.VersionSource);
            Assert.Null(release.Configuration.PrimaryProject);
            _ = new ModulePipelineRunner(new NullLogger()).Plan(prepared.PipelineSpec);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_ParentPublishHost_keeps_package_lanes_but_removes_module_publish_segments()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-module-publish-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "powerforge.json");
            File.WriteAllText(
                configPath,
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "Module" },
                  "Segments": [
                    {
                      "Type": "PackageBuild",
                      "Configuration": { "Name": "Library", "RootPath": ".", "PublishNuget": true }
                    },
                    {
                      "Type": "GalleryNuget",
                      "Configuration": { "Enabled": true, "Destination": "PowerShellGallery" }
                    }
                  ]
                }
                """);
            Directory.CreateDirectory(Path.Combine(root, "Module"));

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Config",
                ConfigPath = configPath,
                CurrentPath = root,
                IncludeProjectPackages = true,
                IncludeModulePublishing = false,
                ResolvePath = path => Path.GetFullPath(Path.Combine(root, path))
            });

            Assert.Single(prepared.PipelineSpec.Segments.OfType<ConfigurationPackageBuildSegment>());
            Assert.Empty(prepared.PipelineSpec.Segments.OfType<ConfigurationPublishSegment>());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
