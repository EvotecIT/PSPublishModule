namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_AppleCheckpoint_writes_publish_shaped_plan_after_archive()
    {
        string? sourceCommit = null;
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
            AppleAppArchiveRequest? archiveRequest = null;
            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Archive checkpoint must not query App Store Connect."),
                    archiveAppleApp: request =>
                    {
                        archiveRequest = request;
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "payload"), "signed archive");
                        return CreateSuccessfulArchive(request);
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    CheckpointAppleApps = true,
                    AppleSourceCommit = sourceCommit ??= EnsureTestSourceCommit(root)
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains(
                $"POWERFORGE_SOURCE_REVISION={sourceCommit}",
                Assert.IsType<AppleAppArchiveRequest>(archiveRequest)
                    .AdditionalArguments);
            Assert.False(result.AppleAppPlan!.Archive);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            Assert.True(receipt.PlanOnly);
            Assert.Equal(sourceCommit, receipt.SourceCommit);
            Assert.Matches("^[0-9A-Fa-f]{64}$", receipt.PlanSha256);
            var target = Assert.Single(receipt.Targets);
            Assert.EndsWith("/apple/archives/iOS/CasaRay-iOS.xcarchive", target.ArchivePath, StringComparison.Ordinal);
            Assert.Matches("^[0-9A-Fa-f]{64}$", target.ArchiveSha256);
            Assert.Equal(target.ArchiveSha256, Assert.Single(result.AppleAppPlan.Apps).ExpectedArchiveSha256);
            Assert.True(File.Exists(result.AppleAppPlan.PlanReceiptPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_ExecutesConfiguredAppleLane()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var archiveCalls = 0;
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Configured archive should not query App Store Connect."),
                archiveAppleApp: request =>
                {
                    archiveCalls++;
                    return CreateSuccessfulArchive(request);
                });

            var result = service.PublishBuiltReleaseOutputs(
                CreateAppleAutomationSpec(root, keyPath),
                new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleActionConfirmed = true
                },
                new PowerForgeReleaseResult { Success = true });

            Assert.True(result.Success);
            Assert.Equal(1, archiveCalls);
            Assert.True(Assert.Single(result.AppleApps).Success);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_rejects_replaced_checkpoint_archive_before_upload()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Archive = false;
            spec.AppleApps.Upload = true;
            spec.AppleApps.Automation.Resume = false;
            var archivePath = Path.Combine(
                root,
                "build",
                "powerforge",
                "apple",
                "archives",
                "iOS",
                "CasaRay-iOS.xcarchive");
            Directory.CreateDirectory(archivePath);
            File.WriteAllText(Path.Combine(archivePath, "payload"), "replacement bytes");
            var uploadCalls = 0;
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Archive hash rejection must happen before remote state."),
                uploadAppleApp: request =>
                {
                    uploadCalls++;
                    return CreateSuccessfulUpload(request);
                });

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleActionConfirmed = true,
                    AppleExpectedArchiveSha256ByTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["CasaRay iOS"] = new string('1', 64)
                    }
                },
                new PowerForgeReleaseResult { Success = true });

            Assert.False(result.Success);
            Assert.Equal(0, uploadCalls);
            Assert.Contains("changed before publish", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUploadExisting_PreflightsEveryArchiveBeforeFirstUpload()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateTwoTargetAppleSpec(root, keyPath);
            spec.AppleApps!.Automation.Resume = false;
            Directory.CreateDirectory(
                Path.Combine(root, "build", "powerforge", "apple", "archives", "iOS", "First.xcarchive"));
            var uploadCalls = 0;
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Resume is disabled; state should not be queried."),
                uploadAppleApp: request =>
                {
                    uploadCalls++;
                    return CreateSuccessfulUpload(request);
                });

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.UploadExisting
                });

            Assert.False(result.Success);
            Assert.Equal(0, uploadCalls);
            Assert.Equal(2, result.AppleApps.Length);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            Assert.False(receipt.Success);
            Assert.Collection(
                receipt.Targets,
                first =>
                {
                    Assert.Equal("First", first.Name);
                    Assert.Contains("remoteActions", first.SkippedSteps);
                },
                second =>
                {
                    Assert.Equal("Second", second.Name);
                    Assert.Contains("remoteActions", second.SkippedSteps);
                    Assert.Contains(
                        "archive was not found",
                        Assert.IsType<string>(second.ErrorMessage),
                        StringComparison.OrdinalIgnoreCase);
                });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleArchiveFailure_MarksLaterTargetsNotAttempted()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var archiveCalls = 0;
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Archive action should not query App Store Connect."),
                archiveAppleApp: request =>
                {
                    archiveCalls++;
                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(
                            65,
                            string.Empty,
                            "archive failed",
                            "xcodebuild",
                            TimeSpan.FromSeconds(1),
                            false)
                    };
                });

            var result = service.Execute(
                CreateTwoTargetAppleSpec(root, keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Archive
                });

            Assert.False(result.Success);
            Assert.Equal(1, archiveCalls);
            Assert.Equal(2, result.AppleApps.Length);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            Assert.Collection(
                receipt.Targets,
                first =>
                {
                    Assert.Equal("First", first.Name);
                    Assert.Contains(
                        "exit code 65",
                        Assert.IsType<string>(first.ErrorMessage),
                        StringComparison.OrdinalIgnoreCase);
                },
                second =>
                {
                    Assert.Equal("Second", second.Name);
                    Assert.Contains("notAttempted", second.SkippedSteps);
                    Assert.Null(second.ErrorMessage);
                });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleArchive_WorkspaceWithoutReleaseIdentityWritesSuccessfulReceipt()
    {
        var root = CreateSandbox();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "CasaRay.xcworkspace"));
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = Assert.Single(spec.AppleApps!.Apps);
            app.ProjectPath = "CasaRay.xcworkspace";
            app.MarketingVersion = null;
            app.BuildNumber = null;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Archive action should not query App Store Connect."),
                    archiveAppleApp: CreateSuccessfulArchive)
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Archive
                    });

            Assert.True(result.Success);
            var target = Assert.Single(Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt).Targets);
            Assert.True(target.ArchiveCreated);
            Assert.Null(target.Version);
            Assert.Null(target.Build);
            Assert.Null(target.ErrorMessage);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleArchive_SkippedEmbeddedWorkspaceDoesNotRequireReleaseIdentity()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            Directory.CreateDirectory(Path.Combine(root, "CasaRayWatch.xcworkspace"));
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var parent = Assert.Single(spec.AppleApps!.Apps);
            spec.AppleApps.Apps = new[]
            {
                parent,
                new AppleAppConfiguration
                {
                    Name = "CasaRay Watch",
                    BundleId = "com.evotecit.casaray.watchkitapp",
                    Platform = ApplePlatform.watchOS,
                    ProjectPath = "CasaRayWatch.xcworkspace",
                    Scheme = "CasaRayWatch",
                    DistributionRoute = AppleDistributionRoute.EmbeddedCompanion,
                    ProductRole = AppleProductRole.CompanionApp,
                    ParentTarget = parent.Name
                }
            };

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Archive action should not query App Store Connect."),
                    archiveAppleApp: CreateSuccessfulArchive)
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Archive
                    });

            Assert.True(result.Success);
            Assert.Collection(
                Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt).Targets,
                primary =>
                {
                    Assert.Equal(parent.Name, primary.Name);
                    Assert.True(primary.ArchiveCreated);
                    Assert.Equal("1.2.0", primary.Version);
                    Assert.Equal("9", primary.Build);
                },
                companion =>
                {
                    Assert.Equal("CasaRay Watch", companion.Name);
                    Assert.Null(companion.Version);
                    Assert.Null(companion.Build);
                    Assert.Null(companion.ErrorMessage);
                    Assert.Contains("independentRelease", companion.SkippedSteps);
                });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleArchive_ProjectIdentityIsRetainedInReceipt()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Archive action should not query App Store Connect."),
                    archiveAppleApp: CreateSuccessfulArchive)
                .Execute(
                    CreateAppleAutomationSpec(root, keyPath),
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Archive
                    });

            Assert.True(result.Success);
            var target = Assert.Single(Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt).Targets);
            Assert.True(target.ArchiveCreated);
            Assert.Equal("1.2.0", target.Version);
            Assert.Equal("9", target.Build);
            Assert.Null(target.ErrorMessage);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static PowerForgeReleaseSpec CreateTwoTargetAppleSpec(string root, string keyPath)
    {
        var spec = CreateAppleAutomationSpec(root, keyPath);
        spec.AppleApps!.Apps = new[]
        {
            new AppleAppConfiguration
            {
                Name = "First",
                BundleId = "com.evotecit.casaray.first",
                Platform = ApplePlatform.iOS,
                ProjectPath = "CasaRay.xcodeproj",
                Scheme = "CasaRay",
                AppStoreConnectAppId = "6778025328"
            },
            new AppleAppConfiguration
            {
                Name = "Second",
                BundleId = "com.evotecit.casaray.second",
                Platform = ApplePlatform.iOS,
                ProjectPath = "CasaRay.xcodeproj",
                Scheme = "CasaRay",
                AppStoreConnectAppId = "6778025329"
            }
        };
        return spec;
    }
}
