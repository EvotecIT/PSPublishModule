namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_AppleArchive_RejectsMalformedJournalBeforeArchiveMutation()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var history = Path.Combine(root, "build", "powerforge", "apple", "receipts");
            Directory.CreateDirectory(history);
            File.WriteAllText(Path.Combine(history, "broken.json"), "{not-json");
            var archiveCalls = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null),
                archiveAppleApp: request =>
                {
                    archiveCalls++;
                    return CreateSuccessfulArchive(request);
                });

            var result = service.Execute(
                CreateAppleAutomationSpec(root, keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Archive
                });

            Assert.False(result.Success);
            Assert.Equal(0, archiveCalls);
            Assert.Contains("not valid JSON", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_RejectsPrivateArchiveSnapshotMutation()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.WaitForProcessing = false;
            string? uploaderArchivePath = null;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null),
                archiveAppleApp: request =>
                {
                    var archive = Directory.CreateDirectory(request.ArchivePath!);
                    File.WriteAllText(Path.Combine(archive.FullName, "payload"), "before");
                    return CreateSuccessfulArchive(request);
                },
                uploadAppleApp: request =>
                {
                    uploaderArchivePath = request.ArchivePath;
                    File.WriteAllText(Path.Combine(request.ArchivePath!, "payload"), "after");
                    return CreateSuccessfulUpload(request);
                });

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleSourceCommit = "0123456789abcdef0123456789abcdef01234567",
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            Assert.False(result.Success);
            Assert.NotNull(uploaderArchivePath);
            Assert.NotEqual(Assert.Single(result.AppleAppPlan!.Apps).ArchivePath, uploaderArchivePath);
            Assert.Equal("before", File.ReadAllText(Path.Combine(Assert.Single(result.AppleAppPlan.Apps).ArchivePath, "payload")));
            Assert.Contains("private Apple upload archive snapshot changed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAttested");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleStatusPlan_ProducesReadOnlyExplicitPlan()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");

            var result = new PowerForgeReleaseService(new NullLogger()).Execute(
                CreateAppleAutomationSpec(root, keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.Status,
                    AppleSourceCommit = new string('a', 64)
                });

            Assert.True(result.Success, result.ErrorMessage);
            var plan = Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan);
            Assert.Equal(PowerForgeAppleReleaseAction.Status, plan.Action);
            Assert.Equal(new string('a', 64), plan.SourceCommit);
            Assert.False(plan.Archive);
            Assert.False(plan.Upload);
            Assert.False(plan.PrepareDistribution);
            Assert.False(plan.SelectBuildForDistribution);
            Assert.False(plan.SubmitForReview);
            Assert.EndsWith(
                Path.Combine("build", "powerforge", "apple", "release-receipt.json"),
                plan.ReceiptPath,
                StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleProtectedPlan_BindsExactObservedAppleState()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var readinessReady = false;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                checkAppleReleaseReadiness: (_, request) => new AppStoreConnectReleaseReadinessResult
                {
                    AppId = request.AppId,
                    VersionString = request.VersionString,
                    BuildNumber = request.BuildNumber,
                    Platform = request.Platform,
                    IsReady = readinessReady,
                    Checks =
                    [
                        new AppStoreConnectReleaseReadinessCheck
                        {
                            Name = "metadata",
                            Passed = readinessReady,
                            Message = readinessReady ? "Metadata is ready." : "Metadata is incomplete."
                        }
                    ]
                });
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.SubmitAppReview
            };

            var first = service.Execute(spec, request);
            var second = service.Execute(spec, request);
            readinessReady = true;
            var changed = service.Execute(spec, request);

            Assert.True(first.Success, first.ErrorMessage);
            Assert.Matches("^[0-9A-F]{64}$", first.AppleReceipt!.PlanSha256!);
            Assert.Equal(first.AppleReceipt.PlanSha256, second.AppleReceipt!.PlanSha256);
            Assert.NotEqual(first.AppleReceipt.PlanSha256, changed.AppleReceipt!.PlanSha256);
            var target = Assert.Single(first.AppleReceipt.Targets);
            Assert.Equal("build-id", target.BuildId);
            Assert.Equal("VALID", target.BuildProcessingState);
            Assert.Equal("version-id", target.DistributionVersionId);
            Assert.True(target.ReadinessChecked);
            Assert.False(target.ReadyForSubmission);
            Assert.Matches("^[0-9A-F]{64}$", target.ReadinessSha256!);
            Assert.False(Assert.Single(target.ReadinessChecks!).Passed);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ConfiguredProtectedPlan_BindsExactObservedAppleState()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var processingState = "PROCESSING";
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, processingState));
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Archive = false;
            spec.AppleApps.Upload = false;
            spec.AppleApps.SubmitForReview = true;
            spec.AppleApps.SkipReviewReadinessCheck = true;
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Configured
            };

            var first = service.Execute(spec, request);
            processingState = "VALID";
            var changed = service.Execute(spec, request);

            Assert.Equal("PROCESSING", Assert.Single(first.AppleReceipt!.Targets).BuildProcessingState);
            Assert.Equal("VALID", Assert.Single(changed.AppleReceipt!.Targets).BuildProcessingState);
            Assert.NotEqual(first.AppleReceipt.PlanSha256, changed.AppleReceipt.PlanSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ConfiguredPlan_BindsArchiveAndUploadExecutionFlags()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"));
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Archive = false;
            spec.AppleApps.Upload = false;
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                PlanOnly = true,
                AppleAction = PowerForgeAppleReleaseAction.Configured
            };

            var statusOnly = service.Execute(spec, request);
            spec.AppleApps.Archive = true;
            var archive = service.Execute(spec, request);
            spec.AppleApps.Upload = true;
            var archiveAndUpload = service.Execute(spec, request);

            Assert.NotEqual(statusOnly.AppleReceipt!.PlanSha256, archive.AppleReceipt!.PlanSha256);
            Assert.NotEqual(archive.AppleReceipt.PlanSha256, archiveAndUpload.AppleReceipt!.PlanSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleProtectedMutationRejectsStateChangedAfterPlanApproval()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var readinessReady = false;
            var submitCalls = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
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
                checkAppleReleaseReadiness: (_, request) => new AppStoreConnectReleaseReadinessResult
                {
                    AppId = request.AppId,
                    VersionString = request.VersionString,
                    BuildNumber = request.BuildNumber,
                    Platform = request.Platform,
                    IsReady = readinessReady,
                    Checks =
                    [
                        new AppStoreConnectReleaseReadinessCheck
                        {
                            Name = "metadata",
                            Passed = readinessReady,
                            Message = readinessReady ? "Metadata is ready." : "Metadata is incomplete."
                        }
                    ]
                });
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var plan = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.SubmitAppReview
                });
            readinessReady = true;

            var execution = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.SubmitAppReview,
                    AppleActionConfirmed = true,
                    AppleExpectedPlanSha256 = plan.AppleReceipt!.PlanSha256
                });

            Assert.False(execution.Success);
            Assert.Equal(0, submitCalls);
            Assert.Contains("changed after plan approval", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleScreenshotReplacementRejectsRemoteInventoryChangedAfterPlanApproval()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var screenshotFolder = Directory.CreateDirectory(Path.Combine(root, "screenshots"));
            File.WriteAllText(Path.Combine(screenshotFolder.FullName, "home.png"), "approved pixels");
            WriteScreenshotConfig(root, "screenshots.json", "6778025328", "1.2.0", "iOS", "screenshots", qualityEnabled: false);
            var remoteScreenshotId = "screenshot-before";
            var prepareCalls = 0;
            string? forwardedScreenshotInventorySha256 = null;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                prepareAppleDistribution: request =>
                {
                    prepareCalls++;
                    forwardedScreenshotInventorySha256 = request.ExpectedScreenshotInventorySha256;
                    return new AppStoreConnectReleasePreparationResult();
                },
                checkAppleReleaseReadiness: (_, request) => new AppStoreConnectReleaseReadinessResult
                {
                    AppId = request.AppId,
                    VersionString = request.VersionString,
                    BuildNumber = request.BuildNumber,
                    Platform = request.Platform,
                    ScreenshotSets =
                    [
                        new AppStoreConnectReleaseScreenshotSetReadiness
                        {
                            ScreenshotDisplayType = "APP_IPHONE_65",
                            ScreenshotSetId = "set-1",
                            Count = 1,
                            Screenshots =
                            [
                                new AppStoreConnectReleaseScreenshotAssetReadiness
                                {
                                    Id = remoteScreenshotId,
                                    FileName = "remote.png",
                                    FileSize = 1234,
                                    SourceFileChecksum = remoteScreenshotId + "-checksum",
                                    AssetDeliveryState = "COMPLETE"
                                }
                            ]
                        }
                    ]
                });
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Archive = false;
            spec.AppleApps.Upload = false;
            spec.AppleApps!.SyncScreenshots = true;
            spec.AppleApps.ReplaceScreenshots = true;
            spec.AppleApps.ScreenshotConfigPath = "screenshots.json";

            var approved = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.Configured
                });
            var approvedTarget = Assert.Single(approved.AppleReceipt!.Targets);
            Assert.False(approvedTarget.ReadinessChecked);
            Assert.Matches("^[0-9A-F]{64}$", approvedTarget.ScreenshotInventorySha256!);
            var approvedPlan = Assert.IsType<PowerForgeAppleReleasePlan>(approved.AppleAppPlan);
            Assert.Equal(
                approvedTarget.ScreenshotInventorySha256,
                Assert.Single(approvedPlan.Apps).ExpectedScreenshotInventorySha256);

            var missingPlanApproval = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Configured,
                    AppleActionConfirmed = true
                });
            Assert.False(missingPlanApproval.Success);
            Assert.Contains("reviewed exact Apple plan", missingPlanApproval.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, prepareCalls);

            remoteScreenshotId = "screenshot-after";

            var execution = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Configured,
                    AppleActionConfirmed = true,
                    AppleExpectedPlanSha256 = approved.AppleReceipt.PlanSha256
                });

            Assert.False(execution.Success);
            Assert.Equal(0, prepareCalls);
            Assert.Contains("changed after plan approval", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            var refreshed = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.Configured
                });
            var refreshedInventorySha256 = Assert.Single(refreshed.AppleReceipt!.Targets).ScreenshotInventorySha256;
            var successfulExecution = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Configured,
                    AppleActionConfirmed = true,
                    AppleExpectedPlanSha256 = refreshed.AppleReceipt.PlanSha256
                });

            Assert.True(successfulExecution.Success, successfulExecution.ErrorMessage);
            Assert.Equal(1, prepareCalls);
            Assert.Equal(refreshedInventorySha256, forwardedScreenshotInventorySha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ExplicitAppleAction_IgnoresEveryNonAppleReleaseSection()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.Module = new PowerForgeModuleReleaseOptions
            {
                ScriptPath = "missing-module-build.ps1"
            };
            spec.Packages = new ProjectBuildConfiguration();
            spec.Tools = new PowerForgeToolReleaseSpec();
            spec.WorkspaceValidation = new PowerForgeWorkspaceValidationOptions
            {
                ConfigPath = "missing-workspace-validation.json"
            };
            spec.GitHub = new PowerForgeReleaseGitHubOptions
            {
                Publish = true
            };

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Status
                    });

            Assert.True(result.Success);
            Assert.Null(result.ModulePlan);
            Assert.Null(result.Module);
            Assert.Null(result.Packages);
            Assert.Null(result.ToolPlan);
            Assert.Null(result.Tools);
            Assert.Null(result.DotNetToolPlan);
            Assert.Null(result.DotNetTools);
            Assert.Null(result.WorkspaceValidationPlan);
            Assert.Null(result.WorkspaceValidation);
            Assert.Null(result.UnifiedGitHubRelease);
            Assert.Null(result.ReleaseManifestPath);
            Assert.Equal(PowerForgeAppleReleaseAction.Status, result.AppleReceipt!.Action);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePreparePlan_EnablesConfiguredDistributionInputs()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(Path.Combine(root, "metadata.json"), "{}");
            WriteScreenshotConfig(root, "screenshots.json", "6778025328", "1.2.0", "iOS", ".", qualityEnabled: false);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.MetadataConfigPath = "metadata.json";
            spec.AppleApps.ScreenshotConfigPath = "screenshots.json";

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        PlanOnly = true,
                        AppleAction = PowerForgeAppleReleaseAction.Prepare
                    });

            var plan = Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan);
            Assert.True(plan.PrepareDistribution);
            Assert.True(plan.SelectBuildForDistribution);
            Assert.True(plan.SyncMetadata);
            Assert.False(plan.SyncScreenshots);
            Assert.True(plan.CheckReleaseReadiness);
            Assert.False(plan.Archive);
            Assert.False(plan.Upload);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Execute_AppleAdvancePlan_RequiresExplicitScreenshotOptIn(
        bool configuredSync,
        bool expectedSync)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            WriteScreenshotConfig(root, "screenshots.json", "6778025328", "1.2.0", "iOS", ".", qualityEnabled: false);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.ScreenshotConfigPath = "screenshots.json";
            spec.AppleApps.SyncScreenshots = configuredSync;

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        PlanOnly = true,
                        AppleAction = PowerForgeAppleReleaseAction.Advance
                    });

            var plan = Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan);
            Assert.Equal(expectedSync, plan.SyncScreenshots);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleScreenshotReplacementPlan_IsIsolatedAndRequiresConfirmation()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            Directory.CreateDirectory(Path.Combine(root, "screenshots"));
            WriteScreenshotConfig(root, "screenshots.json", "6778025328", "1.2.0", "iOS", "screenshots", qualityEnabled: false);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.ScreenshotConfigPath = "screenshots.json";
            spec.AppleApps.ReplaceScreenshots = true;

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        PlanOnly = true,
                        AppleAction = PowerForgeAppleReleaseAction.Screenshots
                    });

            var plan = Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan);
            Assert.True(plan.SyncScreenshots);
            Assert.True(plan.ReplaceScreenshots);
            Assert.True(plan.CheckReleaseReadiness);
            Assert.False(plan.PrepareDistribution);
            Assert.False(plan.SyncMetadata);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Screenshots
                    }));
            Assert.Contains("explicit confirmation", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleReviewSubmission_RequiresExplicitConfirmation()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    CreateAppleAutomationSpec(root, keyPath),
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.SubmitAppReview
                    }));

            Assert.Contains("explicit confirmation", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("TestFlightReview")]
    [InlineData("AppReview")]
    [InlineData("Release")]
    [InlineData("ScreenshotReplacement")]
    public void Execute_ConfiguredRiskyAppleFlags_RequireExplicitConfirmation(string risk)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            switch (risk)
            {
                case "TestFlightReview":
                    spec.AppleApps!.SubmitTestFlightBetaReview = true;
                    break;
                case "AppReview":
                    spec.AppleApps!.SubmitForReview = true;
                    break;
                case "Release":
                    spec.AppleApps!.ReleaseApprovedVersion = true;
                    break;
                case "ScreenshotReplacement":
                    spec.AppleApps!.SyncScreenshots = true;
                    spec.AppleApps.ReplaceScreenshots = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(risk), risk, "Unknown test risk.");
            }

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Configured
                    }));

            Assert.Contains("explicit confirmation", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleArchiveFailure_WritesActionableReceipt()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Archive action must not query App Store Connect."),
                archiveAppleApp: request => new AppleAppArchiveResult
                {
                    ArchivePath = request.ArchivePath!,
                    Destination = request.Destination!,
                    ProcessResult = new ProcessRunResult(
                        65,
                        string.Empty,
                        "codesign failed",
                        "xcodebuild",
                        TimeSpan.FromSeconds(1),
                        false)
                });

            var result = service.Execute(
                CreateAppleAutomationSpec(root, keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Archive
                });

            Assert.False(result.Success);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            Assert.False(receipt.Success);
            Assert.Contains("exit code 65", Assert.IsType<string>(receipt.ErrorMessage), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "exit code 65",
                Assert.IsType<string>(Assert.Single(receipt.Targets).ErrorMessage),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Upload)]
    [InlineData(PowerForgeAppleReleaseAction.UploadExisting)]
    public void Execute_AppleUploadAction_RequiresExplicitAdoptionThenResumesExactRemoteBuild(
        PowerForgeAppleReleaseAction action)
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var seedStateCalls = 0;
            var seedService = CreateAppleAutomationService(
                request => CreateReleaseState(request, ++seedStateCalls == 1 ? null : "VALID"),
                archiveAppleApp: request =>
                {
                    var archive = Directory.CreateDirectory(request.ArchivePath!);
                    File.WriteAllText(Path.Combine(archive.FullName, "archive.txt"), "signed archive");
                    return CreateSuccessfulArchive(request);
                },
                uploadAppleApp: CreateSuccessfulUpload);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.CleanupAfterProcessing = false;
            var seeded = seedService.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleSourceCommit = sourceCommit,
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });
            Assert.True(seeded.Success, seeded.ErrorMessage);
            var seededTarget = Assert.Single(seeded.AppleReceipt!.Targets);
            Assert.True(seededTarget.UploadPerformed);
            Assert.NotNull(seededTarget.ArchiveSha256);
            Assert.NotNull(seededTarget.UploadAttestationAttemptId);

            var status = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleSourceCommit = sourceCommit,
                        AppleAction = PowerForgeAppleReleaseAction.Status
                    });
            Assert.True(status.Success, status.ErrorMessage);
            Assert.Equal(PowerForgeAppleReleaseAction.Status, status.AppleReceipt!.Action);
            Assert.False(Assert.Single(status.AppleReceipt.Targets).UploadPerformed);

            var stateCalls = 0;
            var service = CreateAppleAutomationService(
                request =>
                {
                    stateCalls++;
                    return CreateReleaseState(request, "VALID");
                },
                getAvailableBytes: _ => throw new InvalidOperationException("Resumed builds must skip archive preflight."));

            var blocked = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleSourceCommit = sourceCommit,
                    AppleAction = action
                });
            Assert.False(blocked.Success);
            Assert.Contains(
                "continuity evidence, not authority",
                Assert.Single(blocked.AppleReceipt!.Targets).ErrorMessage,
                StringComparison.OrdinalIgnoreCase);
            stateCalls = 0;

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleSourceCommit = sourceCommit,
                    AppleAction = action,
                    AppleAdoptExistingBuild = true,
                    AppleActionConfirmed = true
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, stateCalls);
            var app = Assert.Single(result.AppleApps);
            Assert.True(app.ResumedExistingBuild);
            Assert.False(app.AdoptedExistingBuild);
            Assert.Null(app.Archive);
            Assert.Null(app.Upload);
            Assert.Equal(new[] { "archive", "upload" }, app.SkippedSteps);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            var target = Assert.Single(receipt.Targets);
            Assert.Equal("1.2.0", target.Version);
            Assert.Equal("9", target.Build);
            Assert.Equal("VALID", target.BuildProcessingState);
            Assert.True(target.ResumedExistingBuild);
            Assert.False(target.ReadinessChecked);
            Assert.Null(target.ReadyForSubmission);
            Assert.Null(target.ScreenshotCount);
            Assert.Null(target.ScreenshotDeliveryStates);
            Assert.False(target.TestFlightBetaGroupsConfigured);
            Assert.Contains(
                "External TestFlight is eligible; configure the intended beta group before explicitly requesting Beta App Review.",
                target.NextActions);
            Assert.Equal("build/powerforge/apple/release-receipt.json", receipt.ReceiptPath);
            var persistedReceiptPath = Path.Combine(root, receipt.ReceiptPath!);
            Assert.True(File.Exists(persistedReceiptPath));
            var json = File.ReadAllText(persistedReceiptPath);
            Assert.Contains("\"schemaVersion\"", json, StringComparison.Ordinal);
            Assert.Contains("\"targets\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"errorMessage\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain(root, json, StringComparison.Ordinal);
            Assert.DoesNotContain("private-key", json, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_RejectsExistingBuildWithoutExactUploadAttestation()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");

            var result = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID")).Execute(
                    CreateAppleAutomationSpec(root, keyPath),
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleSourceCommit = "0123456789abcdef0123456789abcdef01234567",
                        AppleAction = PowerForgeAppleReleaseAction.Upload
                    });

            Assert.False(result.Success);
            Assert.Contains("no immutable local upload receipt", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.False(target.ResumedExistingBuild);
            Assert.False(target.AdoptedExistingBuild);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_AdoptsExistingBuildOnlyWithExplicitConfirmedOverride()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var stateCalls = 0;
            var service = CreateAppleAutomationService(request =>
            {
                stateCalls++;
                return CreateReleaseState(request, "VALID");
            });

            var rejected = Assert.Throws<InvalidOperationException>(() => service.Execute(
                CreateAppleAutomationSpec(root, keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleAdoptExistingBuild = true
                }));
            Assert.Contains("explicit confirmation", rejected.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, stateCalls);

            var result = service.Execute(
                    CreateAppleAutomationSpec(root, keyPath),
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleSourceCommit = "0123456789abcdef0123456789abcdef01234567",
                        AppleAction = PowerForgeAppleReleaseAction.Upload,
                        AppleAdoptExistingBuild = true,
                        AppleActionConfirmed = true
                    });

            Assert.True(result.Success, result.ErrorMessage);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.True(target.ResumedExistingBuild);
            Assert.True(target.AdoptedExistingBuild);
            Assert.Null(target.ArchiveSha256);
            Assert.Contains(target.Diagnostics, diagnostic =>
                diagnostic.Code == "APPLE_BUILD_ADOPTED_WITHOUT_UPLOAD_ATTESTATION");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleAdoptionPlan_BindsTheObservedRemoteBuild()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var processingState = (string?)null;
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, processingState));
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.UploadExisting,
                AppleAdoptExistingBuild = true,
                AppleActionConfirmed = true,
                PlanOnly = true
            };

            var withoutBuild = Assert.Throws<InvalidOperationException>(() =>
                service.Execute(CreateAppleAutomationSpec(root, keyPath), request));
            processingState = "VALID";
            var withBuild = service.Execute(CreateAppleAutomationSpec(root, keyPath), request);

            var presentTarget = Assert.Single(withBuild.AppleReceipt!.Targets);
            Assert.Contains("uniquely selected", withoutBuild.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("build-id", presentTarget.BuildId);
            Assert.Equal("VALID", presentTarget.BuildProcessingState);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleStatus_PreservesBetaReviewActionWhenGroupIsConfigured()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.TestFlightBetaGroupNames = new[] { "Home" };

            var result = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Status
                    });

            var target = Assert.Single(Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt).Targets);
            Assert.True(target.TestFlightBetaGroupsConfigured);
            Assert.Contains("Submit the TestFlight build to Beta App Review.", target.NextActions);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_WaitsForProcessingInsideSharedRunner()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var states = new Queue<string?>(new string?[] { null, "PROCESSING", "VALID" });
            var delays = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, states.Dequeue()),
                _ => delays++,
                archiveAppleApp: request =>
                {
                    var archive = Directory.CreateDirectory(request.ArchivePath!);
                    File.WriteAllText(Path.Combine(archive.FullName, "archive.txt"), "signed archive");
                    return CreateSuccessfulArchive(request);
                },
                uploadAppleApp: CreateSuccessfulUpload);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.CleanupAfterProcessing = false;
            spec.AppleApps.Automation.PollIntervalSeconds = 1;
            spec.AppleApps.Automation.ProcessingTimeoutSeconds = 2;

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            Assert.True(result.Success);
            Assert.Equal(1, delays);
            Assert.Equal("VALID", Assert.Single(result.AppleReceipt!.Targets).BuildProcessingState);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("FAILED")]
    public void Execute_AppleUpload_TerminalProcessingFailureWritesReceipt(string processingState)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState))
                .Execute(
                    CreateAppleAutomationSpec(root, keyPath),
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload
                    });

            Assert.False(result.Success);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            Assert.False(receipt.Success);
            var target = Assert.Single(receipt.Targets);
            Assert.Equal(processingState, target.BuildProcessingState);
            Assert.Contains(processingState, Assert.IsType<string>(target.ErrorMessage), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, receipt.ReceiptPath!)));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_BuildUploadFailureReportsAppleValidationIssueImmediately()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var upload = CreateSuccessfulUpload(new AppleAppArchiveUploadRequest
            {
                ArchivePath = Path.Combine(root, "build", "CasaRay.xcarchive"),
                ExportPath = Path.Combine(root, "build", "export")
            });
            upload.BuildUploadId = "upload-9";
            var delays = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null),
                delay: _ => delays++,
                archiveAppleApp: CreateSuccessfulArchive,
                uploadAppleApp: _ => upload,
                getAppleBuildUpload: (_, id) => new AppStoreConnectBuildUploadInfo
                {
                    Id = id,
                    State = "FAILED",
                    Errors = new[]
                    {
                        new AppStoreConnectBuildUploadIssue
                        {
                            Code = "90683",
                            Description = "Missing purpose string in Info.plist."
                        }
                    }
                });

            var result = service.Execute(
                CreateAppleAutomationSpec(root, keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            Assert.False(result.Success);
            Assert.Equal(0, delays);
            var target = Assert.Single(Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt).Targets);
            Assert.Equal("upload-9", target.BuildUploadId);
            Assert.Contains("90683", Assert.IsType<string>(target.ErrorMessage), StringComparison.Ordinal);
            Assert.Contains("Missing purpose string", target.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_ProcessingTimeoutWritesLastKnownStateReceipt()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.PollIntervalSeconds = 1;
            spec.AppleApps.Automation.ProcessingTimeoutSeconds = 1;
            var delays = 0;

            var stateCalls = 0;
            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, ++stateCalls == 1 ? null : "PROCESSING"),
                    _ => delays++,
                    archiveAppleApp: request =>
                    {
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "archive.txt"), "signed archive");
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: CreateSuccessfulUpload)
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload
                    });

            Assert.False(result.Success);
            Assert.Equal(1, delays);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            var target = Assert.Single(receipt.Targets);
            Assert.Equal("PROCESSING", target.BuildProcessingState);
            Assert.Contains("Timed out", Assert.IsType<string>(target.ErrorMessage), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, receipt.ReceiptPath!)));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_PostUploadStatusFailureWritesReceipt()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var stateCalls = 0;
            var service = CreateAppleAutomationService(
                request =>
                {
                    stateCalls++;
                    if (stateCalls == 1)
                        return CreateReleaseState(request, processingState: null);
                    throw new InvalidOperationException("App Store Connect status unavailable after upload.");
                },
                archiveAppleApp: CreateSuccessfulArchive,
                uploadAppleApp: CreateSuccessfulUpload);

            var result = service.Execute(
                CreateAppleAutomationSpec(root, keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            Assert.False(result.Success);
            Assert.Equal(3, stateCalls);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            Assert.False(receipt.Success);
            Assert.Contains(
                "status unavailable after upload",
                Assert.IsType<string>(Assert.Single(receipt.Targets).ErrorMessage),
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, receipt.ReceiptPath!)));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_ResumesFromImmediateAttestationBeforeRemoteBuildIsVisible()
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            var stateCalls = 0;
            var seeded = CreateAppleAutomationService(
                    request =>
                    {
                        if (++stateCalls == 1)
                            return CreateReleaseState(request, processingState: null);
                        throw new InvalidOperationException("final readback unavailable");
                    },
                    archiveAppleApp: request =>
                    {
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "payload"), "signed archive");
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: request =>
                    {
                        var upload = CreateSuccessfulUpload(request);
                        upload.BuildUploadId = "build-upload-9";
                        return upload;
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit,
                    AppleWaitForProcessing = false
                });
            Assert.False(seeded.Success);
            Assert.Contains(
                new AppleReleaseReceiptStore().ReadAll(seeded.AppleAppPlan!),
                receipt => receipt.OperationPhase == "UploadAttested");

            var buildUploadQueries = 0;
            var resumed = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: _ => throw new InvalidOperationException("Verified resume must skip archive."),
                    uploadAppleApp: _ => throw new InvalidOperationException("Verified resume must skip upload."),
                    getAppleBuildUpload: (_, id) =>
                    {
                        buildUploadQueries++;
                        return new AppStoreConnectBuildUploadInfo { Id = id, State = "PROCESSING" };
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.UploadExisting,
                    AppleSourceCommit = sourceCommit,
                    AppleWaitForProcessing = false,
                    AppleAdoptExistingBuild = true,
                    AppleActionConfirmed = true
                });

            Assert.True(resumed.Success, resumed.ErrorMessage);
            Assert.Equal(1, buildUploadQueries);
            Assert.True(Assert.Single(resumed.AppleApps).ResumedExistingBuild);

            var rejected = CreateAppleAutomationService(
                    request => CreateReleaseState(request, processingState: null),
                    archiveAppleApp: _ => throw new InvalidOperationException("Terminal resume must skip archive."),
                    uploadAppleApp: _ => throw new InvalidOperationException("Terminal resume must skip upload."),
                    getAppleBuildUpload: (_, id) => new AppStoreConnectBuildUploadInfo
                    {
                        Id = id,
                        State = "FAILED",
                        Errors =
                        [
                            new AppStoreConnectBuildUploadIssue
                            {
                                Code = "90683",
                                Description = "Missing purpose string in Info.plist."
                            }
                        ]
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.UploadExisting,
                    AppleSourceCommit = sourceCommit,
                    AppleWaitForProcessing = false,
                    AppleAdoptExistingBuild = true,
                    AppleActionConfirmed = true
                });

            Assert.False(rejected.Success);
            var rejectedTarget = Assert.Single(rejected.AppleReceipt!.Targets);
            var rejectedError = Assert.IsType<string>(rejectedTarget.ErrorMessage);
            Assert.Contains("FAILED", rejectedError, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("90683", rejectedError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ConfiguredAppleUpload_HonorsProcessingWait()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Archive = true;
            spec.AppleApps.Upload = true;
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            var stateCalls = 0;
            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, ++stateCalls == 1 ? null : "VALID"),
                    archiveAppleApp: request =>
                    {
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "payload"), "signed archive");
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: CreateSuccessfulUpload)
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Configured,
                    AppleSourceCommit = "0123456789abcdef0123456789abcdef01234567"
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, stateCalls);
            Assert.Equal("VALID", Assert.Single(result.AppleApps).RemoteState!.Platforms.Single().MatchedBuild!.ProcessingState);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePrepareReceiptUsesCompletedPreparationWhenRemoteReadbackIsStale()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var service = CreateAppleAutomationService(
                request => new AppStoreConnectReleaseStateResult
                {
                    AppId = request.AppId,
                    VersionString = request.VersionString,
                    BuildNumber = request.BuildNumber,
                    Platforms =
                    [
                        new AppStoreConnectPlatformReleaseState
                        {
                            Platform = request.Platforms.Single(),
                            MatchedBuild = new AppStoreConnectBuildInfo
                            {
                                Id = "build-id",
                                Version = request.BuildNumber,
                                ProcessingState = "VALID",
                                MarketingVersion = request.VersionString
                            },
                            NextActions =
                            [
                                "Create App Store distribution version.",
                                "Select the requested build on the App Store version."
                            ]
                        }
                    ]
                },
                prepareAppleDistribution: CreateSuccessfulPreparation);

            var result = service.Execute(
                CreateAppleAutomationSpec(root, keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Prepare
                });

            Assert.True(result.Success, result.ErrorMessage);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal("version-id", target.DistributionVersionId);
            Assert.True(target.BuildSelected);
            Assert.DoesNotContain(target.NextActions, action => action.Contains("Create App Store distribution version", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(target.NextActions, action => action.StartsWith("Select ", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static PowerForgeReleaseSpec CreateAppleAutomationSpec(string root, string keyPath)
        => new()
        {
            AppleApps = new PowerForgeAppleReleaseOptions
            {
                ProjectRoot = root,
                ArchiveRoot = "build/powerforge/apple/archives",
                ExportRoot = "build/powerforge/apple/exports",
                AppStoreConnectApiKeyPath = keyPath,
                AppStoreConnectApiKeyId = "TESTKEY123",
                AppStoreConnectApiIssuerId = "issuer-id",
                Automation = new PowerForgeAppleReleaseAutomationOptions
                {
                    ReceiptPath = "build/powerforge/apple/release-receipt.json"
                },
                Apps = new[]
                {
                    new AppleAppConfiguration
                    {
                        Name = "CasaRay iOS",
                        BundleId = "com.evotecit.casaray",
                        Platform = ApplePlatform.iOS,
                        ProjectPath = "CasaRay.xcodeproj",
                        Scheme = "CasaRay",
                        AppStoreConnectAppId = "6778025328"
                    }
                }
            }
        };

    private static PowerForgeReleaseService CreateAppleAutomationService(
        Func<AppStoreConnectReleaseStateRequest, AppStoreConnectReleaseStateResult> getState,
        Action<TimeSpan>? delay = null,
        Func<string, long>? getAvailableBytes = null,
        Func<AppleAppArchiveRequest, AppleAppArchiveResult>? archiveAppleApp = null,
        Func<AppleAppArchiveUploadRequest, AppleAppArchiveUploadResult>? uploadAppleApp = null,
        Func<AppStoreConnectReleasePreparationRequest, AppStoreConnectReleasePreparationResult>? prepareAppleDistribution = null,
        Func<AppStoreConnectTestFlightDistributionRequest, AppStoreConnectTestFlightDistributionResult>? distributeTestFlight = null,
        Func<AppStoreConnectApiCredential, string, AppStoreConnectBuildUploadInfo?>? getAppleBuildUpload = null,
        Func<PowerForgeAppleAppReleaseTargetPlan, bool>? generateAppleProject = null,
        Func<AppStoreConnectApiCredential, string, ApplePlatform, long>? getHighestAppleBuildNumber = null,
        Func<AppStoreConnectApiCredential, string, ApplePlatform, PowerForgeAppleRemoteVersionInventory>? getAppleVersionInventory = null,
        Func<AppStoreConnectApiCredential, string, AppStoreConnectAppInfo[]>? findAppleApps = null,
        Func<AppleNotarizationRequest, AppleNotarizationResult>? notarizeAppleArtifact = null,
        Func<AppStoreConnectApiCredential, AppStoreConnectGovernanceSpec, AppStoreConnectGovernancePlan>? planAppleGovernance = null,
        Func<AppStoreConnectReviewSubmissionRequest, AppStoreConnectReviewSubmissionResult>? submitAppleReview = null,
        Func<AppStoreConnectApiCredential, AppStoreConnectReleaseReadinessRequest, AppStoreConnectReleaseReadinessResult>? checkAppleReleaseReadiness = null)
        => new(
            new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
            planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
            runTools: _ => throw new InvalidOperationException("Tools should not run."),
            loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
            planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
            runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
            publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
            archiveAppleApp: archiveAppleApp ?? (_ => throw new InvalidOperationException("Exact remote build should skip archive.")),
            uploadAppleApp: uploadAppleApp ?? (_ => throw new InvalidOperationException("Exact remote build should skip upload.")),
            prepareAppleDistribution: prepareAppleDistribution,
            distributeTestFlight: distributeTestFlight,
            getAppleReleaseState: getState,
            getAppleBuildUpload: getAppleBuildUpload,
            generateAppleProject: generateAppleProject,
            delay: delay,
            appleArtifactService: new AppleReleaseArtifactService(getAvailableBytes ?? (_ => long.MaxValue)),
            getHighestAppleBuildNumber: getHighestAppleBuildNumber,
            getAppleVersionInventory: getAppleVersionInventory,
            findAppleApps: findAppleApps,
            notarizeAppleArtifact: notarizeAppleArtifact,
            planAppleGovernance: planAppleGovernance,
            submitAppleReview: submitAppleReview,
            checkAppleReleaseReadiness: checkAppleReleaseReadiness);

    private static AppStoreConnectReleaseStateResult CreateReleaseState(
        AppStoreConnectReleaseStateRequest request,
        string? processingState)
        => new()
        {
            AppId = request.AppId,
            VersionString = request.VersionString,
            BuildNumber = request.BuildNumber,
            Platforms = new[]
            {
                new AppStoreConnectPlatformReleaseState
                {
                    Platform = request.Platforms.Single(),
                    Version = new AppStoreConnectVersionInfo
                    {
                        Id = "version-id",
                        VersionString = request.VersionString,
                        AppStoreState = "PREPARE_FOR_SUBMISSION"
                    },
                    MatchedBuild = processingState is null
                        ? null
                        : new AppStoreConnectBuildInfo
                        {
                            Id = "build-id",
                            Version = request.BuildNumber,
                            ProcessingState = processingState,
                            MarketingVersion = request.VersionString
                        },
                    MatchedBuildSelected = processingState is null ? null : true,
                    BetaDetail = new AppStoreConnectBuildBetaDetailInfo
                    {
                        InternalBuildState = "READY_FOR_BETA_TESTING",
                        ExternalBuildState = "READY_FOR_BETA_SUBMISSION"
                    },
                    NextActions = new[] { "Submit the TestFlight build to Beta App Review." }
                }
            }
        };

    private static AppStoreConnectReleaseReadinessResult CreateReadyReleaseReadiness(
        AppStoreConnectReleaseReadinessRequest request)
        => new()
        {
            AppId = request.AppId,
            VersionString = request.VersionString,
            BuildNumber = request.BuildNumber,
            Platform = request.Platform,
            IsReady = true
        };

    private static AppleAppArchiveResult CreateSuccessfulArchive(AppleAppArchiveRequest request)
        => new()
        {
            ArchivePath = request.ArchivePath!,
            Destination = request.Destination!,
            ProcessResult = new ProcessRunResult(
                0,
                "archive-ok",
                string.Empty,
                "xcodebuild",
                TimeSpan.FromSeconds(1),
                false)
        };

    private static AppleAppArchiveUploadResult CreateSuccessfulUpload(AppleAppArchiveUploadRequest request)
        => new()
        {
            ArchivePath = request.ArchivePath,
            ExportPath = request.ExportPath!,
            ExportOptionsPlistPath = Path.Combine(request.ExportPath!, "ExportOptions.plist"),
            ProcessResult = new ProcessRunResult(
                0,
                "upload-ok",
                string.Empty,
                "xcodebuild",
                TimeSpan.FromSeconds(1),
                false)
        };

    private static AppStoreConnectReleasePreparationResult CreateSuccessfulPreparation(
        AppStoreConnectReleasePreparationRequest request)
        => new()
        {
            AppId = request.AppId,
            VersionString = request.VersionString,
            BuildNumber = request.BuildNumber,
            Platform = request.Platform,
            Version = new AppStoreConnectVersionInfo
            {
                Id = "version-id",
                VersionString = request.VersionString
            },
            Build = new AppStoreConnectBuildInfo
            {
                Id = "build-id",
                Version = request.BuildNumber,
                ProcessingState = "VALID",
                MarketingVersion = request.VersionString
            },
            SelectedBuild = true
        };

    [Fact]
    public void Execute_ApplePlan_RejectsChangedMetadataPayloadBeforeMutation()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var metadataPath = Path.Combine(root, "metadata.json");
            File.WriteAllText(metadataPath, "{ \"payload\": \"approved\" }");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.SyncMetadata = true;
            spec.AppleApps.MetadataConfigPath = "metadata.json";
            var mutationCalls = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                prepareAppleDistribution: request =>
                {
                    mutationCalls++;
                    return CreateSuccessfulPreparation(request);
                });
            var plan = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.Configured
                });
            File.WriteAllText(metadataPath, "{ \"payload\": \"changed\" }");

            var execution = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Configured,
                    AppleExpectedPlanSha256 = plan.AppleReceipt!.PlanSha256
                });

            Assert.False(execution.Success);
            Assert.Equal(0, mutationCalls);
            Assert.Contains("changed after plan approval", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePlan_BindsScreenshotPixelBytes()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var screenshotFolder = Directory.CreateDirectory(Path.Combine(root, "screenshots"));
            var screenshotPath = Path.Combine(screenshotFolder.FullName, "home.png");
            File.WriteAllText(screenshotPath, "approved pixels");
            WriteScreenshotConfig(root, "screenshots.json", "6778025328", "1.2.0", "iOS", "screenshots", qualityEnabled: false);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.SyncScreenshots = true;
            spec.AppleApps.ScreenshotConfigPath = "screenshots.json";
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                checkAppleReleaseReadiness: (_, request) => new AppStoreConnectReleaseReadinessResult
                {
                    AppId = request.AppId,
                    VersionString = request.VersionString,
                    BuildNumber = request.BuildNumber,
                    Platform = request.Platform,
                    IsReady = true
                });

            var approved = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.Screenshots
                });
            File.WriteAllText(screenshotPath, "different pixels");
            var changed = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.Screenshots
                });

            Assert.NotEqual(approved.AppleReceipt!.PlanSha256, changed.AppleReceipt!.PlanSha256);
            Assert.Contains("screenshots/home.png", approved.AppleReceipt.MutationInputFiles.Keys);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void WriteScreenshotConfig(
        string root,
        string fileName,
        string appId,
        string version,
        string platform,
        string folder,
        bool qualityEnabled)
        => File.WriteAllText(
            Path.Combine(root, fileName),
            $$"""
            {
              "appId": "{{appId}}",
              "versionString": "{{version}}",
              "platform": "{{platform}}",
              "locale": "en-US",
              "quality": {
                "enabled": {{qualityEnabled.ToString().ToLowerInvariant()}},
                "rejectDuplicates": true,
                "requireConsistentDimensions": true,
                "minimumFileBytes": 0,
                "minimumKilobytesPerMegapixel": 0
              },
              "screenshotSets": [
                {
                  "screenshotDisplayType": "{{(platform == "macOS" ? "APP_DESKTOP" : "APP_IPHONE_65")}}",
                  "path": "{{folder}}",
                  "filter": "*.png"
                }
              ]
            }
            """);
}
