namespace PowerForge.Tests;

public sealed partial class GitHubServerRecoveryValidationSecurityTests
{
    [Theory]
    [InlineData("sudo -n /usr/bin/cat /var/www/example/current/.deploy-info.env | sed -n -E 's/^engine_commit=([0-9a-fA-F]{40})$/\\1/p'")]
    public void Validator_ShouldRequireExactCatGrant(string command)
    {
        var sudoers = BuildExpectedSudoers(CaptureUser, "root")
            .Replace(ExpectedInspectCommand, "/usr/bin/cat /var/www/example/current/.deploy-info.env", StringComparison.Ordinal);
        var result = RunValidator(sudoers: sudoers, captureCommand: command, captureCommandHydratesRef: true);
        Assert.True(result.ExitCode == 0, result.AllOutput);
    }

    [Theory]
    [InlineData("sudo -n /usr/bin/cat /etc/example/../secret")]
    [InlineData("sudo -n /usr/bin/cat /etc/example/state | sh")]
    [InlineData("sudo -n /usr/bin/cat /etc/example/state; id")]
    [InlineData("sudo -n /usr/bin/cat /var/www/example/current/.deploy-info.env")]
    [InlineData("sudo -n /usr/bin/cat /etc/shadow | sed -n -E 's/^engine_commit=([0-9a-fA-F]{40})$/\\1/p'")]
    [InlineData("sudo -n /usr/bin/cat /var/www/../current/.deploy-info.env | sed -n -E 's/^engine_commit=([0-9a-fA-F]{40})$/\\1/p'")]
    [InlineData("sudo -n /usr/bin/cat /var/www/example/current/.deploy-info.env | sed -n -E 's/^password=([0-9a-fA-F]{40})$/\\1/p'")]
    public void Validator_ShouldRejectNoncanonicalOrExecutableCatSuffix(string command)
    {
        var result = RunValidator(captureCommand: command, captureCommandHydratesRef: true);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Validator_ShouldRejectRevisionReadWithoutRepositoryRefBinding()
    {
        var command = "sudo -n /usr/bin/cat /var/www/example/current/.deploy-info.env | sed -n -E 's/^engine_commit=([0-9a-fA-F]{40})$/\\1/p'";
        var result = RunValidator(captureCommand: command);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("explicitly declared repository ref", result.AllOutput, StringComparison.Ordinal);
    }
}
