namespace PowerForge.Tests;

public sealed partial class GitHubServerRecoveryValidationSecurityTests
{
    [Theory]
    [InlineData("sudo -n /usr/bin/cat /etc/example/deploy@website.env")]
    [InlineData("sudo -n /usr/bin/cat /etc/example/deploy@website.env | sed -n -E 's/^engine_commit=([0-9a-fA-F]{40})$/\\1/p'")]
    public void Validator_ShouldRequireExactCatGrant(string command)
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root")
            .Replace(ExpectedInspectCommand, "/usr/bin/cat /etc/example/deploy@website.env", StringComparison.Ordinal);
        var result = RunValidator(sudoers: sudoers, captureCommand: command);
        Assert.True(result.ExitCode == 0, result.AllOutput);
    }

    [Theory]
    [InlineData("sudo -n /usr/bin/cat /etc/example/../secret")]
    [InlineData("sudo -n /usr/bin/cat /etc/example/state | sh")]
    [InlineData("sudo -n /usr/bin/cat /etc/example/state; id")]
    public void Validator_ShouldRejectNoncanonicalOrExecutableCatSuffix(string command)
    {
        var result = RunValidator(captureCommand: command);
        Assert.NotEqual(0, result.ExitCode);
    }
}
