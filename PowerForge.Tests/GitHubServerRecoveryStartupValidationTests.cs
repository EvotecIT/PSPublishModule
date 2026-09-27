namespace PowerForge.Tests;

public sealed partial class GitHubServerRecoveryValidationSecurityTests
{
    [Theory]
    [InlineData("/bin/bash")]
    [InlineData("/usr/bin/bash")]
    public void Validator_AcceptsRootControlledCommentOnlyBashStartup(string shell)
    {
        var result = RunValidator(captureShell: shell, includeCaptureStartup: true);
        Assert.True(result.ExitCode == 0, result.AllOutput);
    }

    [Fact]
    public void Validator_RejectsMissingBashStartupContract()
    {
        var result = RunValidator(captureShell: "/bin/bash");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("root-owned mode-644 .bashrc", result.AllOutput);
    }

    [Theory]
    [InlineData("powerforge-example-backup", "644", "# safe\n")]
    [InlineData("root", "664", "# safe\n")]
    [InlineData("root", "644", "source ~/.profile\n")]
    [InlineData("root", "644", "sed() { cat; }\n")]
    [InlineData("root", "644", "\u00a0#comment; printf LEAK\n")]
    public void Validator_RejectsMutableOrExecutableBashStartup(string owner, string mode, string content)
    {
        var result = RunValidator(captureShell: "/bin/bash", includeCaptureStartup: true,
            captureStartupOwner: owner, captureStartupMode: mode, captureStartupContent: content);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(".bashrc", result.AllOutput);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/bin/zsh")]
    public void Validator_RejectsUnspecifiedOrUnsupportedInboundShell(string shell)
    {
        var result = RunValidator(captureShell: shell);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("explicit supported sh or bash", result.AllOutput);
    }
}
