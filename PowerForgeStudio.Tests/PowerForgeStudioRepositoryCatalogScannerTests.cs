using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryCatalogScannerTests
{
    [Fact]
    public void InspectRepository_ForwardSlashWorktreePath_IsDetectedAsWorktree()
    {
        using var scope = new TemporaryDirectoryScope();
        var worktreePath = scope.CreateRepository("_worktrees/PSPublishModule-pr-176");

        var scanner = new RepositoryCatalogScanner();
        var entry = scanner.InspectRepository(worktreePath.Replace('\\', '/'));

        Assert.True(entry.IsWorktree);
        Assert.Equal(ReleaseWorkspaceKind.Worktree, entry.WorkspaceKind);
    }

    [Fact]
    public void InspectRepository_UnifiedReleaseModuleConfig_IsDetectedWithoutLegacyScript()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryPath = scope.CreateRepository("JsonModuleRepo");
        var buildPath = Path.Combine(repositoryPath, "Build");
        var legacyScript = Path.Combine(buildPath, "Build-Module.ps1");
        File.Delete(legacyScript);
        File.WriteAllText(Path.Combine(buildPath, "Build-Project.ps1"), "# unified entry point");
        var moduleConfig = Path.Combine(repositoryPath, "powerforge.json");
        File.WriteAllText(moduleConfig, """{ "SchemaVersion": 1, "Segments": [] }""");
        File.WriteAllText(
            Path.Combine(buildPath, "release.json"),
            """
            {
              "Module": {
                "RepositoryRoot": "..",
                "ConfigPath": "powerforge.json"
              }
            }
            """);

        var entry = new RepositoryCatalogScanner().InspectRepository(repositoryPath);

        Assert.Equal(ReleaseRepositoryKind.Module, entry.RepositoryKind);
        Assert.Equal(moduleConfig, entry.ModuleBuildScriptPath);
        Assert.Null(entry.ProjectBuildScriptPath);
        Assert.True(entry.IsReleaseManaged);
    }

    private sealed class TemporaryDirectoryScope : IDisposable
    {
        public TemporaryDirectoryScope()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateRepository(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.Combine(path, "Build"));
            File.WriteAllText(Path.Combine(path, "Build", "Build-Module.ps1"), "# test");
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
