namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Status, "1.1")]
    [InlineData(PowerForgeAppleReleaseAction.Doctor, "1.1")]
    [InlineData(PowerForgeAppleReleaseAction.Status, "1.2.0")]
    [InlineData(PowerForgeAppleReleaseAction.Doctor, "1.2.0")]
    public void Execute_AppleObservation_UsesRequestedVersionAndItsSelectedBuild(PowerForgeAppleReleaseAction action, string observedVersion)
    {
        var root = CreateSandbox();
        try
        {
            var (spec, source) = CreateAppleStatusDeliveryFixture(root);
            spec.AppleApps!.Automation.MarketingVersionPattern = "1.X";
            var service = CreateAppleAutomationService(request =>
            {
                Assert.Equal(observedVersion, request.VersionString);
                Assert.Null(request.BuildNumber);
                var state = CreateReleaseState(request, processingState: null);
                state.Platforms[0].Version!.AppStoreState = "REJECTED";
                state.Platforms[0].SelectedBuild = new() { Id = "historical-build", Version = "8", MarketingVersion = observedVersion, ProcessingState = "VALID" };
                return state;
            }, prepareAppleDistribution: _ => throw new InvalidOperationException("Observation must not prepare."),
               distributeTestFlight: _ => throw new InvalidOperationException("Observation must not distribute."));
            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleAction = action,
                AppleSourceCommit = source, AppleMarketingVersion = observedVersion
            });
            Assert.True(result.Success, result.ErrorMessage);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal(observedVersion, target.Version);
            Assert.Equal("8", target.Build);
            Assert.Equal("REJECTED", target.DistributionState);
            Assert.False(target.ArchiveCreated);
            Assert.False(target.UploadPerformed);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Execute_AppleStatus_DefaultPatternDoesNotOverrideCurrentVersion()
    {
        var root = CreateSandbox();
        try
        {
            var (spec, source) = CreateAppleStatusDeliveryFixture(root);
            spec.AppleApps!.Automation.MarketingVersionPattern = "1.X";
            var service = CreateAppleAutomationService(request =>
            {
                Assert.Equal("1.2.0", request.VersionString);
                Assert.Equal("9", request.BuildNumber);
                return CreateReleaseState(request, "VALID");
            });
            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleAction = PowerForgeAppleReleaseAction.Status,
                AppleSourceCommit = source
            });
            Assert.True(result.Success, result.ErrorMessage);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Execute_AppleStatusPlan_RejectsAPatternAsAnExactObservedVersion()
    {
        var root = CreateSandbox();
        try
        {
            var (spec, source) = CreateAppleStatusDeliveryFixture(root);
            var error = Assert.Throws<InvalidOperationException>(() =>
                CreateAppleAutomationService(_ => throw new InvalidOperationException("Plan must not read Apple."))
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleAction = PowerForgeAppleReleaseAction.Status,
                    AppleSourceCommit = source, AppleMarketingVersion = "1.X", PlanOnly = true
                }));
            Assert.Contains("numeric Apple marketing version", error.Message);
        }
        finally { TryDelete(root); }
    }
}
