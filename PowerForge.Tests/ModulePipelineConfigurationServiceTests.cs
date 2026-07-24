namespace PowerForge.Tests;

public sealed class ModulePipelineConfigurationServiceTests
{
    [Fact]
    public void Load_ResolvesProjectOwnedPathsAndEffectiveManifestVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-module-config-" + Guid.NewGuid().ToString("N")));
        try
        {
            var configPath = Path.Combine(root.FullName, "powerforge.json");
            File.WriteAllText(
                configPath,
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "src/Sample", "Version": "1.0.0" },
                  "Segments": [
                    { "Type": "Manifest", "Configuration": { "ModuleVersion": "1.1.0" } },
                    { "Type": "Manifest", "Configuration": { "ModuleVersion": "1.2.0" } },
                    { "Type": "Packed", "Configuration": { "Enabled": true, "Path": "out/<TagModuleVersionWithPreRelease>" } },
                    {
                      "Type": "GalleryNuget",
                      "Configuration": {
                        "Destination": "PowerShellGallery",
                        "Enabled": true,
                        "ApiKeyFilePath": "secrets/gallery.key"
                      }
                    }
                  ]
                }
                """);

            var context = new ModulePipelineConfigurationService().Load(configPath);

            var projectRoot = Path.Combine(root.FullName, "src", "Sample");
            Assert.Equal(projectRoot, context.ProjectRoot);
            Assert.Equal("1.2.0", context.EffectiveVersion);
            Assert.Equal(
                Path.Combine(projectRoot, "out", "<TagModuleVersionWithPreRelease>"),
                Assert.Single(context.ArtifactPaths));
            var publish = Assert.Single(context.Spec.Segments.OfType<ConfigurationPublishSegment>());
            Assert.Equal(Path.Combine(projectRoot, "secrets", "gallery.key"), publish.Configuration.ApiKeyFilePath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Load_AcceptsSegmentlessBuildButRejectsMissingRequiredBuildValues()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-module-config-contract-" + Guid.NewGuid().ToString("N")));
        try
        {
            var validPath = Path.Combine(root.FullName, "valid.json");
            File.WriteAllText(validPath, """{ "Build": { "Name": "Sample", "SourcePath": "." } }""");
            Assert.Empty(new ModulePipelineConfigurationService().Load(validPath).Spec.Segments);

            var invalidPath = Path.Combine(root.FullName, "invalid.json");
            File.WriteAllText(invalidPath, """{ "Build": { "Name": "Sample" } }""");
            var exception = Assert.Throws<InvalidOperationException>(
                () => new ModulePipelineConfigurationService().Load(invalidPath));
            Assert.Contains("Build.SourcePath", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
