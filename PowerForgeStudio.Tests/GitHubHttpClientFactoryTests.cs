using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Tests;

public sealed class GitHubHttpClientFactoryTests
{
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
