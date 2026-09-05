using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationWorkspaceTests
{
    [Fact]
    public void Create_DefaultWorkspaceIsDeletedOnDispose()
    {
        string path;
        using (var workspace = PowerShellCompilationWorkspace.Create(keep: false))
        {
            path = workspace.Path;
            File.WriteAllText(Path.Combine(path, "payload.txt"), "payload");
            Assert.True(Directory.Exists(path));
        }

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Create_RetainedWorkspaceSurvivesDisposalAndScavenging()
    {
        string path;
        using (var workspace = PowerShellCompilationWorkspace.Create(keep: true))
            path = workspace.Path;
        try
        {
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
            PowerShellCompilationWorkspace.CleanupStaleWorkspaces(
                Path.GetDirectoryName(path)!,
                DateTime.UtcNow.AddHours(-1));

            Assert.True(Directory.Exists(path));
            Assert.True(File.Exists(Path.Combine(path, ".powerforge-keep")));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void CleanupStaleWorkspaces_RemovesOnlyUnlockedCompilerOwnedDirectories()
    {
        using var fixture = new WorkspaceFixture();
        var stale = fixture.Create("ps-stale", old: true);
        var recent = fixture.Create("ps-recent", old: false);
        var kept = fixture.Create("ps-kept", old: true);
        File.WriteAllText(Path.Combine(kept, ".powerforge-keep"), "keep");
        var unrelated = fixture.Create("other-stale", old: true);
        var unowned = fixture.Create("ps-unowned", old: true, owned: false);
        var active = fixture.Create("ps-active", old: true);
        using var activeLock = new FileStream(
            Path.Combine(active, ".powerforge-active.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        var removed = PowerShellCompilationWorkspace.CleanupStaleWorkspaces(
            fixture.RootPath,
            DateTime.UtcNow.AddHours(-1));

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(recent));
        Assert.True(Directory.Exists(kept));
        Assert.True(Directory.Exists(unrelated));
        Assert.True(Directory.Exists(unowned));
        Assert.True(Directory.Exists(active));
    }

    private sealed class WorkspaceFixture : IDisposable
    {
        internal WorkspaceFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PowerForge Compilation Workspace Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        internal string RootPath { get; }

        internal string Create(string name, bool old, bool owned = true)
        {
            var path = Path.Combine(RootPath, name);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "payload.txt"), "payload");
            if (owned) File.WriteAllText(Path.Combine(path, ".powerforge-compiler-workspace"), "owned");
            if (old) Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
    }
}
