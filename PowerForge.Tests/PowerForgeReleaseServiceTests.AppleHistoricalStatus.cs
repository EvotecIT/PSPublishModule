namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Status, false)]
    [InlineData(PowerForgeAppleReleaseAction.Doctor, false)]
    [InlineData(PowerForgeAppleReleaseAction.Status, true)]
    [InlineData(PowerForgeAppleReleaseAction.Doctor, true)]
    public void Execute_AppleHistoricalPlan_BindsRequestedVersionWithoutObservingBuild(PowerForgeAppleReleaseAction action, bool workspace)
    {
        var root = CreateSandbox();
        try
        {
            var (spec, source) = CreateAppleStatusDeliveryFixture(root);
            if (workspace)
            {
                var app = Assert.Single(spec.AppleApps!.Apps);
                app.ProjectPath = "CasaRay.xcworkspace";
                app.MarketingVersion = "2.3";
                app.BuildNumber = "30";
                Directory.CreateDirectory(Path.Combine(root, app.ProjectPath));
                source = EnsureTestSourceCommit(root);
            }
            var service = CreateAppleAutomationService(_ => throw new InvalidOperationException("Observation plan must not read Apple."));
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleAction = action,
                AppleSourceCommit = source, AppleMarketingVersion = "1.1", PlanOnly = true
            };
            var first = service.Execute(spec, request);
            Assert.True(first.Success, first.ErrorMessage);
            var target = Assert.Single(first.AppleReceipt!.Targets);
            Assert.Equal("1.1", target.Version);
            Assert.Null(target.Build);
            Assert.Null(target.BuildId);
            Assert.Null(target.BuildProcessingState);
            request.AppleMarketingVersion = "1.2";
            var second = service.Execute(spec, request);
            Assert.True(second.Success, second.ErrorMessage);
            Assert.NotEqual(first.AppleReceipt.PlanSha256, second.AppleReceipt!.PlanSha256);
        }
        finally { TryDelete(root); }
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Status)]
    [InlineData(PowerForgeAppleReleaseAction.Doctor)]
    public void Execute_AppleExplicitObservation_DoesNotStampSkippedTargets(PowerForgeAppleReleaseAction action)
    {
        var root = CreateSandbox();
        try
        {
            var (spec, source) = CreateAppleStatusDeliveryFixture(root);
            var app = Assert.Single(spec.AppleApps!.Apps);
            spec.AppleApps.TeamId = "TESTTEAM";
            spec.AppleApps.DirectDistribution.KeychainProfile = "synthetic-notary-profile";
            spec.AppleApps.Apps = new[]
            {
                app,
                new AppleAppConfiguration
                {
                    Name = "Direct Mac", BundleId = "com.example.direct", Platform = ApplePlatform.macOS,
                    ProjectPath = app.ProjectPath, Scheme = "DirectMac", DistributionRoute = AppleDistributionRoute.DirectNotarized
                },
                new AppleAppConfiguration
                {
                    Name = "Companion", BundleId = "com.example.companion", Platform = ApplePlatform.watchOS,
                    ProjectPath = app.ProjectPath, Scheme = "Companion", DistributionRoute = AppleDistributionRoute.EmbeddedCompanion,
                    ProductRole = AppleProductRole.CompanionApp, ParentTarget = app.Name
                },
                new AppleAppConfiguration
                {
                    Name = "Development", BundleId = "com.example.development", Platform = ApplePlatform.iOS,
                    ProjectPath = app.ProjectPath, Scheme = "Development", DistributionRoute = AppleDistributionRoute.DevelopmentOnly
                }
            };
            var calls = 0;
            var result = CreateAppleAutomationService(request =>
            {
                calls++;
                Assert.Equal(app.AppStoreConnectAppId, request.AppId);
                return CreateReleaseState(request, null);
            }).Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleAction = action,
                AppleSourceCommit = source, AppleMarketingVersion = "1.1"
            });
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, calls);
            Assert.Equal("1.1", Assert.Single(result.AppleReceipt!.Targets, target => target.Name == app.Name).Version);
            Assert.All(result.AppleReceipt.Targets.Where(target => target.Name != app.Name), target =>
            {
                Assert.Null(target.Version);
                Assert.Null(target.Build);
                Assert.Null(target.BuildId);
            });
        }
        finally { TryDelete(root); }
    }

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
            Assert.True(target.BuildSelected);
            Assert.Equal("REJECTED", target.DistributionState);
            Assert.False(target.ArchiveCreated);
            Assert.False(target.UploadPerformed);
        }
        finally { TryDelete(root); }
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Status, true)]
    [InlineData(PowerForgeAppleReleaseAction.Doctor, true)]
    [InlineData(PowerForgeAppleReleaseAction.Status, false)]
    [InlineData(PowerForgeAppleReleaseAction.Doctor, false)]
    public void Execute_AppleExplicitObservation_DoesNotRequireLocalVersionEvenWithPublisherDefaults(PowerForgeAppleReleaseAction action, bool workspace)
    {
        var root = CreateSandbox();
        try
        {
            var (spec, _) = CreateAppleStatusDeliveryFixture(root);
            var app = Assert.Single(spec.AppleApps!.Apps);
            app.ProjectPath = workspace ? "Versionless.xcworkspace" : "CasaRay.xcodeproj";
            app.UseResolvedVersion = workspace;
            app.BuildNumberPolicy = AppleBuildNumberPolicy.IncrementExisting;
            app.MarketingVersion = null;
            app.BuildNumber = null;
            Directory.CreateDirectory(Path.Combine(root, app.ProjectPath));
            if (!workspace)
                File.WriteAllText(Path.Combine(root, app.ProjectPath, "project.pbxproj"), "// no local version fields");
            var source = EnsureTestSourceCommit(root);
            var calls = 0;
            var service = CreateAppleAutomationService(request =>
            {
                calls++;
                Assert.Equal("1.1", request.VersionString);
                Assert.Null(request.BuildNumber);
                return CreateReleaseState(request, null);
            }, generateAppleProject: _ => throw new InvalidOperationException("Observation must not generate a project."));
            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleAction = action,
                AppleSourceCommit = source, AppleMarketingVersion = "1.1"
            });
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, calls);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal("1.1", target.Version);
            Assert.Null(target.Build);
            Assert.Null(target.BuildSelected);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Execute_AppleHistoricalStatus_DoesNotObserveUploadWhenSelectedBuildIsVisible()
    {
        var root = CreateSandbox();
        try
        {
            var (spec, source) = CreateAppleStatusDeliveryFixture(root);
            var service = CreateAppleAutomationService(request =>
            {
                var state = CreateReleaseState(request, null);
                state.Platforms[0].SelectedBuild = new()
                {
                    Id = "selected-build", Version = "9", MarketingVersion = "1.2.0", ProcessingState = "VALID"
                };
                return state;
            }, getAppleBuildUpload: (_, _) => throw new InvalidOperationException("A visible selected build must not use delivery fallback."));
            var plan = CreateAppleStatusDeliveryPlan(service, spec, root, source);
            WriteAppleStatusDelivery(plan, "older-upload");
            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleAction = PowerForgeAppleReleaseAction.Status,
                AppleSourceCommit = source, AppleMarketingVersion = "1.2.0"
            });
            Assert.True(result.Success, result.ErrorMessage);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal("selected-build", target.BuildId);
            Assert.True(target.BuildSelected);
            Assert.Null(target.BuildUploadId);
            Assert.DoesNotContain(target.Diagnostics, value => value.Code.StartsWith("APPLE_BUILD_UPLOAD_", StringComparison.Ordinal));
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
