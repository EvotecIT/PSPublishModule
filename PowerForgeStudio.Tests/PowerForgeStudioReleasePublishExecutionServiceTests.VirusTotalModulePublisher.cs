using PowerForge;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleasePublishExecutionServiceTests
{
    [Fact]
    public void CreateUnifiedPublishRequest_EnabledStudioModulePublisher_PropagatesActiveLane()
    {
        var repositoryRoot = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
            var releaseConfig = Path.Combine(repositoryRoot, "release.json");
            File.WriteAllText(
                moduleConfig,
                """
                {
                  "Build": { "Name": "Example", "SourcePath": "." },
                  "Segments": [
                    {
                      "Type": "GalleryNuget",
                      "Configuration": {
                        "Destination": "PowerShellGallery",
                        "Enabled": true,
                        "RepositoryName": "PSGallery"
                      }
                    }
                  ]
                }
                """);
            File.WriteAllText(
                releaseConfig,
                """
                {
                  "Module": {
                    "RepositoryRoot": ".",
                    "ConfigPath": "powerforge.json"
                  }
                }
                """);
            var spec = PowerForgeReleaseService.LoadConfiguration(releaseConfig);

            var request = ReleasePublishExecutionService.CreateUnifiedPublishRequest(
                releaseConfig,
                spec,
                new PowerForgeReleaseResult());

            Assert.True(request.ModulePublisherActive);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }
}
