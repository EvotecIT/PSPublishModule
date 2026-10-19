namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ReleaseSourceCommitEnvironmentTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public async Task Capture_UsesConfiguredPathRatherThanFallingBackToFixedSystemGit()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-path-git-" + Guid.NewGuid().ToString("N"))).FullName;
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var git = new GitClient();
            Assert.True((await git.RunRawAsync(root, ["init", "--quiet"])).Succeeded);
            Assert.True((await git.RunRawAsync(root,
            ["-c", "user.name=Release test", "-c", "user.email=release@example.test",
             "-c", "commit.gpgsign=false", "-c", "core.hooksPath=" + (OperatingSystem.IsWindows() ? "NUL" : "/dev/null"),
             "commit", "--allow-empty", "--quiet", "-m", "source"])).Succeeded);
            var commit = ReleaseSourceCommit.Capture(root);
            Assert.True(GitObjectId.IsFull(commit));

            // Removing Git from this test's PATH must not select a different fixed
            // installation. The process seam separately proves ordinary PATH routing.
            Environment.SetEnvironmentVariable("PATH", root);
            Assert.Throws<InvalidOperationException>(() => ReleaseSourceCommit.Capture(root));
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Assert.Equal(commit, ReleaseSourceCommit.Capture(root));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            var objects = Path.Combine(root, ".git", "objects");
            if (Directory.Exists(objects))
                foreach (var file in Directory.EnumerateFiles(objects, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(root, true);
        }
    }
}
