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
