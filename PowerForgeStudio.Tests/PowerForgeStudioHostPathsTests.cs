using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioHostPathsTests
{
    [Fact]
    public void GetScopedFilePath_CreatesSanitizedDirectoryStructure()
    {
        var localAppDataRoot = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests", Guid.NewGuid().ToString("N"));

        try
        {
            var filePath = PowerForgeStudioHostPaths.GetScopedFilePath(
                repositoryName: "Repo:One",
                areaName: "runtime",
                scopeName: "project/publish",
                fileName: "plan.json",
                localApplicationDataPath: localAppDataRoot);

            var expectedDirectory = Path.Combine(localAppDataRoot, "PowerForgeStudio", "runtime", "Repo_One", "project_publish");
            Assert.Equal(Path.Combine(expectedDirectory, "plan.json"), filePath);
            Assert.True(Directory.Exists(expectedDirectory));
        }
        finally
        {
            if (Directory.Exists(localAppDataRoot))
            {
                Directory.Delete(localAppDataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void GetStudioRootPath_UsesProvidedLocalAppDataRoot()
    {
        var localAppDataRoot = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests", Guid.NewGuid().ToString("N"));
        var studioRoot = PowerForgeStudioHostPaths.GetStudioRootPath(localAppDataRoot);
        Assert.Equal(Path.Combine(localAppDataRoot, "PowerForgeStudio"), studioRoot);
    }
}
