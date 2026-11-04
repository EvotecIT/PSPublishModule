namespace PowerForge.Tests;

public sealed class AppleExecutionFingerprintCompatibilityTests
{
    // Golden identities captured from the pre-entitlement release implementation.
    [Theory]
    [InlineData(false, "0C0261D1E4B35538000A42A785BE82DACB67FD8D4658254E09EA414C532791F5")]
    [InlineData(true, "3EBB59135D362EF99632B17D0D62475FA6CD53E600DA3DCD6DCE5C2D2E8A01F3")]
    public void Empty_entitlement_policy_preserves_persisted_execution_identity(bool direct, string legacySha256)
    {
        var root = Path.GetTempPath();
        var plan = new PowerForgeAppleReleasePlan
        {
            ProjectRoot = root,
            SourceCommit = "0123456789abcdef0123456789abcdef01234567"
        };
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            Name = "Example",
            BundleId = "com.example.app",
            Platform = ApplePlatform.macOS,
            ProjectPath = Path.Combine(root, "Example.xcodeproj"),
            ArchivePath = Path.Combine(root, "Example.xcarchive"),
            ExportPath = Path.Combine(root, "export")
        };
        app.DistributionRoute = direct ? AppleDistributionRoute.DirectNotarized : AppleDistributionRoute.AppStore;
        var actual = direct ? PowerForgeReleaseService.ComputeDirectExecutionSha256(plan, app)
            : PowerForgeReleaseService.ComputeAppleUploadExecutionSha256(plan, app);
        Assert.Equal(legacySha256, actual);

        app.RequiredEntitlementKeys = ["com.apple.security.application-groups"];
        var required = direct ? PowerForgeReleaseService.ComputeDirectExecutionSha256(plan, app)
            : PowerForgeReleaseService.ComputeAppleUploadExecutionSha256(plan, app);
        Assert.NotEqual(legacySha256, required);
    }
}
