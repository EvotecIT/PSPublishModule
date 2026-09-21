using PowerForge;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Hub;
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
        Assert.DoesNotContain(NormalizeGitPath(setup.Worktree), NormalizeGitPath(listing.StdOut), StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task ExactMergedPullRequestHeadPermitsSquashStyleCleanup()
    {
        var setup = await CreateWorktreeAsync(mergeIntoDefault: false);
        var head = await Head(setup.Worktree);
        var pull = new GitHubPullRequest(42, "Merged feature", "closed", "dev", "feature/storage", "main",
            GitHubPrReviewStatus.Approved, GitHubPrMergeStatus.Clean, [], 1, 0, 1, DateTimeOffset.UtcNow.AddDays(-2),
            DateTimeOffset.UtcNow.AddDays(-1), "https://github.com/owner/repo/pull/42", HeadSha: head);
        var service = new WorkspaceStorageRemovalService(_git, new FakeGitHub(pull), new ClearExternalUse());

        var review = await service.ReviewAsync(setup.Workspace, StorageEntry(setup), []);

        Assert.False(review.RemoteAncestryPassed);
        Assert.True(review.MergedPullRequestPassed);
        Assert.Equal(42, review.MergedPullRequestNumber);
        Assert.True(review.ReadyForConfirmation, string.Join(" | ", review.Findings));
        await service.RemoveAsync(review, [], true, true);
        Assert.False(Directory.Exists(setup.Worktree));
    }

    [Fact]
    public async Task DetectedOpenHandlesBlockRemovalReview()
    {
        var setup = await CreateMergedWorktreeAsync();
        var external = new WorkspaceExternalUseEvidence(true, 3, [new WorkspaceExternalProcess(1234, "Editor")], null);
        var service = new WorkspaceStorageRemovalService(_git, new FakeGitHub(null), new FixedExternalUse(external));

        var review = await service.ReviewAsync(setup.Workspace, StorageEntry(setup), []);

        Assert.True(review.ExternalUseEvidence.HasDetectedProcesses);
        Assert.False(review.ReadyForConfirmation);
        Assert.Contains(review.Findings, finding => finding.Contains("Editor (PID 1234)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReviewedPruneRemovesOnlyGitMarkedMissingRegistrations()
    {
        var setup = await CreateWorktreeAsync(mergeIntoDefault: false);
        DeleteWorkingCopy(setup.Worktree);
        var entry = StorageEntry(setup) with { Exists = false, LocalState = "Broken reference", IsReviewCandidate = false };
        var service = new WorkspaceStorageRemovalService(_git, new FakeGitHub(null), new ClearExternalUse());

        var review = await service.ReviewPruneAsync(setup.Workspace, entry);

        Assert.True(review.ReadyForConfirmation, string.Join(" | ", review.Findings));
        var reviewedRegistration = Assert.Single(review.PrunableRegistrations);
        Assert.True(PathsEqual(reviewedRegistration.Path, setup.Worktree));
        Assert.False(string.IsNullOrWhiteSpace(reviewedRegistration.AdministrativeIdentity));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PruneAsync(review, false));
        await service.PruneAsync(review, true);
        var listing = await _git.RunRawAsync(setup.Primary, ["worktree", "list", "--porcelain"]);
        Assert.DoesNotContain(NormalizeGitPath(setup.Worktree), NormalizeGitPath(listing.StdOut), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(NormalizeGitPath(setup.Primary), NormalizeGitPath(listing.StdOut), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(setup.Worktree));
        var commonDirectory = await GetCommonDirectoryAsync(setup.Primary);
        Assert.Empty(Directory.EnumerateFileSystemEntries(commonDirectory, "powerforge-studio-worktree-*"));
    }

    [Fact]
    public async Task ChangedPruneSetRequiresFreshReview()
    {
        var setup = await CreateWorktreeAsync(mergeIntoDefault: false);
        DeleteWorkingCopy(setup.Worktree);
        var entry = StorageEntry(setup) with { Exists = false, LocalState = "Broken reference", IsReviewCandidate = false };
        var service = new WorkspaceStorageRemovalService(_git, new FakeGitHub(null), new ClearExternalUse());
        var review = await service.ReviewPruneAsync(setup.Workspace, entry);
        Assert.True(review.ReadyForConfirmation, string.Join(" | ", review.Findings));
        var second = Path.Combine(setup.Workspace, "_worktrees", "second");
        await Run(setup.Primary, "worktree", "add", "-b", "feature/second", second);
        DeleteWorkingCopy(second);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PruneAsync(review, true));

        var listing = await _git.RunRawAsync(setup.Primary, ["worktree", "list", "--porcelain"]);
        Assert.Contains(NormalizeGitPath(setup.Worktree), NormalizeGitPath(listing.StdOut), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(NormalizeGitPath(second), NormalizeGitPath(listing.StdOut), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateAdministrativeRegistrationsBlockPrune()
    {
        var setup = await CreateWorktreeAsync(mergeIntoDefault: false);
        var commonDirectory = await GetCommonDirectoryAsync(setup.Primary);
        var registrations = Path.Combine(commonDirectory, "worktrees");
        var source = Assert.Single(Directory.EnumerateDirectories(registrations));
        CopyDirectory(source, Path.Combine(registrations, "duplicate"));
        DeleteWorkingCopy(setup.Worktree);
        var entry = StorageEntry(setup) with { Exists = false, LocalState = "Broken reference", IsReviewCandidate = false };
        var service = new WorkspaceStorageRemovalService(_git, new FakeGitHub(null), new ClearExternalUse());

        var review = await service.ReviewPruneAsync(setup.Workspace, entry);

        Assert.False(review.ReadyForConfirmation);
        Assert.Contains(review.Findings, finding => finding.Contains("duplicate worktree registrations", StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PruneAsync(review, true));
    }

    [Fact]
    public async Task HiddenMalformedAdministrativeRegistrationBlocksPrune()
    {
        var setup = await CreateWorktreeAsync(mergeIntoDefault: false);
        DeleteWorkingCopy(setup.Worktree);
        var commonDirectory = await GetCommonDirectoryAsync(setup.Primary);
        var hidden = Directory.CreateDirectory(Path.Combine(commonDirectory, "worktrees", "hidden-malformed")).FullName;
        await File.WriteAllTextAsync(Path.Combine(hidden, "HEAD"), new string('a', 40));
        var entry = StorageEntry(setup) with { Exists = false, LocalState = "Broken reference", IsReviewCandidate = false };
        var service = new WorkspaceStorageRemovalService(_git, new FakeGitHub(null), new ClearExternalUse());

        var review = await service.ReviewPruneAsync(setup.Workspace, entry);

        Assert.False(review.AdministrativeRegistryPassed);
        Assert.False(review.ReadyForConfirmation);
        Assert.Contains(review.Findings, finding => finding.Contains("administrative worktree registry", StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PruneAsync(review, true));
        Assert.True(Directory.Exists(hidden));
    }

    [Fact]
    public async Task AdministrativeEntryAppearingAfterReviewAbortsExactCleanup()
    {
        var setup = await CreateWorktreeAsync(mergeIntoDefault: false);
        DeleteWorkingCopy(setup.Worktree);
        var commonDirectory = await GetCommonDirectoryAsync(setup.Primary);
        var hidden = Path.Combine(commonDirectory, "worktrees", "late-malformed");
        var boundaryInvoked = false;
        var service = new WorkspaceStorageRemovalService(_git, new FakeGitHub(null), new ClearExternalUse())
        {
            BeforePruneGuardAcquisition = () =>
            {
                boundaryInvoked = true;
                Directory.CreateDirectory(hidden);
                File.WriteAllText(Path.Combine(hidden, "HEAD"), new string('b', 40));
            }
        };
        var entry = StorageEntry(setup) with { Exists = false, LocalState = "Broken reference", IsReviewCandidate = false };
        var review = await service.ReviewPruneAsync(setup.Workspace, entry);
        Assert.True(review.ReadyForConfirmation, string.Join(" | ", review.Findings));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PruneAsync(review, true));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(boundaryInvoked);
        Assert.True(Directory.Exists(hidden));
        Assert.False(File.Exists(setup.Worktree));
        var listing = await _git.RunRawAsync(setup.Primary, ["worktree", "list", "--porcelain"]);
        Assert.Contains(NormalizeGitPath(setup.Worktree), NormalizeGitPath(listing.StdOut), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SamePathReplacementAfterReviewIsPreserved()
    {
        var setup = await CreateWorktreeAsync(mergeIntoDefault: false);
        DeleteWorkingCopy(setup.Worktree);
        var sentinel = Path.Combine(setup.Worktree, ".cache", "replacement.txt");
        var boundaryInvoked = false;
        var service = new WorkspaceStorageRemovalService(_git, new FakeGitHub(null), new ClearExternalUse())
        {
            BeforePruneGuardAcquisition = () =>
            {
                boundaryInvoked = true;
                Run(setup.Primary, "worktree", "add", "-f", "--detach", setup.Worktree, "HEAD")
                    .GetAwaiter().GetResult();
                Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
                File.WriteAllText(sentinel, "replacement survives");
            }
        };
        var entry = StorageEntry(setup) with { Exists = false, LocalState = "Broken reference", IsReviewCandidate = false };
        var review = await service.ReviewPruneAsync(setup.Workspace, entry);
        Assert.True(review.ReadyForConfirmation, string.Join(" | ", review.Findings));

        var error = await Assert.ThrowsAsync<IOException>(() => service.PruneAsync(review, true));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(boundaryInvoked);
        Assert.True(File.Exists(sentinel));
        Assert.Equal("replacement survives", await File.ReadAllTextAsync(sentinel));
        var listing = await _git.RunRawAsync(setup.Primary, ["worktree", "list", "--porcelain"]);
        Assert.Contains(NormalizeGitPath(setup.Worktree), NormalizeGitPath(listing.StdOut), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepairedAdministrativeRegistrationAtMoveBoundaryIsRestored(bool relativePaths)
    {
        var setup = await CreateWorktreeAsync(mergeIntoDefault: false, relativePaths);
        var relocated = Path.Combine(setup.Workspace, "_worktrees", "relocated");
        Directory.Move(setup.Worktree, relocated);
        var sentinel = Path.Combine(relocated, ".cache", "relocated.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "repaired registration survives");
        var boundaryInvoked = false;
        var service = new WorkspaceStorageRemovalService(_git, new FakeGitHub(null), new ClearExternalUse())
        {
            BeforePruneAdministrativeMove = () =>
            {
                boundaryInvoked = true;
                Run(setup.Primary, "worktree", "repair", relocated).GetAwaiter().GetResult();
            }
        };
        var entry = StorageEntry(setup) with { Exists = false, LocalState = "Broken reference", IsReviewCandidate = false };
        var review = await service.ReviewPruneAsync(setup.Workspace, entry);
        Assert.True(review.ReadyForConfirmation, string.Join(" | ", review.Findings));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PruneAsync(review, true));

        Assert.Contains("repointed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(boundaryInvoked);
        Assert.True(File.Exists(sentinel));
        Assert.Equal("repaired registration survives", await File.ReadAllTextAsync(sentinel));
        var listing = await _git.RunRawAsync(setup.Primary, ["worktree", "list", "--porcelain"]);
        Assert.Contains(NormalizeGitPath(relocated), NormalizeGitPath(listing.StdOut), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(NormalizeGitPath(setup.Worktree), NormalizeGitPath(listing.StdOut), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Setup> CreateMergedWorktreeAsync()
        => await CreateWorktreeAsync(mergeIntoDefault: true);

    private async Task<Setup> CreateWorktreeAsync(bool mergeIntoDefault, bool relativePaths = false)
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
        if (relativePaths)
            await Run(primary, "worktree", "add", "--relative-paths", "-b", "feature/storage", worktree);
        else
            await Run(primary, "worktree", "add", "-b", "feature/storage", worktree);
        await File.WriteAllTextAsync(Path.Combine(worktree, "feature.txt"), "feature");
        await Run(worktree, "add", "feature.txt");
        await Run(worktree, "commit", "-m", "Feature");
        if (mergeIntoDefault)
        {
            await Run(primary, "merge", "--no-ff", "feature/storage", "-m", "Merge feature");
            await Run(primary, "push", "origin", "main");
        }
        return new(workspace, primary, worktree);
    }

    private static void DeleteWorkingCopy(string path)
    {
        foreach (var item in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(item);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(item, attributes & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(path, recursive: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static WorkspaceStorageEntry StorageEntry(Setup setup)
        => new("Product", setup.Worktree, setup.Primary, "feature/storage", false, true, false,
            0, 0, 0, "Clean", "main", "Locally merged", "Review removal evidence", true, null);

    private async Task<string> Head(string root)
        => (await _git.RunRawAsync(root, ["rev-parse", "HEAD"])).StdOut.Trim();

    private async Task<string> GetCommonDirectoryAsync(string root)
    {
        var common = (await _git.RunRawAsync(root, ["rev-parse", "--git-common-dir"])).StdOut.Trim();
        return Path.GetFullPath(Path.IsPathFullyQualified(common) ? common : Path.Combine(root, common));
    }

    private async Task Run(string root, params string[] arguments)
    {
        var result = await _git.RunRawAsync(root, arguments);
        Assert.True(result.Succeeded, $"git {string.Join(' ', arguments)} failed: {result.StdErr}");
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string NormalizeGitPath(string path) => path.Replace('\\', '/');

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

    private sealed class ClearExternalUse : IWorkspaceExternalUseInspectionService
    {
        public Task<WorkspaceExternalUseEvidence> InspectAsync(string workingCopy, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceExternalUseEvidence(true, 2, [], "Manual confirmation is still required."));
    }

    private sealed class FixedExternalUse(WorkspaceExternalUseEvidence evidence) : IWorkspaceExternalUseInspectionService
    {
        public Task<WorkspaceExternalUseEvidence> InspectAsync(string workingCopy, CancellationToken cancellationToken = default)
            => Task.FromResult(evidence);
    }

    private sealed class FakeGitHub(GitHubPullRequest? pull) : IGitHubProjectService
    {
        public Task<string?> ResolveRepositoryAsync(string workingCopy, CancellationToken cancellationToken = default) => Task.FromResult<string?>("owner/repo");
        public Task<GitHubPullRequest?> FindMergedPullRequestByHeadAsync(string slug, string headSha, string expectedBaseBranch, CancellationToken cancellationToken = default)
            => Task.FromResult(pull is not null && string.Equals(pull.HeadSha, headSha, StringComparison.OrdinalIgnoreCase) &&
                                             string.Equals(pull.BaseBranch, expectedBaseBranch, StringComparison.Ordinal) ? pull : null);
        public Task<GitHubPullRequestFiles> FetchPullRequestFilesAsync(string slug, int number, string expectedHeadSha, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitHubPage<GitHubCheck>> FetchChecksAsync(string slug, string headSha, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitHubPage<GitHubIssue>> FetchIssuesAsync(string slug, string state = "open", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitHubPage<GitHubPullRequest>> FetchPullRequestsAsync(string slug, string state = "open", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitHubIssueDetail?> FetchIssueDetailAsync(string slug, int issueNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitHubPullRequestDetail?> FetchPullRequestDetailAsync(string slug, int pullRequestNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

}
