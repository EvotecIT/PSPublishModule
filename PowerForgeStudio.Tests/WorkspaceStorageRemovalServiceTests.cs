using PowerForge;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Tests;

public sealed class WorkspaceStorageRemovalServiceTests : IDisposable
{
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), "studio-remove-" + Guid.NewGuid().ToString("N"));
    private readonly GitClient _git = new();

    [Fact]
    public async Task ExactRemoteReviewRequiresConfirmationsAndRemovesWithoutForce()
    {
        var setup = await CreateMergedWorktreeAsync();
        var artifactDirectory = Directory.CreateDirectory(Path.Combine(setup.Worktree, "bin")).FullName;
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "retained.zip"), "artifact");
        var ignoredDirectory = Directory.CreateDirectory(Path.Combine(setup.Worktree, ".cache")).FullName;
        await File.WriteAllTextAsync(Path.Combine(ignoredDirectory, "session.bin"), "ignored");
        var entry = StorageEntry(setup);
        var service = new WorkspaceStorageRemovalService();

        var review = await service.ReviewAsync(setup.Workspace, entry, []);

        Assert.True(review.ReadyForConfirmation, string.Join(" | ", review.Findings));
        Assert.Equal(await Head(setup.Worktree), review.HeadSha);
        Assert.Equal("refs/heads/main", review.RemoteDefaultRef);
        Assert.True(review.HasRetainedArtifacts);
        Assert.Contains(review.RetainedArtifactPaths, path => PathsEqual(path, ignoredDirectory));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveAsync(review, [], false, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveAsync(review, [], true, false));
        Assert.True(Directory.Exists(setup.Worktree));

        await service.RemoveAsync(review, [], true, true);

        Assert.False(Directory.Exists(setup.Worktree));
        var listing = await _git.RunRawAsync(setup.Primary, ["worktree", "list", "--porcelain"]);
        Assert.DoesNotContain(setup.Worktree, listing.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangedOrStudioOwnedWorktreeCannotUseAnEarlierReview()
    {
        var setup = await CreateMergedWorktreeAsync();
        var entry = StorageEntry(setup);
        var service = new WorkspaceStorageRemovalService();
        var protectedReview = await service.ReviewAsync(setup.Workspace, entry, [setup.Worktree]);
        Assert.False(protectedReview.StudioUsePassed);
        Assert.False(protectedReview.ReadyForConfirmation);

        await Run(setup.Primary, "worktree", "lock", setup.Worktree);
        var lockedReview = await service.ReviewAsync(setup.Workspace, entry, []);
        Assert.False(lockedReview.UnlockedPassed);
        Assert.False(lockedReview.ReadyForConfirmation);
        await Run(setup.Primary, "worktree", "unlock", setup.Worktree);

        var review = await service.ReviewAsync(setup.Workspace, entry, []);
        Assert.True(review.ReadyForConfirmation, string.Join(" | ", review.Findings));
        await File.WriteAllTextAsync(Path.Combine(setup.Worktree, "draft.txt"), "draft");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveAsync(review, [], true, true));

        Assert.True(Directory.Exists(setup.Worktree));
        Assert.Equal("draft", await File.ReadAllTextAsync(Path.Combine(setup.Worktree, "draft.txt")));
    }

    private async Task<Setup> CreateMergedWorktreeAsync()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_fixture, Guid.NewGuid().ToString("N"), "Workspace")).FullName;
        var remote = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(workspace)!, "Remote.git")).FullName;
        await Run(remote, "init", "--bare");
        await Run(remote, "symbolic-ref", "HEAD", "refs/heads/main");
        var primary = Directory.CreateDirectory(Path.Combine(workspace, "Product")).FullName;
        await Run(primary, "init", "-b", "main");
        await Run(primary, "config", "user.email", "studio@example.test");
        await Run(primary, "config", "user.name", "Studio Test");
        await File.WriteAllTextAsync(Path.Combine(primary, ".gitignore"), "bin/\nobj/\n.cache/\n");
        await File.WriteAllTextAsync(Path.Combine(primary, "README.md"), "primary");
        await Run(primary, "add", ".gitignore", "README.md");
        await Run(primary, "commit", "-m", "Initial");
        await Run(primary, "remote", "add", "origin", remote);
        await Run(primary, "push", "-u", "origin", "main");
        var worktree = Path.Combine(workspace, "_worktrees", "feature");
        Directory.CreateDirectory(Path.GetDirectoryName(worktree)!);
        await Run(primary, "worktree", "add", "-b", "feature/storage", worktree);
        await File.WriteAllTextAsync(Path.Combine(worktree, "feature.txt"), "feature");
        await Run(worktree, "add", "feature.txt");
        await Run(worktree, "commit", "-m", "Feature");
        await Run(primary, "merge", "--no-ff", "feature/storage", "-m", "Merge feature");
        await Run(primary, "push", "origin", "main");
        return new(workspace, primary, worktree);
    }

    private static WorkspaceStorageEntry StorageEntry(Setup setup)
        => new("Product", setup.Worktree, setup.Primary, "feature/storage", false, true, false,
            0, 0, 0, "Clean", "main", "Locally merged", "Review removal evidence", true, null);

    private async Task<string> Head(string root)
        => (await _git.RunRawAsync(root, ["rev-parse", "HEAD"])).StdOut.Trim();

    private async Task Run(string root, params string[] arguments)
    {
        var result = await _git.RunRawAsync(root, arguments);
        Assert.True(result.Succeeded, $"git {string.Join(' ', arguments)} failed: {result.StdErr}");
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public void Dispose()
    {
        if (!Directory.Exists(_fixture)) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(_fixture, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            var clear = FileAttributes.ReadOnly | FileAttributes.Hidden;
            if ((attributes & clear) != 0) File.SetAttributes(path, attributes & ~clear);
        }
        Directory.Delete(_fixture, recursive: true);
    }

    private sealed record Setup(string Workspace, string Primary, string Worktree);
}
