using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Tests;

public sealed class WorktreeDetectorTests : IDisposable
{
    private readonly string _tempRoot;

    public WorktreeDetectorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "WorktreeDetectorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void IsGitRepository_WithGitDirectory_ReturnsTrue()
    {
        var repoDir = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(repoDir);
        Directory.CreateDirectory(Path.Combine(repoDir, ".git"));

        Assert.True(WorktreeDetector.IsGitRepository(repoDir));
    }

    [Fact]
    public void IsGitRepository_WithoutGit_ReturnsFalse()
    {
        var plainDir = Path.Combine(_tempRoot, "plain");
        Directory.CreateDirectory(plainDir);

        Assert.False(WorktreeDetector.IsGitRepository(plainDir));
    }

    [Fact]
    public void IsWorktree_RegularRepoWithGitDirectory_ReturnsFalse()
    {
        var repoDir = Path.Combine(_tempRoot, "regular");
        Directory.CreateDirectory(repoDir);
        Directory.CreateDirectory(Path.Combine(repoDir, ".git"));

        Assert.False(WorktreeDetector.IsWorktree(repoDir));
    }

    [Fact]
    public void ResolveParentRepositoryName_NonWorktree_ReturnsNull()
    {
        var repoDir = Path.Combine(_tempRoot, "nonwt");
        Directory.CreateDirectory(repoDir);
        Directory.CreateDirectory(Path.Combine(repoDir, ".git"));

        Assert.Null(WorktreeDetector.ResolveParentRepositoryName(repoDir));
    }
}
