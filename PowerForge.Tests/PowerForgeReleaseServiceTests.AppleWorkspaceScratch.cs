namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppleReleaseSourceSnapshot_accepts_normal_xcode_workspace_container_creation(bool standaloneWorkspace)
    {
        var root = CreateSandbox();
        try
        {
            var workspace = CreateSnapshotWorkspaceFixture(root, standaloneWorkspace);
            using var snapshot = CreateWorkspaceSnapshot(root);
            using var monitor = snapshot.MonitorChanges();
            // Xcode performs this even for projects without remote packages.
            Directory.CreateDirectory(Path.Combine(snapshot.RootPath, workspace, "xcshareddata", "swiftpm", "configuration"));

            monitor.ValidateNoChanges(snapshot.ValidateUnchanged);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void AppleReleaseSourceSnapshot_rejects_ignored_workspace_state_inserted_before_monitoring()
    {
        var root = CreateSandbox();
        try
        {
            var workspace = CreateSnapshotWorkspaceFixture(root, false);
            using var snapshot = CreateWorkspaceSnapshot(root);
            var metadata = Path.Combine(snapshot.RootPath, workspace, "xcshareddata", "swiftpm", "configuration");
            Directory.CreateDirectory(metadata);
            File.WriteAllText(Path.Combine(metadata, "mirrors.json"), "unbound package configuration");

            var error = Assert.Throws<InvalidOperationException>(() => snapshot.MonitorChanges());

            Assert.Contains("untracked state", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { TryDelete(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppleReleaseSourceSnapshot_rejects_transient_workspace_file_changes(bool trackedLock)
    {
        var root = CreateSandbox();
        try
        {
            var workspace = CreateSnapshotWorkspaceFixture(root, false, trackedLock);
            using var snapshot = CreateWorkspaceSnapshot(root);
            var metadata = Path.Combine(snapshot.RootPath, workspace, "xcshareddata", "swiftpm");
            Directory.CreateDirectory(metadata);
            using var monitor = snapshot.MonitorChanges();
            var path = Path.Combine(metadata, trackedLock ? "Package.resolved" : "unexpected.json");
            var original = trackedLock ? File.ReadAllText(path) : null;
            File.WriteAllText(path, "transient unbound dependency state");
            if (trackedLock) File.WriteAllText(path, original);
            else File.Delete(path);

            var error = Assert.Throws<InvalidOperationException>(() => monitor.ValidateNoChanges(snapshot.ValidateUnchanged));

            Assert.Contains("snapshot changed while xcodebuild", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void AppleReleaseSourceSnapshot_rejects_linked_workspace_package_metadata()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = CreateSandbox();
        var outside = root + "-outside";
        try
        {
            var workspace = CreateSnapshotWorkspaceFixture(root, false);
            Directory.CreateDirectory(outside);
            var shared = Path.Combine(root, workspace, "xcshareddata");
            Directory.CreateDirectory(shared);
            Directory.CreateSymbolicLink(Path.Combine(shared, "swiftpm"), outside);
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "linked metadata");

            var error = Assert.Throws<InvalidOperationException>(() => CreateWorkspaceSnapshot(root));

            Assert.Contains("symbolic link", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { TryDelete(root); TryDelete(outside); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppleReleaseSourceSnapshot_rejects_workspace_directory_alias_before_monitoring(bool linkParent)
    {
        if (OperatingSystem.IsWindows()) return;
        var root = CreateSandbox();
        var outside = root + "-outside";
        try
        {
            var workspace = CreateSnapshotWorkspaceFixture(root, false);
            using var snapshot = CreateWorkspaceSnapshot(root);
            var relative = linkParent ? "xcshareddata" : Path.Combine("xcshareddata", "swiftpm");
            var directory = Path.Combine(snapshot.RootPath, workspace, relative);
            // Preserve the prepared directory's inode while replacing its pathname
            // with an alias. Content/identity equality alone cannot admit the link.
            Directory.Move(directory, outside);
            Directory.CreateSymbolicLink(directory, outside);

            var error = Assert.Throws<InvalidOperationException>(() => snapshot.MonitorChanges());

            Assert.Contains("symbolic link", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { TryDelete(root); TryDelete(outside); }
    }

    private static string CreateSnapshotWorkspaceFixture(string root, bool standaloneWorkspace, bool trackedLock = false)
    {
        CreateXcodeProject(root, "Sample.xcodeproj", "1.2.0", "9");
        var relative = standaloneWorkspace ? "Sample.xcworkspace" : Path.Combine("Sample.xcodeproj", "project.xcworkspace");
        var workspace = Path.Combine(root, relative);
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "contents.xcworkspacedata"), "<Workspace version=\"1.0\"/>");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "**/swiftpm/**\n");
        if (trackedLock)
        {
            var metadata = Path.Combine(workspace, "xcshareddata", "swiftpm");
            Directory.CreateDirectory(metadata);
            File.WriteAllText(Path.Combine(metadata, "Package.resolved"), "{\"pins\":[],\"version\":3}");
        }
        RunSnapshotGit(root, "init", "--quiet");
        RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
        RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
        RunSnapshotGit(root, "add", ".");
        if (trackedLock) RunSnapshotGit(root, "add", "--force", Path.Combine(relative, "xcshareddata", "swiftpm", "Package.resolved"));
        RunSnapshotGit(root, "commit", "--quiet", "-m", "workspace source");
        return relative;
    }

    private static AppleReleaseSourceSnapshot CreateWorkspaceSnapshot(string root)
        => AppleReleaseSourceSnapshot.CreateIfRequired(new PowerForgeAppleReleasePlan
        {
            ProjectRoot = root,
            SourceCommit = RunSnapshotGit(root, "rev-parse", "HEAD").Trim(),
            RequireImmutableSourceSnapshot = true,
            Archive = true
        })!;
}
