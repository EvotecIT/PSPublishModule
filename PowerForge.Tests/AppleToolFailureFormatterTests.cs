using Xunit;

namespace PowerForge.Tests;

public sealed class AppleToolFailureFormatterTests
{
    [Fact]
    public void Format_PreservesXcodeErrorsAfterLongBuildOutputAndRedactsBeforeTruncation()
    {
        var secret = "KEY_PREFIX_" + new string('s', 4000) + "_KEY_SUFFIX";
        var output = new string('x', 6000) + "\nproject: error: Authentication failed for " + secret +
            "\nproject: error: Provisioning profile doesn't include the App Groups capability.";
        var diagnostic = AppleToolFailureFormatter.Format(Failure(output, "** ARCHIVE FAILED **"), new[] { secret });

        Assert.Contains("Authentication failed", diagnostic);
        Assert.Contains("App Groups capability", diagnostic);
        Assert.Contains("[REDACTED]", diagnostic);
        Assert.DoesNotContain("KEY_PREFIX", diagnostic);
        Assert.DoesNotContain("KEY_SUFFIX", diagnostic);
        Assert.True(diagnostic.Length <= 2000);
    }

    [Fact]
    public void Format_PreservesBothUnstructuredStreamsWithinItsBound()
    {
        var diagnostic = AppleToolFailureFormatter.Format(
            Failure("stdout start" + new string('x', 10000) + "stdout end", "stderr start" + new string('x', 10000) + "stderr end"),
            Array.Empty<string>());

        Assert.Contains("stdout start", diagnostic);
        Assert.Contains("stdout end", diagnostic);
        Assert.Contains("stderr start", diagnostic);
        Assert.Contains("stderr end", diagnostic);
        Assert.True(diagnostic.Length <= 2000);
    }

    [Fact]
    public void Format_RedactsPrivateKeyBlocksAndEncodedCredentials()
    {
        var credential = "private\"credential";
        var output = "project: error: " + System.Text.Json.JsonEncodedText.Encode(credential) +
            "\n-----BEGIN PRIVATE KEY-----\nPRIVATE_BYTES\n-----END PRIVATE KEY-----";
        var diagnostic = AppleToolFailureFormatter.Format(Failure(output, ""), new[] { credential });

        Assert.DoesNotContain("credential", diagnostic);
        Assert.DoesNotContain("PRIVATE_BYTES", diagnostic);
        Assert.Contains("[REDACTED]", diagnostic);
    }

    [Fact]
    public void Classify_ProvisioningFailureDoesNotTreatXcodeCredentialFlagsAsAuthenticationFailure()
    {
        var diagnostics = AppleReleaseFailureClassifier.Classify("xcodebuild failed with exit code 65.",
            standardOutput: "/usr/bin/xcodebuild -authenticationKeyPath [REDACTED] -authenticationKeyID [REDACTED]\n" +
                "project: error: Provisioning profile doesn't include the App Groups capability.");

        Assert.Equal("APPLE_PROVISIONING", Assert.Single(diagnostics).Code);
    }

    private static ProcessRunResult Failure(string stdout, string stderr)
        => new(65, stdout, stderr, "xcodebuild", TimeSpan.FromSeconds(1), false);
}
