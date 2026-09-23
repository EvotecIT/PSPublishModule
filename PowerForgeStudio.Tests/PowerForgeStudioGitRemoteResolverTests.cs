using PowerForge;
using PowerForgeStudio.Orchestrator.Git;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioGitRemoteResolverTests
{
    [Fact]
    public async Task ResolveOriginUrlAsync_UsesSharedGitClientContract()
    {
        string? capturedRepositoryRoot = null;
        string? capturedRemoteName = null;
        var resolver = new GitRemoteResolver((repositoryRoot, remoteName, _) => {
            capturedRepositoryRoot = repositoryRoot;
            capturedRemoteName = remoteName;
            return Task.FromResult(new GitCommandResult(
                GitCommandKind.GetRemoteUrl,
                repositoryRoot,
                "git remote get-url origin",
                0,
                "https://github.com/EvotecIT/PSPublishModule.git",
                string.Empty,
                "git",
                TimeSpan.Zero,
                timedOut: false));
        });
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeStudio.Tests", Guid.NewGuid().ToString("N"))).FullName;

        try
        {
            Assert.True((await new GitClient().RunRawAsync(repositoryRoot, ["init", "-b", "main"])).Succeeded);
            var url = await resolver.ResolveOriginUrlAsync(repositoryRoot);

            Assert.Equal(repositoryRoot, capturedRepositoryRoot);
            Assert.Equal("origin", capturedRemoteName);
            Assert.Equal("https://github.com/EvotecIT/PSPublishModule.git", url);
        }
        finally
        {
            try { Directory.Delete(repositoryRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ResolveOriginUrlAsync_PropagatesCallerCancellation()
    {
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-origin-cancel-" + Guid.NewGuid().ToString("N"))).FullName;
        using var cancellation = new CancellationTokenSource();
        var resolver = new GitRemoteResolver(async (_, _, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable");
        });
        try
        {
            Assert.True((await new GitClient().RunRawAsync(repositoryRoot, ["init", "-b", "main"])).Succeeded);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                resolver.ResolveOriginUrlAsync(repositoryRoot, cancellation.Token));
        }
        finally { Directory.Delete(repositoryRoot, recursive: true); }
    }

    [Fact]
    public async Task ResolveOriginUrlAsync_DoesNotInheritEnclosingRepositoryRemote()
    {
        var parent = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-origin-parent-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Assert.True((await new GitClient().RunRawAsync(parent, ["init", "-b", "main"])).Succeeded);
            Assert.True((await new GitClient().RunRawAsync(parent,
                ["remote", "add", "origin", "https://github.com/EvotecIT/Parent.git"])).Succeeded);
            var child = Directory.CreateDirectory(Path.Combine(parent, "OrdinaryProject")).FullName;

            Assert.Null(await new GitRemoteResolver().ResolveOriginUrlAsync(child));
            Assert.False((await new GitRepositoryInspector().InspectAsync(child)).IsGitRepository);
            Assert.Equal("https://github.com/EvotecIT/Parent.git",
                await new GitRemoteResolver().ResolveOriginUrlAsync(parent));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
}
