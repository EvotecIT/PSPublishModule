using System.Security.Cryptography;
using System.Text;

namespace PowerForge.Tests;

public sealed class AppStoreConnectWebhookVerifierTests
{
    [Fact]
    public void VerifyAndParse_AuthenticatesBuildUploadFailureAndRequestsRefresh()
    {
        const string secret = "a-strong-test-secret";
        const string json = """
            {
              "data": {
                "type": "buildUploadStateUpdated",
                "id": "event-1",
                "version": 1,
                "attributes": {
                  "newState": "FAILED",
                  "timestamp": "2026-07-28T08:30:00Z"
                },
                "relationships": {
                  "instance": {
                    "data": { "type": "buildUploads", "id": "upload-1" }
                  }
                }
              }
            }
            """;
        var payload = Encoding.UTF8.GetBytes(json);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = "hmacsha256=" + Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();

        var notification = new AppStoreConnectWebhookVerifier().VerifyAndParse(payload, signature, secret);

        Assert.Equal("buildUploadStateUpdated", notification.Type);
        Assert.Equal("upload-1", notification.InstanceId);
        Assert.Equal("FAILED", notification.NewState);
        Assert.True(notification.IsFailure);
        Assert.True(notification.ShouldRefreshReleaseState);
        Assert.Contains(notification.NextActions, action => action.Contains("Doctor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VerifyAndParse_RejectsTamperedPayload()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AppStoreConnectWebhookVerifier().VerifyAndParse(
                Encoding.UTF8.GetBytes("{\"data\":{}}"),
                "hmacsha256=" + new string('0', 64),
                "a-strong-test-secret"));

        Assert.Contains("signature validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyAndParse_RequestsStateRefreshForBetaFeedback()
    {
        const string secret = "a-strong-test-secret";
        const string json = """
            {
              "data": {
                "type": "betaFeedbackCrashSubmissionCreated",
                "id": "event-feedback-1",
                "version": 1,
                "attributes": { "timestamp": "2026-07-28T08:30:00Z" }
              }
            }
            """;
        var payload = Encoding.UTF8.GetBytes(json);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = "hmacsha256=" + Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();

        var notification = new AppStoreConnectWebhookVerifier().VerifyAndParse(payload, signature, secret);

        Assert.True(notification.ShouldRefreshReleaseState);
        Assert.Contains(notification.NextActions, action => action.Contains("feedback", StringComparison.OrdinalIgnoreCase));
    }
}
