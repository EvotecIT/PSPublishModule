using System.Diagnostics;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Tests;

public sealed class WorkspaceProjectChangeServiceTests
{
    [Fact]
    public async Task InspectionIgnoresBareCommonRepositoryForLinkedWorkingCopy()
    {
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-change-bare-" + Guid.NewGuid().ToString("N"))).FullName;
        var source = Directory.CreateDirectory(Path.Combine(fixture, "Source")).FullName;
        var bare = Path.Combine(fixture, "Common.git");
        var linked = Path.Combine(fixture, "Linked");
        try
        {
            InitializeRepository(source);
            RunGit(fixture, "clone", "--quiet", "--bare", source, bare);
            RunGit(fixture, $"--git-dir={bare}", "worktree", "add", "--quiet", linked, "main");
            await File.AppendAllTextAsync(Path.Combine(linked, "tracked.txt"), "linked change\n");

            var snapshot = await new WorkspaceProjectChangeService().InspectAsync([Entry("Product", linked)]);

            var changed = Assert.Single(snapshot.ChangedProjects);
            var copy = Assert.Single(changed.Value);
            Assert.True(SamePath(linked, copy.Path));
            Assert.Equal(1, copy.ChangeCount);
            Assert.Empty(snapshot.UnavailableWorkingCopies);
        }
        finally { DeleteFixture(fixture); }
    }

    [Fact]
    public async Task InspectionUsesBoundedPurposeSpecificGitProbes()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-change-bounds-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var runner = new RecordingRunner(request => request.Arguments[0] switch
            {
                "status" => new ProcessRunResult(0, "# branch.head main\0? large-tree\0", "", "git", TimeSpan.Zero,
                    timedOut: false, standardOutputLimitExceeded: true),
                "worktree" => new ProcessRunResult(0, $"worktree {root}\0HEAD {new string('a', 40)}\0branch refs/heads/main\0", "", "git", TimeSpan.Zero, false),
                _ => throw new InvalidOperationException("Unexpected Git command: " + string.Join(' ', request.Arguments))
            });

            var snapshot = await new WorkspaceProjectChangeService(new GitClient(runner)).InspectAsync([Entry("Product", root)]);

            var changed = Assert.Single(snapshot.ChangedProjects);
            Assert.Equal(1, Assert.Single(changed.Value).ChangeCount);
            Assert.Equal(2, runner.Requests.Count);
            var status = Assert.Single(runner.Requests, request => request.Arguments[0] == "status");
            Assert.Equal(WorkspaceProjectChangeService.StatusOutputLimit, status.MaxCapturedOutputCharacters);
            Assert.Contains("--untracked-files=normal", status.Arguments);
            var worktrees = Assert.Single(runner.Requests, request => request.Arguments[0] == "worktree");
            Assert.Equal(WorkspaceProjectChangeService.WorktreeOutputLimit, worktrees.MaxCapturedOutputCharacters);
            Assert.DoesNotContain(runner.Requests, request => request.Arguments[0] == "branch");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InspectionSkipsLocalProjectsWithoutBorrowingParentGitState()
    {
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-change-local-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var standalone = Directory.CreateDirectory(Path.Combine(fixture, "Standalone")).FullName;
            var parent = Directory.CreateDirectory(Path.Combine(fixture, "ParentGit")).FullName;
            InitializeRepository(parent);
            var nested = Directory.CreateDirectory(Path.Combine(parent, "NestedLocal")).FullName;
            await File.AppendAllTextAsync(Path.Combine(parent, "tracked.txt"), "parent change\n");

            var snapshot = await new WorkspaceProjectChangeService().InspectAsync(
                [Entry("Standalone", standalone), Entry("NestedLocal", nested)]);

            Assert.Empty(snapshot.ChangedProjects);
            Assert.Empty(snapshot.UnavailableWorkingCopies);
        }
        finally { DeleteFixture(fixture); }
    }

    [Fact]
    public async Task InspectionFindsChangesInPrimaryAndRegisteredWorktree()
    {
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-change-scan-" + Guid.NewGuid().ToString("N"))).FullName;
        var repository = Directory.CreateDirectory(Path.Combine(fixture, "Product")).FullName;
        var worktree = Path.Combine(fixture, "Product-feature");
        var clean = Directory.CreateDirectory(Path.Combine(fixture, "Clean")).FullName;
        try
        {
            InitializeRepository(repository);
            InitializeRepository(clean);
            RunGit(repository, "worktree", "add", "--quiet", "-b", "feature/sample", worktree);
            await File.AppendAllTextAsync(Path.Combine(repository, "tracked.txt"), "primary change\n");
            await File.WriteAllTextAsync(Path.Combine(worktree, "untracked.txt"), "worktree change\n");
            var catalog = new[] { Entry("Product", repository), Entry("Clean", clean) };

            var snapshot = await new WorkspaceProjectChangeService().InspectAsync(catalog);

            var changed = Assert.Single(snapshot.ChangedProjects);
            Assert.Equal(repository, changed.Key, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Assert.Equal(2, changed.Value.Count);
            Assert.Contains(changed.Value, item => SamePath(item.Path, repository) && item.ChangeCount == 1);
            Assert.Contains(changed.Value, item => SamePath(item.Path, worktree) && item.ChangeCount == 1);
            Assert.Empty(snapshot.UnavailableWorkingCopies);
        }
        finally
        {
            DeleteFixture(fixture);
        }
    }

    private static RepositoryCatalogEntry Entry(string name, string root) => new(
        name, root, ReleaseRepositoryKind.Unknown, ReleaseWorkspaceKind.PrimaryRepository,
        null, null, false, false);

    private static void InitializeRepository(string root)
    {
        RunGit(root, "init", "--quiet", "-b", "main");
        RunGit(root, "config", "user.name", "PowerForge Studio Tests");
        RunGit(root, "config", "user.email", "studio-tests@example.invalid");
        File.WriteAllText(Path.Combine(root, "tracked.txt"), "initial\n");
        RunGit(root, "add", ".");
        RunGit(root, "commit", "--quiet", "-m", "Initial fixture");
    }

    private static void RunGit(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void DeleteFixture(string fixture)
    {
        if (!Directory.Exists(fixture)) return;
        foreach (var directory in new DirectoryInfo(fixture).EnumerateDirectories("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            if ((directory.Attributes & FileAttributes.Hidden) != 0) directory.Attributes &= ~FileAttributes.Hidden;
        foreach (var file in new DirectoryInfo(fixture).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            if (file.IsReadOnly) file.IsReadOnly = false;
        Directory.Delete(fixture, recursive: true);
    }

    private sealed class RecordingRunner(Func<ProcessRunRequest, ProcessRunResult> result) : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(result(request));
        }
    }
}
