namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData("1.6")]
    [InlineData("1.6.0-beta")]
    [InlineData("1.6.0\nCURRENT_PROJECT_VERSION: 999")]
    public void Execute_AppleVersion_RejectsNonThreePartMarketingVersion(string marketingVersion)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            WriteXcodeGenVersionSource(root, "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";

            var service = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Version must not query release status."),
                    getHighestAppleBuildNumber: (_, _, _) => 13);

            var exception = Assert.Throws<ArgumentException>(() => service.Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Version,
                    AppleMarketingVersion = marketingVersion,
                    PlanOnly = true
                }));

            Assert.Contains("must use x.y.z", exception.Message, StringComparison.Ordinal);
            var source = new AppleReleaseVersionSourceService().Read(Path.Combine(root, "project.yml"));
            Assert.Equal("1.5.0", source.MarketingVersion);
            Assert.Equal("13", source.BuildNumber);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleVersion_UsesOneBuildAboveLocalAndEveryRemotePlatform()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            WriteXcodeGenVersionSource(root, "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            spec.AppleApps.Apps = new[]
            {
                spec.AppleApps.Apps.Single(),
                new AppleAppConfiguration
                {
                    Name = "CasaRay Mac",
                    BundleId = "com.evotecit.casaray",
                    Platform = ApplePlatform.macOS,
                    ArchiveVariant = AppleArchiveVariant.MacCatalyst,
                    ProjectPath = "CasaRay.xcodeproj",
                    Scheme = "CasaRay",
                    AppStoreConnectAppId = "6778025328"
                }
            };
            var generationCalls = 0;
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Version must not query release status."),
                generateAppleProject: _ =>
                {
                    generationCalls++;
                    var version = new AppleReleaseVersionSourceService().Read(Path.Combine(root, "project.yml"));
                    Assert.Equal("1.6.0", version.MarketingVersion);
                    Assert.Equal("16", version.BuildNumber);
                    return true;
                },
                getHighestAppleBuildNumber: (_, _, platform) => platform == ApplePlatform.iOS ? 15 : 14);

            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Version,
                AppleMarketingVersion = "1.6.0",
                AppleActionConfirmed = true
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, generationCalls);
            var versioning = Assert.IsType<PowerForgeAppleVersionReceipt>(result.AppleReceipt!.Versioning);
            Assert.Equal("1.6.0", versioning.MarketingVersion);
            Assert.Equal("16", versioning.BuildNumber);
            Assert.Equal(15, versioning.HighestRemoteBuildNumber);
            Assert.All(result.AppleReceipt.Targets, target =>
            {
                Assert.Equal("1.6.0", target.Version);
                Assert.Equal("16", target.Build);
            });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleVersion_RetryKeepsUnpublishedSelectedBuild()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "16");
            WriteXcodeGenVersionSource(root, "1.6.0", "16");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Version must not query release status."),
                    generateAppleProject: _ => true,
                    getHighestAppleBuildNumber: (_, _, _) => 15)
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Version,
                    AppleMarketingVersion = "1.6.0",
                    AppleActionConfirmed = true
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("16", result.AppleReceipt!.Versioning!.BuildNumber);
            Assert.False(result.AppleReceipt.Versioning.Changed);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleVersionPlan_WritesSeparatePlanReceiptWithoutChangingSource()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            WriteXcodeGenVersionSource(root, "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Version plan must not query release status."),
                    getHighestAppleBuildNumber: (_, _, _) => 13)
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Version,
                    AppleMarketingVersion = "1.6.0",
                    PlanOnly = true
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(result.AppleReceipt!.PlanOnly);
            Assert.Equal("14", result.AppleReceipt.Versioning!.BuildNumber);
            Assert.True(File.Exists(Path.Combine(root, "build/powerforge/apple/release-plan.json")));
            Assert.False(File.Exists(Path.Combine(root, "build/powerforge/apple/release-receipt.json")));
            var source = new AppleReleaseVersionSourceService().Read(Path.Combine(root, "project.yml"));
            Assert.Equal("1.5.0", source.MarketingVersion);
            Assert.Equal("13", source.BuildNumber);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleAdvancePlan_EnablesSafeStepsAndStopsBeforeReview()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);

            var result = new PowerForgeReleaseService(new NullLogger()).Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Advance,
                PlanOnly = true
            });

            Assert.True(result.Success, result.ErrorMessage);
            var plan = Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan);
            Assert.True(plan.Archive);
            Assert.True(plan.Upload);
            Assert.True(plan.PrepareDistribution);
            Assert.True(plan.SelectBuildForDistribution);
            Assert.True(plan.CheckReleaseReadiness);
            Assert.False(plan.SubmitTestFlightBetaReview);
            Assert.False(plan.SubmitForReview);
            Assert.False(plan.ReleaseApprovedVersion);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleAdvance_DoesNotPrepareTestFlightOnlyTargetForPublicStore()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            CreateXcodeProject(root, "CasaRayWatch.xcodeproj", "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Apps = new[]
            {
                Assert.Single(spec.AppleApps.Apps),
                new AppleAppConfiguration
                {
                    Name = "CasaRay watchOS",
                    BundleId = "com.evotecit.casaray.watch",
                    Platform = ApplePlatform.watchOS,
                    ProjectPath = "CasaRayWatch.xcodeproj",
                    Scheme = "CasaRayWatch",
                    AppStoreConnectAppId = "watch-app-id",
                    DistributionRoute = AppleDistributionRoute.TestFlightOnly,
                    TestFlightPolicy = AppleTestFlightPolicy.Internal
                }
            };
            var preparedAppIds = new List<string>();

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    prepareAppleDistribution: request =>
                    {
                        preparedAppIds.Add(request.AppId);
                        return CreateSuccessfulPreparation(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Advance,
                    AppleActionConfirmed = true
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(new[] { "6778025328" }, preparedAppIds);
            Assert.True(result.AppleReceipt!.Success);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleScreenshots_BindsVersionAtRuntimeWhenMappingOmitsVersionAndId()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var folder = Directory.CreateDirectory(Path.Combine(root, "shots"));
            File.WriteAllBytes(
                Path.Combine(folder.FullName, "01.png"),
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII="));
            File.WriteAllText(
                Path.Combine(root, "screenshots.json"),
                """
                {
                  "appId": "6778025328",
                  "useReleaseVersion": true,
                  "platform": "iOS",
                  "locale": "en-US",
                  "quality": {
                    "enabled": true,
                    "minimumFileBytes": 0,
                    "minimumKilobytesPerMegapixel": 0
                  },
                  "screenshotSets": [
                    {
                      "screenshotDisplayType": "APP_IPHONE_65",
                      "path": "shots",
                      "filter": "*.png"
                    }
                  ]
                }
                """);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.ScreenshotConfigPath = "screenshots.json";
            AppStoreConnectReleasePreparationRequest? observed = null;

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    prepareAppleDistribution: request =>
                    {
                        observed = request;
                        return CreateSuccessfulPreparation(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Screenshots,
                    AppleActionConfirmed = true
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(observed?.ScreenshotSpec);
            Assert.Equal("1.6.0", observed!.ScreenshotSpec!.VersionString);
            Assert.Null(observed.ScreenshotSpec.VersionId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void AppleReleaseOperationLock_RejectsOverlappingOwnerAndRecoversAfterDispose()
    {
        var root = CreateSandbox();
        try
        {
            var path = Path.Combine(root, "release.lock");
            using (AppleReleaseOperationLock.Acquire(path, PowerForgeAppleReleaseAction.Advance))
            {
                var exception = Assert.Throws<InvalidOperationException>(() =>
                    AppleReleaseOperationLock.Acquire(path, PowerForgeAppleReleaseAction.Version));
                Assert.Contains("Another Apple release operation", exception.Message, StringComparison.Ordinal);
            }

            using var recovered = AppleReleaseOperationLock.Acquire(path, PowerForgeAppleReleaseAction.Version);
            Assert.True(File.Exists(path));
            recovered.Dispose();
            Assert.True(File.Exists(path));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleVersion_ProjectGenerationFailureRetainsSelectedIdentityInReceipt()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            WriteXcodeGenVersionSource(root, "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            spec.AppleApps.Apps = new[]
            {
                spec.AppleApps.Apps.Single(),
                new AppleAppConfiguration
                {
                    Name = "CasaRay Mac",
                    BundleId = "com.evotecit.casaray",
                    Platform = ApplePlatform.macOS,
                    ArchiveVariant = AppleArchiveVariant.MacCatalyst,
                    ProjectPath = "CasaRay.xcodeproj",
                    Scheme = "CasaRay",
                    AppStoreConnectAppId = "6778025328"
                }
            };

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Version must not query release status."),
                    generateAppleProject: _ => throw new InvalidOperationException("xcodegen failed"),
                    getHighestAppleBuildNumber: (_, _, _) => 13)
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Version,
                    AppleMarketingVersion = "1.6.0",
                    AppleActionConfirmed = true
                });

            Assert.False(result.Success);
            Assert.Equal("1.6.0", result.AppleReceipt!.Versioning!.MarketingVersion);
            Assert.Equal("14", result.AppleReceipt.Versioning.BuildNumber);
            Assert.All(result.AppleReceipt.Targets, target =>
            {
                Assert.Equal("1.6.0", target.Version);
                Assert.Equal("14", target.Build);
            });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleAdvance_PartialFailureRefreshesRemoteStateAndKeepsOriginalError()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.TestFlightBetaGroupNames = new[] { "Internal" };
            var stateCalls = 0;

            var result = CreateAppleAutomationService(
                    request =>
                    {
                        stateCalls++;
                        var state = CreateReleaseState(request, "VALID");
                        state.Platforms.Single().Version!.Id = stateCalls == 1 ? "version-before" : "version-after";
                        return state;
                    },
                    prepareAppleDistribution: CreateSuccessfulPreparation,
                    distributeTestFlight: _ => throw new InvalidOperationException("group assignment failed"))
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Advance,
                    AppleActionConfirmed = true
                });

            Assert.False(result.Success);
            Assert.Equal("group assignment failed", result.ErrorMessage);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal("group assignment failed", target.ErrorMessage);
            Assert.Equal("version-after", target.DistributionVersionId);
            Assert.True(stateCalls >= 2);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void WriteXcodeGenVersionSource(string root, string marketingVersion, string buildNumber)
    {
        File.WriteAllText(
            Path.Combine(root, "project.yml"),
            $"""
            name: CasaRay
            settings:
              base:
                CURRENT_PROJECT_VERSION: "{buildNumber}"
                MARKETING_VERSION: "{marketingVersion}"
            """);
    }
}
