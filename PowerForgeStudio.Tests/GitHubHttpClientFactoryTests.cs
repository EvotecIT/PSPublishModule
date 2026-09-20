using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Tests;

public sealed class GitHubHttpClientFactoryTests
{
    [Theory]
    [InlineData(false, false, "example-test-token")]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    public async Task CliDiscoveryUsesBoundedSharedRunnerAndExplicitGitHubHost(bool timedOut, bool truncated, string? expected)
    {
        var runner = new TokenRunner(timedOut, truncated);
        Assert.Equal(expected, await GitHubHttpClientFactory.TryGetGhCliTokenAsync(runner));
        Assert.NotEmpty(runner.Requests);
        foreach (var request in runner.Requests)
        {
            Assert.Equal(new[] { "auth", "token", "--hostname", "github.com" }, request.Arguments);
            Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout);
            Assert.Equal(8192, request.MaxCapturedOutputCharacters);
            Assert.Null(request.OutputLineReceived);
            Assert.Null(request.ErrorLineReceived);
        }
    }

    private sealed class TokenRunner(bool timedOut, bool truncated) : PowerForge.IProcessRunner
    {
        public List<PowerForge.ProcessRunRequest> Requests { get; } = [];
        public Task<PowerForge.ProcessRunResult> RunAsync(PowerForge.ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new PowerForge.ProcessRunResult(0, "example-test-token", "", request.FileName,
                TimeSpan.Zero, timedOut, standardOutputLimitExceeded: truncated));
        }
    }

    [Fact]
    public void Create_SetsCorrectBaseAddress()
    {
        using var client = GitHubHttpClientFactory.Create();
        Assert.Equal(new Uri("https://api.github.com"), client.BaseAddress);
    }

    [Fact]
    public void Create_SetsUserAgentHeader()
    {
        using var client = GitHubHttpClientFactory.Create();
        var userAgent = client.DefaultRequestHeaders.UserAgent.ToString();
        Assert.Contains("PowerForgeStudio", userAgent);
    }
}
