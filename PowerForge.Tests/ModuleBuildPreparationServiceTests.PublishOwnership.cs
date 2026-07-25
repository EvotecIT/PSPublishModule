namespace PowerForge.Tests;

public sealed partial class ModuleBuildPreparationServiceTests
{
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
