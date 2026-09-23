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

    [Fact]
    public async Task StorageListsGitCheckoutsWithoutTreatingLocalBuildFoldersAsWorkingCopies()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_fixture, "Workspace")).FullName;
        var primary = Directory.CreateDirectory(Path.Combine(workspace, "Product")).FullName;
        await Run(primary, "init", "-b", "main");
        var localBuild = Directory.CreateDirectory(Path.Combine(workspace, "LocalBuild", "Build")).FullName;
        await File.WriteAllTextAsync(Path.Combine(localBuild, "project.build.json"), "{}");

        var snapshot = await new WorkspaceStorageInspectionService().InspectAsync(workspace);

        var entry = Assert.Single(snapshot.Entries);
        Assert.Equal(primary, entry.Path);
        Assert.True(entry.IsPrimary);
    }

    [Fact]
    public async Task OtherWorktreeContainerFoldersAreVisibleWithoutBecomingRemovalCandidates()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_fixture, "Workspace")).FullName;
        var primary = Directory.CreateDirectory(Path.Combine(workspace, "Product")).FullName;
        await Run(primary, "init", "-b", "main");
        await Run(primary, "config", "user.email", "studio@example.test");
        await Run(primary, "config", "user.name", "Studio Test");
        await File.WriteAllTextAsync(Path.Combine(primary, "README.md"), "primary");
        await Run(primary, "add", "README.md");
        await Run(primary, "commit", "-m", "Initial");

        var container = Directory.CreateDirectory(Path.Combine(workspace, "_worktrees")).FullName;
        var registered = Path.Combine(container, "registered");
        await Run(primary, "worktree", "add", "-b", "feature/registered", registered);
        var independent = Directory.CreateDirectory(Path.Combine(container, "independent")).FullName;
        await Run(independent, "init", "-b", "main");
        var linked = Directory.CreateDirectory(Path.Combine(container, "linked")).FullName;
        await File.WriteAllTextAsync(Path.Combine(linked, ".git"), "gitdir: ../missing-owner/.git/worktrees/linked");
        var adminTarget = Directory.CreateDirectory(Path.Combine(workspace, "admin-target")).FullName;
        var linkedPresent = Directory.CreateDirectory(Path.Combine(container, "linked-present")).FullName;
        await File.WriteAllTextAsync(Path.Combine(linkedPresent, ".git"), "gitdir: ../../admin-target");
        var invalidLink = Directory.CreateDirectory(Path.Combine(container, "invalid-link")).FullName;
        await File.WriteAllTextAsync(Path.Combine(invalidLink, ".git"), "not a Git link");
        var plain = Directory.CreateDirectory(Path.Combine(container, "plain")).FullName;
        await File.WriteAllTextAsync(Path.Combine(plain, "notes.txt"), "keep");

        var snapshot = await new WorkspaceStorageInspectionService().InspectAsync(workspace);

        Assert.Equal(2, snapshot.Entries.Count);
        Assert.DoesNotContain(snapshot.UnregisteredFolders, folder => folder.Path == registered);
        Assert.Equal(5, snapshot.UnregisteredFolders.Count);
        Assert.Equal("Independent Git checkout", Assert.Single(snapshot.UnregisteredFolders, folder => folder.Path == independent).Kind);
        var missingLink = Assert.Single(snapshot.UnregisteredFolders, folder => folder.Path == linked);
        Assert.Equal("Git-linked folder", missingLink.Kind);
        Assert.Equal("Git administrative target missing", missingLink.GitMetadataState);
        var presentLink = Assert.Single(snapshot.UnregisteredFolders, folder => folder.Path == linkedPresent);
        Assert.Equal("Git-linked folder", presentLink.Kind);
        Assert.Equal("Git administrative target present", presentLink.GitMetadataState);
        var invalid = Assert.Single(snapshot.UnregisteredFolders, folder => folder.Path == invalidLink);
        Assert.Equal("Folder with .git file", invalid.Kind);
        Assert.Equal("Git link could not be resolved", invalid.GitMetadataState);
        Assert.Equal("Folder without Git metadata", Assert.Single(snapshot.UnregisteredFolders, folder => folder.Path == plain).Kind);
        Assert.True(snapshot.OtherFolderBytes > 0);
        Assert.All(snapshot.Entries.Where(entry => entry.IsReviewCandidate), entry => Assert.NotEqual(independent, entry.Path));
    }

    [Fact]
    public async Task FailedGitRegistrationListingDoesNotClassifyOtherFolders()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_fixture, "Workspace")).FullName;
        var primary = Directory.CreateDirectory(Path.Combine(workspace, "Product")).FullName;
        Directory.CreateDirectory(Path.Combine(primary, ".git"));
        var unknown = Directory.CreateDirectory(Path.Combine(workspace, "_worktrees", "possibly-registered")).FullName;
        await File.WriteAllTextAsync(Path.Combine(unknown, "notes.txt"), "retain");

        var snapshot = await new WorkspaceStorageInspectionService().InspectAsync(workspace);

        Assert.False(snapshot.IsRegistrationInventoryComplete);
        Assert.Contains("partial", snapshot.RegistrationScanWarning!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(snapshot.UnregisteredFolders);
        Assert.Contains("not classified", snapshot.OtherFolderScanWarning!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Git inspection failed", Assert.Single(snapshot.Entries).LocalState);
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
