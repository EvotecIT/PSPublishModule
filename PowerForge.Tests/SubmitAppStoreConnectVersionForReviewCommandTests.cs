namespace PowerForge.Tests;

public sealed class SubmitAppStoreConnectVersionForReviewCommandTests
{
    [Fact]
    public void DeserializeScreenshotSpec_AcceptsStringPlatform()
    {
        var spec = PSPublishModule.SubmitAppStoreConnectVersionForReviewCommand.DeserializeScreenshotSpec(
            """
            {
              "appId": "6778025328",
              "versionString": "1.2.0",
              "platform": "macOS",
              "screenshotSets": []
            }
            """,
            "screenshots.json");

        Assert.Equal(ApplePlatform.macOS, spec.Platform);
    }
}
