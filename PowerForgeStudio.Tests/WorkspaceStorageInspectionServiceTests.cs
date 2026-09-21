using PowerForge;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Tests;

public sealed class WorkspaceStorageInspectionServiceTests : IDisposable
{
    private readonly string _fixture = Path.Combine(
        Path.GetTempPath(),
        "studio-storage-" + Guid.NewGuid().ToString("N"));
    private readonly GitClient _git = new();

    [Fact]
    public async Task InspectionTracksMergedAndChangedWorktreeWithoutAuthorizingRemoval()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_fixture, "Workspace")).FullName;
        var primary = Directory.CreateDirectory(Path.Combine(workspace, "Product")).FullName;
        await Run(primary, "init", "-b", "main");
        await Run(primary, "config", "user.email", "studio@example.test");
        await Run(primary, "config", "user.name", "Studio Test");
        await File.WriteAllTextAsync(Path.Combine(primary, "README.md"), "primary");
        await Run(primary, "add", "README.md");
        await Run(primary, "commit", "-m", "Initial");
        var worktree = Path.Combine(workspace, "_worktrees", "product-feature");
        Directory.CreateDirectory(Path.GetDirectoryName(worktree)!);
        await Run(primary, "worktree", "add", "-b", "feature/storage", worktree);
        await File.WriteAllTextAsync(Path.Combine(worktree, "feature.txt"), "feature");
        await Run(worktree, "add", "feature.txt");
        await Run(worktree, "commit", "-m", "Feature");

        var service = new WorkspaceStorageInspectionService();
        var observations = new List<WorkspaceStorageScanProgress>();
        var beforeMerge = await service.InspectAsync(workspace, new InlineProgress<WorkspaceStorageScanProgress>(observations.Add));
        Assert.Contains(observations, item => item.TotalRepositories == 1 && item.CompletedRepositories == 1);
        Assert.Contains(observations, item => item.WorkingCopyPath == worktree && item.MeasuredBytes > 0);
        var unmerged = Assert.Single(beforeMerge.Entries, entry => !entry.IsPrimary);
        Assert.Equal("Not merged", unmerged.AncestryState);
        Assert.False(unmerged.IsReviewCandidate);
        Assert.True(beforeMerge.IndexedBytes > 0);
        Assert.True(beforeMerge.WorktreeBytes > 0);

        await Run(primary, "merge", "--no-ff", "feature/storage", "-m", "Merge feature");
        var merged = Assert.Single((await service.InspectAsync(workspace)).Entries, entry => !entry.IsPrimary);
        Assert.Equal("Locally merged", merged.AncestryState);
        Assert.True(merged.IsReviewCandidate);
        Assert.Contains("remote", merged.NextCheck, StringComparison.OrdinalIgnoreCase);

        await File.WriteAllTextAsync(Path.Combine(worktree, "draft.txt"), "draft");
        var changed = Assert.Single((await service.InspectAsync(workspace)).Entries, entry => !entry.IsPrimary);
        Assert.True(changed.IsChanged);
        Assert.False(changed.IsReviewCandidate);
        Assert.Contains("Preserve", changed.NextCheck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingRegisteredWorktreeIsReportedAsBrokenReference()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_fixture, "Workspace")).FullName;
        var primary = Directory.CreateDirectory(Path.Combine(workspace, "Product")).FullName;
        await Run(primary, "init", "-b", "main");
        await Run(primary, "config", "user.email", "studio@example.test");
        await Run(primary, "config", "user.name", "Studio Test");
        await File.WriteAllTextAsync(Path.Combine(primary, "README.md"), "primary");
        await Run(primary, "add", "README.md");
        await Run(primary, "commit", "-m", "Initial");
        var worktree = Path.Combine(workspace, "_worktrees", "missing-feature");
        Directory.CreateDirectory(Path.GetDirectoryName(worktree)!);
        await Run(primary, "worktree", "add", "-b", "feature/missing", worktree);
        Directory.Delete(worktree, recursive: true);

        var snapshot = await new WorkspaceStorageInspectionService().InspectAsync(workspace);

        var broken = Assert.Single(snapshot.Entries, entry => !entry.Exists);
        Assert.True(broken.IsBroken);
        Assert.Equal("Broken reference", broken.LocalState);
        Assert.False(broken.IsReviewCandidate);
        Assert.Contains("prune", broken.NextCheck, StringComparison.OrdinalIgnoreCase);
    }

    private async Task Run(string root, params string[] arguments)
    {
        var result = await _git.RunRawAsync(root, arguments);
        Assert.True(result.Succeeded, $"git {string.Join(' ', arguments)} failed: {result.StdErr}");
    }

    [Fact]
    public async Task ProgressCallbackCanCancelBeforeRepositoryMeasurement()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_fixture, "Workspace")).FullName;
        var primary = Directory.CreateDirectory(Path.Combine(workspace, "Product")).FullName;
        await Run(primary, "init", "-b", "main");
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<WorkspaceStorageScanProgress>(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WorkspaceStorageInspectionService().InspectAsync(workspace, progress, cancellation.Token));
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_fixture))
            return;
        foreach (var path in Directory.EnumerateFileSystemEntries(_fixture, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(_fixture, recursive: true);
    }
}
