namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_ConfiguredAppleUpload_WaitsForAfterStagingValidation(bool validationSucceeds)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var validationPath = Path.Combine(root, "validate.ps1");
            File.WriteAllText(validationPath, "exit 0");
            var configPath = Path.Combine(root, "powerforge.release.json");
            File.WriteAllText(configPath, "{}");
            var calls = new List<string>();
            var uploaded = false;
            var progress = new AppleRehearsalProgress();
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Archive = true;
            spec.AppleApps.Upload = true;
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.Validation = new PowerForgeReleaseValidationOptions
            {
                AfterStaging =
                [
                    new PowerForgeReleaseValidationAction
                    {
                        Name = "complete release",
                        FilePath = validationPath
                    }
                ]
            };

            var service = CreateAppleAutomationService(
                request =>
                {
                    calls.Add("state");
                    return CreateReleaseState(request, uploaded ? "VALID" : null);
                },
                archiveAppleApp: request =>
                {
                    calls.Add("archive");
                    return CreateSuccessfulArchive(request);
                },
                uploadAppleApp: request =>
                {
                    calls.Add("upload");
                    uploaded = true;
                    return CreateSuccessfulUpload(request);
                },
                runReleaseValidation: (_, _, _, _) =>
                {
                    calls.Add("validation");
                    return new PowerForgeReleaseValidationResult
                    {
                        Name = "complete release",
                        Succeeded = validationSucceeds,
                        ExitCode = validationSucceeds ? 0 : 1,
                        StdErr = validationSucceeds ? string.Empty : "validation rejected the staged release"
                    };
                });
            var plan = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = configPath,
                    StageRoot = Path.Combine(root, "stage"),
                    PlanOnly = true
                });
            Assert.True(plan.Success, plan.ErrorMessage);
            var expectedPlanSha256 = Assert.IsType<string>(plan.AppleReceipt?.PlanSha256);
            calls.Clear();

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = configPath,
                    StageRoot = Path.Combine(root, "stage"),
                    AppleExpectedPlanSha256 = expectedPlanSha256,
                    Progress = progress
                });

            Assert.True(result.Success == validationSucceeds, result.ErrorMessage);
            Assert.True(calls.IndexOf("archive") < calls.IndexOf("validation"));
            if (validationSucceeds)
            {
                Assert.True(calls.IndexOf("validation") < calls.IndexOf("upload"));
                Assert.Equal(1, calls.Count(call => call == "archive"));
                Assert.Equal(1, calls.Count(call => call == "upload"));
                Assert.False(Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan).Archive);
                var appleResult = Assert.Single(result.AppleApps);
                Assert.NotNull(appleResult.Archive);
                Assert.NotNull(appleResult.ArchiveSha256);
                var target = Assert.Single(Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt).Targets);
                Assert.True(target.ArchiveCreated);
                Assert.NotNull(target.ArchivePath);
                Assert.NotNull(target.ArchiveSha256);
                Assert.Equal(1, progress.Events.Count(entry => entry == "phase:start:AppleApps"));
                Assert.Equal(1, progress.Events.Count(entry => entry == "phase:complete:AppleApps"));
                Assert.DoesNotContain("phase:fail:AppleApps", progress.Events);
            }
            else
            {
                Assert.DoesNotContain("upload", calls);
                Assert.Contains("validation rejected", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, progress.Events.Count(entry => entry == "phase:start:AppleApps"));
                Assert.Equal(1, progress.Events.Count(entry => entry == "phase:fail:AppleApps"));
                Assert.DoesNotContain("phase:complete:AppleApps", progress.Events);
                Assert.False(File.Exists(Path.Combine(root, "build", "powerforge", "apple", "release-receipt.json")));
                var receiptHistoryPath = Path.Combine(root, "build", "powerforge", "apple", "receipts");
                Assert.False(Directory.Exists(receiptHistoryPath));
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ConfiguredAppleMetadata_PreservesArchiveEvidenceAfterValidation()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(
                Path.Combine(root, "metadata.json"),
                """
                {
                  "appId": "6778025328",
                  "versionString": "1.2.0",
                  "platform": "iOS",
                  "locale": "en-US",
                  "metadata": {}
                }
                """);
            var validationPath = Path.Combine(root, "validate.ps1");
            File.WriteAllText(validationPath, "exit 0");
            var configPath = Path.Combine(root, "powerforge.release.json");
            File.WriteAllText(configPath, "{}");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Archive = true;
            spec.AppleApps.Upload = false;
            spec.AppleApps.SyncMetadata = true;
            spec.AppleApps.MetadataConfigPath = "metadata.json";
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.Validation = new PowerForgeReleaseValidationOptions
            {
                AfterStaging =
                [
                    new PowerForgeReleaseValidationAction
                    {
                        Name = "complete release",
                        FilePath = validationPath
                    }
                ]
            };
            var metadataCalls = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                archiveAppleApp: CreateSuccessfulArchive,
                prepareAppleDistribution: request =>
                {
                    metadataCalls++;
                    return CreateSuccessfulPreparation(request);
                },
                runReleaseValidation: (_, _, _, _) => new PowerForgeReleaseValidationResult
                {
                    Name = "complete release",
                    Succeeded = true,
                    ExitCode = 0
                });

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = configPath,
                    StageRoot = Path.Combine(root, "stage")
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, metadataCalls);
            var appleResult = Assert.Single(result.AppleApps);
            Assert.NotNull(appleResult.Archive);
            Assert.NotNull(appleResult.ArchiveSha256);
            var target = Assert.Single(Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt).Targets);
            Assert.True(target.ArchiveCreated);
            Assert.NotNull(target.ArchivePath);
            Assert.NotNull(target.ArchiveSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ConfiguredAppleScreenshotReplacement_RejectsRemoteChangeDuringArchiveCheckpoint()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var screenshotFolder = Directory.CreateDirectory(Path.Combine(root, "screenshots"));
            File.WriteAllText(Path.Combine(screenshotFolder.FullName, "home.png"), "approved pixels");
            WriteScreenshotConfig(
                root,
                "screenshots.json",
                "6778025328",
                "1.2.0",
                "iOS",
                "screenshots",
                qualityEnabled: false);
            var validationPath = Path.Combine(root, "validate.ps1");
            File.WriteAllText(validationPath, "exit 0");
            var configPath = Path.Combine(root, "powerforge.release.json");
            File.WriteAllText(configPath, "{}");
            var remoteScreenshotId = "screenshot-before";
            var readinessCalls = 0;
            var mutationCalls = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                archiveAppleApp: request =>
                {
                    var archive = CreateSuccessfulArchive(request);
                    remoteScreenshotId = "screenshot-after";
                    return archive;
                },
                prepareAppleDistribution: request =>
                {
                    mutationCalls++;
                    return CreateSuccessfulPreparation(request);
                },
                checkAppleReleaseReadiness: (_, request) =>
                {
                    readinessCalls++;
                    return new AppStoreConnectReleaseReadinessResult
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
                    };
                },
                runReleaseValidation: (_, _, _, _) => new PowerForgeReleaseValidationResult
                {
                    Name = "complete release",
                    Succeeded = true,
                    ExitCode = 0
                });
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Archive = true;
            spec.AppleApps.Upload = false;
            spec.AppleApps.SyncScreenshots = true;
            spec.AppleApps.ReplaceScreenshots = true;
            spec.AppleApps.ScreenshotConfigPath = "screenshots.json";
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.Validation = new PowerForgeReleaseValidationOptions
            {
                AfterStaging =
                [
                    new PowerForgeReleaseValidationAction
                    {
                        Name = "complete release",
                        FilePath = validationPath
                    }
                ]
            };
            var plan = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = configPath,
                    StageRoot = Path.Combine(root, "stage"),
                    PlanOnly = true
                });
            Assert.True(plan.Success, plan.ErrorMessage);

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = configPath,
                    StageRoot = Path.Combine(root, "stage"),
                    AppleExpectedPlanSha256 = plan.AppleReceipt!.PlanSha256,
                    AppleActionConfirmed = true
                });

            Assert.False(result.Success);
            Assert.Equal(3, readinessCalls);
            Assert.Equal(0, mutationCalls);
            Assert.Contains("changed after plan approval", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
