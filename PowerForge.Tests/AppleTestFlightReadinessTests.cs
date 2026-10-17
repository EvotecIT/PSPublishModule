namespace PowerForge.Tests;

public sealed class AppleTestFlightReadinessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.TestFlightReadiness", Guid.NewGuid().ToString("N"));

    public AppleTestFlightReadinessTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(_root, "Sample.xcodeproj", "project.pbxproj"),
            "MARKETING_VERSION = 1.0.0;\nCURRENT_PROJECT_VERSION = 9;\n");
        File.WriteAllText(Path.Combine(_root, "AuthKey_TEST.p8"), "synthetic-private-key");
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.SubmitTestFlightReview, false)]
    [InlineData(PowerForgeAppleReleaseAction.SubmitTestFlightReview, true)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight, false)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight, true)]
    public void BetaPlan_BindsValidBuildWithoutPublicStoreSelection(PowerForgeAppleReleaseAction action, bool hasStoreVersion)
    {
        var result = Service(request => State(request, "VALID", hasStoreVersion)).Execute(Spec(), Request(action));

        Assert.True(result.Success, result.ErrorMessage);
        var target = Assert.Single(result.AppleReceipt!.Targets);
        Assert.Equal("beta-build", target.BuildId);
        Assert.Equal("VALID", target.BuildProcessingState);
        Assert.Equal("1.0.0", target.Version);
        Assert.Equal("9", target.Build);
        Assert.NotEqual(true, target.BuildSelected);
    }

    [Fact]
    public void BetaReview_SubmitsExactPlannedBuildWithoutPreparingPublicVersion()
    {
        AppStoreConnectBetaAppReviewSubmissionRequest? submitted = null;
        var service = Service(request => State(request, "VALID", false), request =>
        {
            submitted = request;
            return new AppStoreConnectBetaAppReviewSubmissionResult
            {
                Build = new AppStoreConnectBuildInfo { Id = "beta-build", Version = "9", MarketingVersion = "1.0.0", ProcessingState = "VALID" },
                Submission = new AppStoreConnectBetaAppReviewSubmissionInfo { Id = "beta-review", BetaReviewState = "WAITING_FOR_REVIEW" }
            };
        });
        var preview = service.Execute(Spec(), Request(PowerForgeAppleReleaseAction.SubmitTestFlightReview));
        var execution = Request(PowerForgeAppleReleaseAction.SubmitTestFlightReview);
        execution.PlanOnly = false;
        execution.AppleActionConfirmed = true;
        execution.AppleExpectedPlanSha256 = preview.AppleReceipt!.PlanSha256;

        var result = service.Execute(Spec(), execution);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(submitted);
        Assert.Equal("app-1", submitted!.AppId);
        Assert.Equal("1.0.0", submitted.VersionString);
        Assert.Equal("9", submitted.BuildNumber);
        Assert.True(submitted.RequireValidBuild);
        Assert.False(result.AppleAppPlan!.PrepareDistribution);
        Assert.False(result.AppleAppPlan.SubmitForReview);
        Assert.False(result.AppleAppPlan.ReleaseApprovedVersion);
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.SubmitTestFlightReview, null)]
    [InlineData(PowerForgeAppleReleaseAction.SubmitTestFlightReview, "PROCESSING")]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight, null)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight, "INVALID")]
    public void BetaPlan_RejectsMissingOrUnprocessedBuild(PowerForgeAppleReleaseAction action, string? processingState)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Service(request => State(request, processingState, false)).Execute(Spec(), Request(action)));

        Assert.Contains("exact matched TestFlight build", error.Message);
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.SubmitAppReview)]
    [InlineData(PowerForgeAppleReleaseAction.Release)]
    public void PublicReviewAndRelease_StillRequireStoreBuildSelection(PowerForgeAppleReleaseAction action)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Service(request => State(request, "VALID", true)).Execute(Spec(), Request(action)));

        Assert.Contains("uniquely selected", error.Message);
    }

    [Fact]
    public void TestFlightPlan_PreservesExplicitUnprocessedBuildPolicy()
    {
        var spec = Spec();
        spec.AppleApps!.AllowUnprocessedTestFlightBuild = true;

        var result = Service(request => State(request, "PROCESSING", false))
            .Execute(spec, Request(PowerForgeAppleReleaseAction.TestFlight));

        Assert.True(result.Success, result.ErrorMessage);
    }

    [Theory]
    [InlineData("com.example.media.mac.widgets")]
    [InlineData("com.example.other.widget")]
    [InlineData("com.example.media")]
    [InlineData("com.example.media.")]
    public void MacStoreArchivePlan_RejectsInvalidCompanionIdentityEvenForParentOnlySelection(string extensionId)
    {
        var spec = MacSpec(extensionId);
        var request = Request(PowerForgeAppleReleaseAction.Archive);
        request.Targets = ["Sample Mac"];

        var error = Assert.Throws<InvalidOperationException>(() => Service().Execute(spec, request));

        Assert.Contains("Mac App Store extension", error.Message);
        Assert.Contains("one nonempty suffix", error.Message);
    }

    [Fact]
    public void MacStoreArchivePlan_AcceptsOneSuffix()
    {
        var result = Service().Execute(MacSpec("com.example.media.macwidgets"), Request(PowerForgeAppleReleaseAction.Archive));

        Assert.True(result.Success, result.ErrorMessage);
    }

    [Fact]
    public void IosOnlyBetaPlan_DoesNotRequireRepairOfUnselectedMacCompanion()
    {
        var spec = MacSpec("com.example.media.mac.widgets");
        spec.AppleApps!.Apps = spec.AppleApps.Apps.Append(App("Sample iOS", ApplePlatform.iOS)).ToArray();
        var request = Request(PowerForgeAppleReleaseAction.SubmitTestFlightReview);
        request.Targets = ["Sample iOS"];

        var result = Service(value => State(value, "VALID", false)).Execute(spec, request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("Sample iOS", Assert.Single(result.AppleReceipt!.Targets).Name);
    }

    private PowerForgeReleaseService Service(
        Func<AppStoreConnectReleaseStateRequest, AppStoreConnectReleaseStateResult>? state = null,
        Func<AppStoreConnectBetaAppReviewSubmissionRequest, AppStoreConnectBetaAppReviewSubmissionResult>? submit = null)
        => new(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
            planTools: (_, _, _) => throw new InvalidOperationException("Tools must not run."),
            runTools: _ => throw new InvalidOperationException("Tools must not run."),
            loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("Tools must not run."),
            planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("Tools must not run."),
            runDotNetTools: _ => throw new InvalidOperationException("Tools must not run."),
            publishGitHubRelease: _ => throw new InvalidOperationException("GitHub must not run."),
            getAppleReleaseState: state ?? (_ => throw new InvalidOperationException("Archive preview must not query Apple.")),
            generateAppleProject: _ => false,
            prepareAppleDistribution: _ => throw new InvalidOperationException("Beta delivery must not prepare a public version."),
            submitTestFlightBetaReview: submit,
            appleAppStateRoot: Path.Combine(_root, "state"));

    private PowerForgeReleaseRequest Request(PowerForgeAppleReleaseAction action)
        => new() { ConfigPath = Path.Combine(_root, "release.json"), AppleAction = action, PlanOnly = true };

    private PowerForgeReleaseSpec Spec()
        => new()
        {
            AppleApps = new()
            {
                ProjectRoot = _root,
                AppStoreConnectApiKeyPath = Path.Combine(_root, "AuthKey_TEST.p8"),
                AppStoreConnectApiKeyId = "TEST",
                AppStoreConnectApiIssuerId = "synthetic-issuer",
                TestFlightBetaGroupIds = ["external-group"],
                Apps = [App("Sample iOS", ApplePlatform.iOS)],
                Automation = new() { MinimumFreeSpaceGB = 0, CleanupBeforeArchive = false }
            }
        };

    private PowerForgeReleaseSpec MacSpec(string extensionId)
    {
        var spec = Spec();
        spec.AppleApps!.Apps = [App("Sample Mac", ApplePlatform.macOS), new()
        {
            Name = "Sample Widget", BundleId = extensionId, Platform = ApplePlatform.macOS,
            ProjectPath = "Sample.xcodeproj", Scheme = "Widget", ParentTarget = "Sample Mac",
            DistributionRoute = AppleDistributionRoute.EmbeddedCompanion, ProductRole = AppleProductRole.Extension
        }];
        return spec;
    }

    private static AppleAppConfiguration App(string name, ApplePlatform platform)
        => new()
        {
            Name = name, BundleId = "com.example.media", Platform = platform,
            ProjectPath = "Sample.xcodeproj", Scheme = name, AppStoreConnectAppId = "app-1",
            DistributionRoute = AppleDistributionRoute.AppStore, TestFlightPolicy = AppleTestFlightPolicy.External
        };

    private static AppStoreConnectReleaseStateResult State(AppStoreConnectReleaseStateRequest request, string? processingState, bool hasStoreVersion)
        => new()
        {
            AppId = request.AppId, VersionString = request.VersionString, BuildNumber = request.BuildNumber,
            Platforms = [new()
            {
                Platform = request.Platforms.Single(),
                Version = hasStoreVersion ? new() { Id = "public-version", VersionString = request.VersionString } : null,
                MatchedBuildSelected = hasStoreVersion ? false : null,
                MatchedBuild = processingState is null ? null : new()
                {
                    Id = "beta-build", Version = request.BuildNumber, MarketingVersion = request.VersionString,
                    ProcessingState = processingState
                }
            }]
        };

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
