namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_forwards_cancellation_to_legacy_tool_runner()
    {
        using var cancellation = new CancellationTokenSource();
        var tokenObserved = false;
        var service = new PowerForgeReleaseService(
            new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
            planTools: (_, _, _) => new PowerForgeToolReleasePlan(),
            runTools: _ => throw new InvalidOperationException("The cancellation-aware tool runner should be used."),
            loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
            planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
            runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
            publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
            runToolsWithProgressAndCancellation: (_, _, token) =>
            {
                tokenObserved = true;
                Assert.Equal(cancellation.Token, token);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Cancellation was not observed.");
            });

        Assert.ThrowsAny<OperationCanceledException>(() => service.Execute(
            new PowerForgeReleaseSpec { Tools = new PowerForgeToolReleaseSpec() },
            new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                CancellationToken = cancellation.Token
            }));
        Assert.True(tokenObserved);
    }
}
