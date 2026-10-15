namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData("IN_BETA_REVIEW")]
    [InlineData("WAITING_FOR_BETA_REVIEW")]
    public void Execute_ExternalTestFlightVerifiesBothPlatformAssignmentsWhileSiblingIsInReview(string reviewState)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.TestFlightBetaGroupNames = new[] { "External Testers" };
            var ios = Assert.Single(spec.AppleApps.Apps);
            ios.TestFlightPolicy = AppleTestFlightPolicy.External;
            spec.AppleApps.Apps = new[]
            {
                ios,
                new AppleAppConfiguration
                {
                    Name = "CasaRay Mac", BundleId = ios.BundleId, AppStoreConnectAppId = ios.AppStoreConnectAppId,
                    ProjectPath = ios.ProjectPath, Scheme = "CasaRayMac", Platform = ApplePlatform.macOS,
                    TestFlightPolicy = AppleTestFlightPolicy.External
                }
            };
            var distributed = new List<ApplePlatform>();
            var result = CreateAppleAutomationService(
                    request =>
                    {
                        var state = CreateReleaseState(request, "VALID");
                        state.Platforms.Single().BetaDetail!.ExternalBuildState =
                            request.Platforms.Single() == ApplePlatform.iOS ? reviewState : "READY_FOR_BETA_TESTING";
                        return state;
                    },
                    distributeTestFlight: request =>
                    {
                        distributed.Add(request.Platform);
                        return new AppStoreConnectTestFlightDistributionResult();
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.TestFlight
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(new[] { ApplePlatform.iOS, ApplePlatform.macOS }, distributed);
            var pending = Assert.Single(result.AppleReceipt!.Targets, target => target.Platform == ApplePlatform.iOS);
            Assert.Equal(reviewState, pending.TestFlightExternalState);
            Assert.All(result.AppleReceipt.Targets, target =>
            {
                Assert.False(target.ArchiveCreated);
                Assert.False(target.UploadPerformed);
                Assert.Null(target.TestFlightReviewState);
            });
        }
        finally
        {
            TryDelete(root);
        }
    }
}
