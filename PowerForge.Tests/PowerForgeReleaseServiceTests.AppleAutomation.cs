using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
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
                    AppleAction = PowerForgeAppleReleaseAction.Status
                });

            Assert.True(result.Success);
            var plan = Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan);
            Assert.Equal(PowerForgeAppleReleaseAction.Status, plan.Action);
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
    public void Execute_ApplePreparePlan_EnablesConfiguredDistributionInputs()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(Path.Combine(root, "metadata.json"), "{}");
            File.WriteAllText(Path.Combine(root, "screenshots.json"), "{}");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.MetadataConfigPath = "metadata.json";
            spec.AppleApps.ScreenshotConfigPath = "screenshots.json";

            var result = new PowerForgeReleaseService(new NullLogger()).Execute(
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

    [Fact]
    public void Execute_AppleScreenshotReplacementPlan_IsIsolatedAndRequiresConfirmation()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(Path.Combine(root, "screenshots.json"), "{}");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.ScreenshotConfigPath = "screenshots.json";
            spec.AppleApps.ReplaceScreenshots = true;

            var result = new PowerForgeReleaseService(new NullLogger()).Execute(
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

    [Fact]
    public void Execute_AppleArchivePlan_AcceptsMissingXcodeGenProject()
    {
        var root = CreateSandbox();
        try
        {
            var projectDirectory = Path.Combine(root, "Generated");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(projectDirectory, "project.yml"), "name: Generated");
            var spec = new PowerForgeReleaseSpec
            {
                AppleApps = new PowerForgeAppleReleaseOptions
                {
                    ProjectRoot = root,
                    Automation = new PowerForgeAppleReleaseAutomationOptions(),
                    Apps = new[]
                    {
                        new AppleAppConfiguration
                        {
                            Name = "Generated iOS",
                            ProjectPath = "Generated/Generated.xcodeproj",
                            Scheme = "Generated",
                            GenerateProjectIfMissing = true
                        }
                    }
                }
            };

            var result = new PowerForgeReleaseService(new NullLogger()).Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.Archive
                });

            Assert.True(result.Success);
            Assert.True(Assert.Single(result.AppleAppPlan!.Apps).GenerateProjectIfMissing);
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
    public void Execute_AppleUploadAction_ResumesExactValidRemoteBuildAndWritesCompactReceipt(
        PowerForgeAppleReleaseAction action)
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
                    return CreateReleaseState(request, "VALID");
                },
                getAvailableBytes: _ => throw new InvalidOperationException("Resumed builds must skip archive preflight."));
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.CleanupAfterProcessing = false;

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = action
                });

            Assert.True(result.Success);
            Assert.Equal(1, stateCalls);
            var app = Assert.Single(result.AppleApps);
            Assert.True(app.ResumedExistingBuild);
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
            var states = new Queue<string>(new[] { "PROCESSING", "VALID" });
            var delays = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, states.Dequeue()),
                _ => delays++);
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
        Func<AppleAppArchiveRequest, AppleAppArchiveResult>? archiveAppleApp = null)
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
            uploadAppleApp: _ => throw new InvalidOperationException("Exact remote build should skip upload."),
            getAppleReleaseState: getState,
            delay: delay,
            appleArtifactService: new AppleReleaseArtifactService(getAvailableBytes ?? (_ => long.MaxValue)));

    private static AppStoreConnectReleaseStateResult CreateReleaseState(
        AppStoreConnectReleaseStateRequest request,
        string processingState)
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
                    MatchedBuild = new AppStoreConnectBuildInfo
                    {
                        Id = "build-id",
                        Version = request.BuildNumber,
                        ProcessingState = processingState,
                        MarketingVersion = request.VersionString
                    },
                    MatchedBuildSelected = true,
                    BetaDetail = new AppStoreConnectBuildBetaDetailInfo
                    {
                        InternalBuildState = "READY_FOR_BETA_TESTING",
                        ExternalBuildState = "READY_FOR_BETA_SUBMISSION"
                    },
                    NextActions = new[] { "Submit the TestFlight build to Beta App Review." }
                }
            }
        };
}
