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
        var result = Service(request => State(request, "VALID", hasStoreVersion, expired: false)).Execute(Spec(), Request(action));

        Assert.True(result.Success, result.ErrorMessage);
        var target = Assert.Single(result.AppleReceipt!.Targets);
        Assert.Equal("beta-build", target.BuildId);
        Assert.Equal("VALID", target.BuildProcessingState);
        Assert.False(target.BuildExpired);
        Assert.Equal("1.0.0", target.Version);
        Assert.Equal("9", target.Build);
        Assert.NotEqual(true, target.BuildSelected);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ConfiguredBetaOnlyPlan_BindsValidBuildWithoutPublicVersion(bool distribute, bool submitBetaReview)
    {
        var spec = Spec();
        spec.AppleApps!.Archive = false;
        spec.AppleApps.Upload = false;
        spec.AppleApps.DistributeTestFlight = distribute;
        spec.AppleApps.SubmitTestFlightBetaReview = submitBetaReview;

        var result = Service(request => State(request, "VALID", false))
            .Execute(spec, Request(PowerForgeAppleReleaseAction.Configured));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("beta-build", Assert.Single(result.AppleReceipt!.Targets).BuildId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfiguredBetaOnlyPlan_PreservesProcessingOverride(bool submitBetaReview)
    {
        var spec = Spec();
        spec.AppleApps!.Archive = false;
        spec.AppleApps.Upload = false;
        spec.AppleApps.DistributeTestFlight = !submitBetaReview;
        spec.AppleApps.SubmitTestFlightBetaReview = submitBetaReview;
        spec.AppleApps.AllowUnprocessedTestFlightBuild = true;

        Assert.True(Service(request => State(request, "PROCESSING", false))
            .Execute(spec, Request(PowerForgeAppleReleaseAction.Configured)).Success);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, null)]
    [InlineData(false, true, null)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, null)]
    [InlineData(true, true, null)]
    public void ConfiguredBetaOnlyPlan_RequiresKnownUnexpiredBuild(bool submitBetaReview, bool allowUnprocessed, bool? expired)
    {
        var spec = Spec();
        spec.AppleApps!.Archive = false;
        spec.AppleApps.Upload = false;
        spec.AppleApps.DistributeTestFlight = !submitBetaReview;
        spec.AppleApps.SubmitTestFlightBetaReview = submitBetaReview;
        spec.AppleApps.AllowUnprocessedTestFlightBuild = allowUnprocessed;

        var error = Assert.Throws<InvalidOperationException>(() =>
            Service(request => State(request, "VALID", false, expired))
                .Execute(spec, Request(PowerForgeAppleReleaseAction.Configured)));

        Assert.Contains("not expired", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ConfiguredBetaPlan_PreservesPublicReviewAndReleaseSelection(int operation)
    {
        var spec = Spec();
        spec.AppleApps!.Archive = false;
        spec.AppleApps.Upload = false;
        spec.AppleApps.DistributeTestFlight = true;
        spec.AppleApps.SubmitForReview = operation == 0;
        spec.AppleApps.ReleaseApprovedVersion = operation == 1;
        var request = Request(PowerForgeAppleReleaseAction.Configured);

        var error = Assert.Throws<InvalidOperationException>(() =>
            Service(value => State(value, "VALID", true)).Execute(spec, request));

        Assert.Contains("uniquely selected", error.Message);
    }

    [Theory]
    [InlineData(false, AppleTestFlightPolicy.Disabled)]
    [InlineData(true, AppleTestFlightPolicy.Disabled)]
    [InlineData(true, AppleTestFlightPolicy.Internal)]
    [InlineData(true, AppleTestFlightPolicy.Automatic)]
    public void ConfiguredBetaPlan_DoesNotRequireReadyBuildForTargetsSkippedByPolicy(
        bool submitBetaReview, AppleTestFlightPolicy skippedPolicy)
    {
        var spec = Spec();
        spec.AppleApps!.Archive = false;
        spec.AppleApps.Upload = false;
        spec.AppleApps.DistributeTestFlight = !submitBetaReview;
        spec.AppleApps.SubmitTestFlightBetaReview = submitBetaReview;
        var skipped = App("Skipped iOS", ApplePlatform.iOS);
        skipped.BundleId = "com.example.skipped";
        skipped.AppStoreConnectAppId = "skipped-app";
        skipped.TestFlightPolicy = skippedPolicy;
        spec.AppleApps.Apps = spec.AppleApps.Apps.Append(skipped).ToArray();

        var result = Service(request =>
        {
            var isSkipped = request.AppId == "skipped-app";
            var state = State(request, "VALID", isSkipped, expired: isSkipped);
            if (isSkipped)
                state.Platforms[0].MatchedBuildSelected = true;
            return state;
        }).Execute(spec, Request(PowerForgeAppleReleaseAction.Configured));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.AppleReceipt!.Targets.Length);
    }

    [Theory]
    [InlineData(AppleTestFlightPolicy.Internal)]
    [InlineData(AppleTestFlightPolicy.Automatic)]
    public void ConfiguredCombinedBetaPlan_StillChecksTargetsReceivingDistribution(AppleTestFlightPolicy policy)
    {
        var spec = Spec();
        spec.AppleApps!.Archive = false;
        spec.AppleApps.Upload = false;
        spec.AppleApps.DistributeTestFlight = true;
        spec.AppleApps.SubmitTestFlightBetaReview = true;
        var internalApp = App("Internal iOS", ApplePlatform.iOS);
        internalApp.BundleId = "com.example.internal";
        internalApp.AppStoreConnectAppId = "internal-app";
        internalApp.TestFlightPolicy = policy;
        spec.AppleApps.Apps = spec.AppleApps.Apps.Append(internalApp).ToArray();

        var error = Assert.Throws<InvalidOperationException>(() =>
            Service(request => State(request, "VALID", false, expired: request.AppId == "internal-app"))
                .Execute(spec, Request(PowerForgeAppleReleaseAction.Configured)));

        Assert.Contains("not expired", error.Message);
        Assert.Contains("Internal iOS", error.Message);
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
    [InlineData(PowerForgeAppleReleaseAction.SubmitTestFlightReview, false, true)]
    [InlineData(PowerForgeAppleReleaseAction.SubmitTestFlightReview, true, true)]
    [InlineData(PowerForgeAppleReleaseAction.SubmitTestFlightReview, false, null)]
    [InlineData(PowerForgeAppleReleaseAction.SubmitTestFlightReview, true, null)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight, false, true)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight, true, true)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight, false, null)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight, true, null)]
    public void BetaPlan_RequiresKnownUnexpiredBuildIndependentlyOfProcessingPolicy(
        PowerForgeAppleReleaseAction action, bool allowUnprocessed, bool? expired)
    {
        var spec = Spec();
        spec.AppleApps!.AllowUnprocessedTestFlightBuild = allowUnprocessed;
        var error = Assert.Throws<InvalidOperationException>(() =>
            Service(request => State(request, "VALID", false, expired)).Execute(spec, Request(action)));

        Assert.Contains("not expired", error.Message);
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.SubmitTestFlightReview)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight)]
    public void BetaApproval_BindsExpirationAndRejectsExpiryBeforeMutation(PowerForgeAppleReleaseAction action)
    {
        var expired = false;
        var service = Service(request => State(request, "VALID", false, expired));
        var preview = service.Execute(Spec(), Request(action));
        var target = Assert.Single(preview.AppleReceipt!.Targets);
        target.BuildExpired = true;
        Assert.NotEqual(preview.AppleReceipt.PlanSha256,
            PowerForgeReleaseService.ComputeApplePlanSha256(preview.AppleReceipt));
        target.BuildExpired = false;
        expired = true;
        var request = Request(action);
        request.PlanOnly = false;
        request.AppleActionConfirmed = true;
        request.AppleExpectedPlanSha256 = preview.AppleReceipt.PlanSha256;

        var result = service.Execute(Spec(), request);

        Assert.False(result.Success);
        Assert.Contains("not expired", result.ErrorMessage);
    }

    [Theory]
    [InlineData("com.example.media.helper.widget", true)]
    [InlineData("com.example.media.helper.mac.widget", false)]
    [InlineData("com.example.media.widget", false)]
    public void MacStoreArchivePlan_ValidatesNestedExtensionAgainstImmediateParent(string extensionId, bool valid)
    {
        var spec = MacSpec(extensionId);
        var extension = spec.AppleApps!.Apps[1];
        extension.ParentTarget = "Sample Helper";
        spec.AppleApps.Apps = spec.AppleApps.Apps.Append(new AppleAppConfiguration
        {
            Name = "Sample Helper", BundleId = "com.example.media.helper", Platform = ApplePlatform.macOS,
            ProjectPath = "Sample.xcodeproj", Scheme = "Helper", ParentTarget = "Sample Mac",
            DistributionRoute = AppleDistributionRoute.EmbeddedCompanion, ProductRole = AppleProductRole.CompanionApp
        }).ToArray();
        var request = Request(PowerForgeAppleReleaseAction.Archive);
        request.Targets = ["Sample Mac"];

        if (valid)
            Assert.True(Service().Execute(spec, request).Success);
        else
            Assert.Contains("Mac App Store extension", Assert.Throws<InvalidOperationException>(() =>
                Service().Execute(spec, request)).Message);
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(false, true, " ")]
    [InlineData(true, false, null)]
    [InlineData(true, true, " ")]
    public void MacStoreArchivePlan_RequiresIdentifiersForUnselectedExtensionAndItsParent(
        bool nested, bool missingParent, string? missingId)
    {
        var spec = MacSpec(nested ? "com.example.media.helper.widget" : "com.example.media.widget");
        var parent = spec.AppleApps!.Apps[0];
        var extension = spec.AppleApps.Apps[1];
        if (nested)
        {
            parent = new AppleAppConfiguration
            {
                Name = "Helper", BundleId = "com.example.media.helper", Platform = ApplePlatform.macOS,
                ProjectPath = "Sample.xcodeproj", Scheme = "Helper", ParentTarget = "Sample Mac",
                DistributionRoute = AppleDistributionRoute.EmbeddedCompanion, ProductRole = AppleProductRole.CompanionApp
            };
            extension.ParentTarget = parent.Name;
            spec.AppleApps.Apps = spec.AppleApps.Apps.Append(parent).ToArray();
        }
        (missingParent ? parent : extension).BundleId = missingId;
        var request = Request(PowerForgeAppleReleaseAction.Archive);
        request.Targets = ["Sample Mac"];

        var error = Assert.Throws<InvalidOperationException>(() => Service().Execute(spec, request));

        Assert.Contains("requires BundleId", error.Message);
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Status)]
    [InlineData(PowerForgeAppleReleaseAction.Doctor)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight)]
    [InlineData(PowerForgeAppleReleaseAction.SubmitTestFlightReview)]
    [InlineData(PowerForgeAppleReleaseAction.Release)]
    [InlineData(PowerForgeAppleReleaseAction.Cleanup)]
    [InlineData(PowerForgeAppleReleaseAction.Configured)]
    public void ExistingMacBuildPlan_DoesNotRequireArchiveExtensionRepair(PowerForgeAppleReleaseAction action)
    {
        var spec = MacSpec("com.example.media.mac.widgets");
        spec.AppleApps!.Archive = false;
        spec.AppleApps.Upload = false;
        spec.AppleApps.DistributeTestFlight = true;
        var request = Request(action);
        request.Targets = ["Sample Mac"];
        var service = Service(value =>
        {
            var state = State(value, "VALID", true);
            state.Platforms[0].MatchedBuildSelected = true;
            state.Platforms[0].Version!.AppStoreState = "PENDING_DEVELOPER_RELEASE";
            return state;
        });

        var result = service.Execute(spec, request);

        Assert.True(result.Success, result.ErrorMessage);
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Upload)]
    [InlineData(PowerForgeAppleReleaseAction.UploadExisting)]
    [InlineData(PowerForgeAppleReleaseAction.Rehearse)]
    [InlineData(PowerForgeAppleReleaseAction.Advance)]
    [InlineData(PowerForgeAppleReleaseAction.Ship)]
    [InlineData(PowerForgeAppleReleaseAction.Configured)]
    public void MacArtifactPlan_RejectsInvalidExtensionForArchiveAndUploadActions(PowerForgeAppleReleaseAction action)
    {
        var spec = MacSpec("com.example.media.mac.widgets");
        spec.AppleApps!.Archive = false;
        spec.AppleApps.Upload = true;
        var request = Request(action);
        if (action == PowerForgeAppleReleaseAction.Ship)
        {
            spec.AppleApps.Automation.VersionSourcePath = "project.yml";
            File.WriteAllText(Path.Combine(_root, "project.yml"),
                "MARKETING_VERSION: 1.0.0\nCURRENT_PROJECT_VERSION: 9\n");
            request.AppleMarketingVersion = "1.0.0";
            request.AppleSourceCommit = "0123456789abcdef0123456789abcdef01234567";
            request.AppleShipTestFlightTargets = ["Sample Mac"];
        }

        var error = Assert.Throws<InvalidOperationException>(() => Service().Execute(spec, request));

        Assert.Contains("Mac App Store extension", error.Message);
    }

    [Fact]
    public void MacStoreArchivePlan_FollowsMultipleEmbeddedParents()
    {
        var spec = MacSpec("com.example.media.helper.child.widget");
        spec.AppleApps!.Apps[1].ParentTarget = "Child Helper";
        spec.AppleApps.Apps = spec.AppleApps.Apps.Concat(new[]
        {
            new AppleAppConfiguration
            {
                Name = "Helper", BundleId = "com.example.media.helper", Platform = ApplePlatform.macOS,
                ProjectPath = "Sample.xcodeproj", Scheme = "Helper", ParentTarget = "Sample Mac",
                DistributionRoute = AppleDistributionRoute.EmbeddedCompanion, ProductRole = AppleProductRole.CompanionApp
            },
            new AppleAppConfiguration
            {
                Name = "Child Helper", BundleId = "com.example.media.helper.child", Platform = ApplePlatform.macOS,
                ProjectPath = "Sample.xcodeproj", Scheme = "Child Helper", ParentTarget = "Helper",
                DistributionRoute = AppleDistributionRoute.EmbeddedCompanion, ProductRole = AppleProductRole.CompanionApp
            }
        }).ToArray();
        var request = Request(PowerForgeAppleReleaseAction.Archive);
        request.Targets = ["Sample Mac"];

        Assert.True(Service().Execute(spec, request).Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MacStoreArchivePlan_IgnoresDuplicateNamesOutsideSelectedEmbeddingChain(bool useSchemeFallback)
    {
        var spec = MacSpec("com.example.media.widget");
        spec.AppleApps!.Apps = spec.AppleApps.Apps.Concat(new[]
        {
            new AppleAppConfiguration
            {
                Name = useSchemeFallback ? null : "Unrelated", Scheme = "Unrelated",
                BundleId = "com.example.unrelated.one", Platform = ApplePlatform.macOS,
                ProjectPath = "Sample.xcodeproj"
            },
            new AppleAppConfiguration
            {
                Name = " unrelated ", Scheme = "Other", BundleId = "com.example.unrelated.two",
                Platform = ApplePlatform.macOS, ProjectPath = "Sample.xcodeproj"
            }
        }).ToArray();
        var request = Request(PowerForgeAppleReleaseAction.Archive);
        request.Targets = ["Sample Mac"];

        Assert.True(Service().Execute(spec, request).Success);
    }

    [Fact]
    public void MacStoreArchivePlan_RejectsAmbiguousParentUsedBySelectedEmbeddingChain()
    {
        var spec = MacSpec("com.example.media.helper.widget");
        spec.AppleApps!.Apps[1].ParentTarget = "Helper";
        spec.AppleApps.Apps = spec.AppleApps.Apps.Concat(new[]
        {
            new AppleAppConfiguration
            {
                Name = "Helper", Scheme = "Helper", BundleId = "com.example.media.helper",
                Platform = ApplePlatform.macOS, ProjectPath = "Sample.xcodeproj",
                ParentTarget = "Sample Mac", DistributionRoute = AppleDistributionRoute.EmbeddedCompanion,
                ProductRole = AppleProductRole.CompanionApp
            },
            new AppleAppConfiguration
            {
                Name = " helper ", Scheme = "Other", BundleId = "com.example.other",
                Platform = ApplePlatform.macOS, ProjectPath = "Sample.xcodeproj"
            }
        }).ToArray();
        var request = Request(PowerForgeAppleReleaseAction.Archive);
        request.Targets = ["Sample Mac"];

        var error = Assert.Throws<InvalidOperationException>(() => Service().Execute(spec, request));

        Assert.Contains("ambiguous", error.Message);
        Assert.Contains("Helper", error.Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MacStoreArchivePlan_PreservesSchemeFallbackInAncestry(bool omitRootName, bool omitHelperName)
    {
        var spec = MacSpec("com.example.media.helper.mac.widget");
        if (omitRootName)
            spec.AppleApps!.Apps[0].Name = null;
        spec.AppleApps!.Apps[1].ParentTarget = "Sample Helper";
        spec.AppleApps.Apps = spec.AppleApps.Apps.Append(new AppleAppConfiguration
        {
            Name = omitHelperName ? null : "Sample Helper", Scheme = "Sample Helper",
            BundleId = "com.example.media.helper", Platform = ApplePlatform.macOS,
            ProjectPath = "Sample.xcodeproj", ParentTarget = "Sample Mac",
            DistributionRoute = AppleDistributionRoute.EmbeddedCompanion, ProductRole = AppleProductRole.CompanionApp
        }).ToArray();
        var request = Request(PowerForgeAppleReleaseAction.Archive);
        request.Targets = ["Sample Mac"];

        var error = Assert.Throws<InvalidOperationException>(() => Service().Execute(spec, request));

        Assert.Contains("Mac App Store extension", error.Message);
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

    [Theory]
    [InlineData("com.example.media.macwidgets")]
    [InlineData("COM.EXAMPLE.MEDIA.macwidgets")]
    public void MacStoreArchivePlan_AcceptsOneSuffix(string extensionId)
    {
        var result = Service().Execute(MacSpec(extensionId), Request(PowerForgeAppleReleaseAction.Archive));

        Assert.True(result.Success, result.ErrorMessage);
    }

    [Fact]
    public void DirectMacArchivePlan_DoesNotApplyAppStoreSuffixRestriction()
    {
        var spec = MacSpec("com.example.media.mac.widgets");
        spec.AppleApps!.Apps[0].DistributionRoute = AppleDistributionRoute.DirectNotarized;
        spec.AppleApps.Apps[0].TestFlightPolicy = AppleTestFlightPolicy.Disabled;

        Assert.True(Service().Execute(spec, Request(PowerForgeAppleReleaseAction.Archive)).Success);
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
            distributeTestFlight: _ => throw new InvalidOperationException("Beta distribution must not run during plan validation."),
            submitTestFlightBetaReview: submit ?? (_ => throw new InvalidOperationException("Beta review must not run during plan validation.")),
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

    private static AppStoreConnectReleaseStateResult State(AppStoreConnectReleaseStateRequest request, string? processingState, bool hasStoreVersion,
        bool? expired = false)
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
                    ProcessingState = processingState, Expired = expired
                }
            }]
        };

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
