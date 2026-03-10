using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryPlanPreviewServiceTests
{
    [Fact]
    public void ResolveProjectConfigPath_UsesSiblingConfigForNestedBuildScript()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("MixedRepo");
        var nestedBuildDirectory = scope.CreateDirectory(Path.Combine("MixedRepo", "src", "ReleaseHost", "Build"));
        var nestedScriptPath = Path.Combine(nestedBuildDirectory, "Build-Project.ps1");
        var nestedConfigPath = Path.Combine(nestedBuildDirectory, "project.build.json");

        File.WriteAllText(nestedScriptPath, "# test");
        File.WriteAllText(nestedConfigPath, "{ }");

        var resolved = RepositoryPlanPreviewService.ResolveProjectConfigPath(nestedScriptPath, repositoryRoot);

        Assert.Equal(nestedConfigPath, resolved);
    }

    [Fact]
    public void ResolveProjectConfigPath_FallsBackToRootBuildConfigWhenSiblingMissing()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LibraryRepo");
        var rootBuildDirectory = scope.CreateDirectory(Path.Combine("LibraryRepo", "Build"));
        var nestedBuildDirectory = scope.CreateDirectory(Path.Combine("LibraryRepo", "tools", "Build"));
        var nestedScriptPath = Path.Combine(nestedBuildDirectory, "Build-Project.ps1");
        var rootConfigPath = Path.Combine(rootBuildDirectory, "project.build.json");

        File.WriteAllText(nestedScriptPath, "# test");
        File.WriteAllText(rootConfigPath, "{ }");

        var resolved = RepositoryPlanPreviewService.ResolveProjectConfigPath(nestedScriptPath, repositoryRoot);

        Assert.Equal(rootConfigPath, resolved);
    }

    private sealed class TemporaryDirectoryScope : IDisposable
    {
        public TemporaryDirectoryScope()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
