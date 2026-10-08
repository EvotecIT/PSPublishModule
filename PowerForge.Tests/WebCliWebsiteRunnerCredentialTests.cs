using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebCliWebsiteRunnerCredentialTests
{
    [Theory]
    [InlineData("", null, "job-token", "job-token")]
    [InlineData(" ", "", "job-token", "job-token")]
    [InlineData("repository-token", "cli-token", "job-token", "repository-token")]
    [InlineData(null, "cli-token", "job-token", "cli-token")]
    [InlineData("", " ", null, null)]
    public void BinaryBootstrapUsesFirstNonemptyCredentialWithoutLosingExplicitPrecedence(
        string? repositoryToken, string? cliToken, string? jobToken, string? expected)
    {
        Assert.Equal(expected, WebCliCommandHandlers.ResolveWebsiteRunnerGitHubToken(repositoryToken, cliToken, jobToken));
    }
}
