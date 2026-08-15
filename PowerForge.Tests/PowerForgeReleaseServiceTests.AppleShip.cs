namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    private const string AppleShipPlanSourceCommit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void Execute_AppleShipPlan_RequiresExactSourceCommit()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            WriteXcodeGenVersionSource(root, "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            EnableInternalAppleShipTargets(spec);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                CreateAppleAutomationService(
                        request => CreateReleaseState(request, "VALID"),
                        getHighestAppleBuildNumber: (_, _, _) => 13)
                    .Execute(spec, new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        PlanOnly = true,
                        AppleAction = PowerForgeAppleReleaseAction.Ship,
                        AppleMarketingVersion = "1.6.0",
                        AppleShipTestFlightTargets = ["CasaRay iOS"]
                    }));

            Assert.Contains("requires --apple-source-commit", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(AppleTestFlightPolicy.Automatic)]
    [InlineData(AppleTestFlightPolicy.External)]
    public void Execute_AppleShipPlan_RejectsNonInternalTestFlightPolicy(AppleTestFlightPolicy policy)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            WriteXcodeGenVersionSource(root, "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            spec.AppleApps.Apps.Single().TestFlightPolicy = policy;

            var exception = Assert.Throws<InvalidOperationException>(() =>
                CreateAppleAutomationService(
                        request => CreateReleaseState(request, "VALID"),
                        getHighestAppleBuildNumber: (_, _, _) => 13)
                    .Execute(spec, new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        PlanOnly = true,
                        AppleAction = PowerForgeAppleReleaseAction.Ship,
                        AppleMarketingVersion = "1.6.0",
                        AppleSourceCommit = AppleShipPlanSourceCommit,
                        AppleShipTestFlightTargets = ["CasaRay iOS"]
                    }));

            Assert.Contains("TestFlightPolicy=Internal", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Execute_AppleShipReleasePlan_SupportsInternalTestFlightRouteMatrix(bool includeIos, bool includeMac)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            WriteXcodeGenVersionSource(root, "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            spec.AppleApps.Apps =
            [
                spec.AppleApps.Apps.Single(),
                new AppleAppConfiguration
                {
                    Name = "CasaRay Mac",
                    BundleId = "com.evotecit.casaray",
                    Platform = ApplePlatform.macOS,
                    ArchiveVariant = AppleArchiveVariant.MacCatalyst,
                    ProjectPath = "CasaRay.xcodeproj",
                    Scheme = "CasaRayMac",
                    AppStoreConnectAppId = "6778025328"
                }
            ];
            EnableInternalAppleShipTargets(spec);
            var targets = new List<string>();
            if (includeIos) targets.Add("CasaRay iOS");
            if (includeMac) targets.Add("CasaRay Mac");

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    getHighestAppleBuildNumber: (_, _, _) => 13)
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.Ship,
                    AppleMarketingVersion = "1.6.0",
                    AppleSourceCommit = AppleShipPlanSourceCommit,
                    AppleShipTestFlightTargets = targets.ToArray()
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(PowerForgeAppleShipPhase.Release, result.AppleReceipt!.ShipPhase);
            Assert.False(result.AppleAppPlan!.PrepareDistribution);
            Assert.False(result.AppleAppPlan.SubmitForReview);
            Assert.False(result.AppleAppPlan.SyncScreenshots);
            Assert.Equal(includeIos, Assert.Single(result.AppleReceipt.Targets, target => target.Platform == ApplePlatform.iOS).ShipToTestFlight);
            Assert.Equal(includeMac, Assert.Single(result.AppleReceipt.Targets, target => target.Platform == ApplePlatform.macOS).ShipToTestFlight);
            Assert.All(result.AppleReceipt.Targets, target => Assert.False(target.ShipToAppStoreReview));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleShipPlan_StopsAtVersionCheckpointAndBindsIntent()
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
            EnableInternalAppleShipTargets(spec);
            var stateQueries = 0;
            var service = CreateAppleAutomationService(
                request =>
                {
                    stateQueries++;
                    return CreateReleaseState(request, "VALID");
                },
                getHighestAppleBuildNumber: (_, _, _) => 13);

            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = AppleShipPlanSourceCommit,
                AppleShipTestFlightTargets = ["CasaRay iOS"]
            });
            var appStorePlan = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = AppleShipPlanSourceCommit,
                AppleShipAppStoreTargets = ["CasaRay iOS"]
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(PowerForgeAppleShipPhase.VersionCheckpoint, result.AppleReceipt!.ShipPhase);
            Assert.Equal(0, stateQueries);
            Assert.Matches("^[0-9A-F]{64}$", result.AppleReceipt.PlanSha256!);
            Assert.NotEqual(result.AppleReceipt.PlanSha256, appStorePlan.AppleReceipt!.PlanSha256);
            var target = Assert.Single(result.AppleReceipt.Targets);
            Assert.True(target.ShipToTestFlight);
            Assert.False(target.ShipToAppStoreReview);
            Assert.Null(target.BuildId);
            Assert.Contains(result.AppleReceipt.NextActions, action =>
                action.Contains("review and merge", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleShipVersionCheckpoint_UpdatesSourceOnlyAfterExactConfirmation()
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
            EnableInternalAppleShipTargets(spec);
            var sourceCommit = CommitAppleShipSource(root);
            var archiveCalls = 0;
            var uploadCalls = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                archiveAppleApp: request =>
                {
                    archiveCalls++;
                    return CreateSuccessfulArchive(request);
                },
                uploadAppleApp: request =>
                {
                    uploadCalls++;
                    return CreateSuccessfulUpload(request);
                },
                generateAppleProject: _ => true,
                getHighestAppleBuildNumber: (_, _, _) => 13);
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = sourceCommit,
                AppleShipTestFlightTargets = ["CasaRay iOS"]
            };
            var planned = service.Execute(spec, request);

            var execution = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = request.ConfigPath,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = sourceCommit,
                AppleShipTestFlightTargets = ["CasaRay iOS"],
                AppleExpectedPlanSha256 = planned.AppleReceipt!.PlanSha256,
                AppleActionConfirmed = true
            });

            Assert.True(execution.Success, execution.ErrorMessage);
            Assert.Equal(0, archiveCalls);
            Assert.Equal(0, uploadCalls);
            var source = new AppleReleaseVersionSourceService().Read(Path.Combine(root, "project.yml"));
            Assert.Equal("1.6.0", source.MarketingVersion);
            Assert.Equal("14", source.BuildNumber);
            Assert.Equal(PowerForgeAppleShipPhase.VersionCheckpoint, execution.AppleReceipt!.ShipPhase);
            Assert.All(execution.AppleReceipt.Targets, target =>
            {
                Assert.False(target.ArchiveCreated);
                Assert.False(target.UploadPerformed);
            });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleShipVersionCheckpoint_RejectsCheckoutThatMovedAfterApproval()
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
            EnableInternalAppleShipTargets(spec);
            var approvedSourceCommit = CommitAppleShipSource(root);
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                generateAppleProject: _ => true,
                getHighestAppleBuildNumber: (_, _, _) => 13);
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = approvedSourceCommit,
                AppleShipTestFlightTargets = ["CasaRay iOS"]
            };
            var planned = service.Execute(spec, request);

            RunSnapshotGit(root, "commit", "--quiet", "--allow-empty", "-m", "move checkout after approval");

            var execution = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = request.ConfigPath,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = approvedSourceCommit,
                AppleShipTestFlightTargets = ["CasaRay iOS"],
                AppleExpectedPlanSha256 = planned.AppleReceipt!.PlanSha256,
                AppleActionConfirmed = true
            });

            Assert.False(execution.Success);
            Assert.Contains("instead of the approved commit", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
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
    public void Execute_AppleShipReleasePlan_SupportsIosStoreAndMacBetaWithoutMacStorePreparation()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            WriteXcodeGenVersionSource(root, "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            spec.AppleApps.Apps =
            [
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
            ];
            EnableInternalAppleShipTargets(spec);
            var readinessPlatforms = new List<ApplePlatform>();
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                getHighestAppleBuildNumber: (_, _, _) => 13,
                checkAppleReleaseReadiness: (_, request) =>
                {
                    readinessPlatforms.Add(request.Platform);
                    return CreateReadyReleaseReadiness(request);
                });

            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = AppleShipPlanSourceCommit,
                AppleShipTestFlightTargets = ["CasaRay Mac"],
                AppleShipAppStoreTargets = ["CasaRay iOS"]
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(PowerForgeAppleShipPhase.Release, result.AppleReceipt!.ShipPhase);
            Assert.False(result.AppleAppPlan!.SyncScreenshots);
            Assert.Empty(result.AppleAppPlan.TestFlightBetaGroupIds);
            Assert.Empty(result.AppleAppPlan.TestFlightBetaGroupNames);
            Assert.Empty(result.AppleAppPlan.TestFlightTesterEmails);
            Assert.Equal([ApplePlatform.iOS], readinessPlatforms);
            var ios = Assert.Single(result.AppleReceipt.Targets, target => target.Platform == ApplePlatform.iOS);
            Assert.True(ios.ShipToAppStoreReview);
            Assert.False(ios.ShipToTestFlight);
            Assert.True(ios.ReadinessChecked);
            var mac = Assert.Single(result.AppleReceipt.Targets, target => target.Platform == ApplePlatform.macOS);
            Assert.True(mac.ShipToTestFlight);
            Assert.False(mac.ShipToAppStoreReview);
            Assert.False(mac.ReadinessChecked);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleShipRelease_RunsStoreStepsOnlyForExplicitAppStoreTargets()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            WriteXcodeGenVersionSource(root, "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.CleanupAfterProcessing = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            spec.AppleApps.Apps =
            [
                spec.AppleApps.Apps.Single(),
                new AppleAppConfiguration
                {
                    Name = "CasaRay Mac",
                    BundleId = "com.evotecit.casaray",
                    Platform = ApplePlatform.macOS,
                    ArchiveVariant = AppleArchiveVariant.MacCatalyst,
                    ProjectPath = "CasaRay.xcodeproj",
                    Scheme = "CasaRayMac",
                    AppStoreConnectAppId = "6778025328"
                }
            ];
            EnableInternalAppleShipTargets(spec);
            var sourceCommit = CommitAppleShipSource(root);
            var archived = new List<string>();
            var uploaded = new List<string>();
            var prepared = new List<ApplePlatform>();
            var submitted = new List<ApplePlatform>();
            var uploadCompleted = false;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, uploadCompleted ? "VALID" : null),
                archiveAppleApp: request =>
                {
                    archived.Add(request.Scheme!);
                    return CreateSuccessfulArchive(request);
                },
                uploadAppleApp: request =>
                {
                    uploaded.Add(request.BundleId! + ":" + request.ArchivePath);
                    if (uploaded.Count == 2)
                        uploadCompleted = true;
                    return CreateSuccessfulUpload(request);
                },
                prepareAppleDistribution: request =>
                {
                    prepared.Add(request.Platform);
                    return CreateSuccessfulPreparation(request);
                },
                generateAppleProject: _ => true,
                getHighestAppleBuildNumber: (_, _, _) => 13,
                submitAppleReview: request =>
                {
                    submitted.Add(request.Platform);
                    return new AppStoreConnectReviewSubmissionResult
                    {
                        AppId = request.AppId,
                        VersionString = request.VersionString,
                        BuildNumber = request.BuildNumber,
                        Platform = request.Platform
                    };
                },
                checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request));
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = sourceCommit,
                AppleShipTestFlightTargets = ["CasaRay Mac"],
                AppleShipAppStoreTargets = ["CasaRay iOS"]
            };
            var planned = service.Execute(spec, request);

            var execution = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = request.ConfigPath,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = sourceCommit,
                AppleShipTestFlightTargets = ["CasaRay Mac"],
                AppleShipAppStoreTargets = ["CasaRay iOS"],
                AppleExpectedPlanSha256 = planned.AppleReceipt!.PlanSha256,
                AppleActionConfirmed = true
            });

            Assert.True(execution.Success, execution.ErrorMessage);
            Assert.Equal(2, archived.Count);
            Assert.Equal(2, uploaded.Count);
            Assert.Equal([ApplePlatform.iOS], prepared);
            Assert.Equal([ApplePlatform.iOS], submitted);
            var mac = Assert.Single(execution.AppleReceipt!.Targets, target => target.Platform == ApplePlatform.macOS);
            Assert.Null(mac.AppReviewSubmissionId);
            Assert.True(mac.UploadPerformed);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleShipRelease_ResumesItsAttestedUploadWithOriginalIntentHash()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.6.0", "14");
            WriteXcodeGenVersionSource(root, "1.6.0", "14");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.VersionSourcePath = "project.yml";
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.CleanupAfterProcessing = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            EnableInternalAppleShipTargets(spec);
            var sourceCommit = CommitAppleShipSource(root);
            var uploaded = false;
            var planRequest = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = sourceCommit,
                AppleShipAppStoreTargets = ["CasaRay iOS"]
            };
            var initialService = CreateAppleAutomationService(
                request => CreateReleaseState(request, uploaded ? "VALID" : null),
                archiveAppleApp: CreateSuccessfulArchive,
                uploadAppleApp: request =>
                {
                    uploaded = true;
                    var result = CreateSuccessfulUpload(request);
                    result.BuildUploadId = "ship-upload-14";
                    return result;
                },
                prepareAppleDistribution: _ => throw new InvalidOperationException("simulated interruption after upload"),
                generateAppleProject: _ => true,
                getHighestAppleBuildNumber: (_, _, _) => uploaded ? 14 : 13,
                checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request));
            var planned = initialService.Execute(spec, planRequest);

            var interrupted = initialService.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = planRequest.ConfigPath,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = sourceCommit,
                AppleShipAppStoreTargets = ["CasaRay iOS"],
                AppleExpectedPlanSha256 = planned.AppleReceipt!.PlanSha256,
                AppleActionConfirmed = true,
                AppleWaitForProcessing = false
            });

            Assert.False(interrupted.Success);
            Assert.Contains("simulated interruption", interrupted.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                new AppleReleaseReceiptStore().ReadAll(interrupted.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAttested" &&
                           string.Equals(receipt.PlanSha256, planned.AppleReceipt.PlanSha256, StringComparison.OrdinalIgnoreCase));

            var archiveCalls = 0;
            var uploadCalls = 0;
            var prepareCalls = 0;
            var submitCalls = 0;
            var resumedService = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                archiveAppleApp: request =>
                {
                    archiveCalls++;
                    return CreateSuccessfulArchive(request);
                },
                uploadAppleApp: request =>
                {
                    uploadCalls++;
                    return CreateSuccessfulUpload(request);
                },
                prepareAppleDistribution: request =>
                {
                    prepareCalls++;
                    return CreateSuccessfulPreparation(request);
                },
                generateAppleProject: _ => true,
                getHighestAppleBuildNumber: (_, _, _) => 14,
                getAppleBuildUpload: (_, id) => new AppStoreConnectBuildUploadInfo
                {
                    Id = id,
                    State = "COMPLETE",
                    MarketingVersion = "1.6.0",
                    BuildNumber = "14",
                    Platform = "IOS"
                },
                submitAppleReview: request =>
                {
                    submitCalls++;
                    return new AppStoreConnectReviewSubmissionResult
                    {
                        AppId = request.AppId,
                        VersionString = request.VersionString,
                        BuildNumber = request.BuildNumber,
                        Platform = request.Platform
                    };
                },
                checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request));
            planRequest.AppleExpectedPlanSha256 = planned.AppleReceipt.PlanSha256;
            var resumedPlan = resumedService.Execute(spec, planRequest);

            Assert.Equal(planned.AppleReceipt.PlanSha256, resumedPlan.AppleReceipt!.PlanSha256);
            var resumed = resumedService.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = planRequest.ConfigPath,
                AppleAction = PowerForgeAppleReleaseAction.Ship,
                AppleMarketingVersion = "1.6.0",
                AppleSourceCommit = sourceCommit,
                AppleShipAppStoreTargets = ["CasaRay iOS"],
                AppleExpectedPlanSha256 = planned.AppleReceipt.PlanSha256,
                AppleActionConfirmed = true,
                AppleWaitForProcessing = false
            });

            Assert.True(resumed.Success, resumed.ErrorMessage);
            Assert.Equal(0, archiveCalls);
            Assert.Equal(0, uploadCalls);
            Assert.Equal(1, prepareCalls);
            Assert.Equal(1, submitCalls);
            Assert.True(Assert.Single(resumed.AppleApps).ResumedExistingBuild);
            Assert.False(Assert.Single(resumed.AppleApps).AdoptedExistingBuild);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleShipPlan_RejectsUnknownRouteTarget()
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
            EnableInternalAppleShipTargets(spec);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                CreateAppleAutomationService(
                        request => CreateReleaseState(request, "VALID"),
                        getHighestAppleBuildNumber: (_, _, _) => 13)
                    .Execute(spec, new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        PlanOnly = true,
                        AppleAction = PowerForgeAppleReleaseAction.Ship,
                        AppleMarketingVersion = "1.6.0",
                        AppleSourceCommit = AppleShipPlanSourceCommit,
                        AppleShipTestFlightTargets = ["Missing target"]
                    }));

            Assert.Contains("Unknown Apple Ship internal TestFlight target", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void EnableInternalAppleShipTargets(PowerForgeReleaseSpec spec)
    {
        foreach (var app in spec.AppleApps!.Apps.Where(static app =>
                     app.DistributionRoute is AppleDistributionRoute.AppStore or AppleDistributionRoute.TestFlightOnly))
        {
            app.TestFlightPolicy = AppleTestFlightPolicy.Internal;
        }
    }

    private static string CommitAppleShipSource(string root)
    {
        File.WriteAllText(Path.Combine(root, ".gitignore"), "build/\n");
        RunSnapshotGit(root, "init", "--quiet");
        RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
        RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
        RunSnapshotGit(root, "add", ".");
        RunSnapshotGit(root, "commit", "--quiet", "-m", "exact Apple Ship source");
        return RunSnapshotGit(root, "rev-parse", "HEAD").Trim();
    }
}
