using PowerForge;
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
}
