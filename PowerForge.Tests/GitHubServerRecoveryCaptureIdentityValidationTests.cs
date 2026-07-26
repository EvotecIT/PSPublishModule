namespace PowerForge.Tests;

public sealed partial class GitHubServerRecoveryValidationSecurityTests
{
    [Fact]
    public void Validator_ShouldRejectCallerControlledEncryptionHelpers()
    {
        var result = RunValidator(helperFromCaller: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exact managed helper from the pinned PowerForge engine", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectRepositoryPathsThatShadowTheEngineHelper()
    {
        var result = RunValidator(shadowEngineHelper: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exact managed helper from the pinned PowerForge engine", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRequireManagedCaptureAccount()
    {
        var result = RunValidator(includeCaptureAccount: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exactly one managed capture account", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectUnrestrictedCaptureAuthorizedKey()
    {
        var result = RunValidator(
            authorizedKeyContent: RestrictedCaptureKey.Replace("restrict ", string.Empty, StringComparison.Ordinal));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exactly one restrict-prefixed Ed25519 public key", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectMultipleCaptureAuthorizedKeys()
    {
        var result = RunValidator(authorizedKeyContent: RestrictedCaptureKey + "\n" + RestrictedCaptureKey);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exactly one restrict-prefixed Ed25519 public key", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRequireRootOwnedAuthorizedKeyMetadata()
    {
        var result = RunValidator(authorizedKeyOwner: CaptureUser);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("one managed root-owned mode-644 authorized_keys file", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRejectRootOwnedAuthorizedKeyThatSshdCannotReadAfterPrivilegeDrop()
    {
        var result = RunValidator(authorizedKeyMode: "600");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("so sshd can read it after privilege drop", result.AllOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRequireRootControlledCaptureAccountDirectories()
    {
        var result = RunValidator(captureDirectoryOwner: CaptureUser);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("root-controlled mode-755 account home and key directories", result.AllOutput, StringComparison.Ordinal);
    }
}
