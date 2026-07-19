namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_AppleScreenshots_RejectsNonmatchingConfiguredSpecBeforeMutation()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(
                Path.Combine(root, "screenshots.json"),
                """
                {
                  "appId": "6778025328",
                  "versionString": "9.9.9",
                  "platform": "iOS",
                  "locale": "en-US",
                  "screenshotSets": []
                }
                """);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.ScreenshotConfigPath = "screenshots.json";
            var prepareCalls = 0;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("A config mismatch must not query final status."),
                    prepareAppleDistribution: _ =>
                    {
                        prepareCalls++;
                        throw new InvalidOperationException("Screenshot mutation must not run.");
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Screenshots
                    });

            Assert.False(result.Success);
            Assert.Equal(0, prepareCalls);
            Assert.Contains(
                "No screenshot sync config matches",
                Assert.IsType<string>(Assert.Single(result.AppleReceipt!.Targets).ErrorMessage),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePrepare_RejectsNonmatchingConfiguredMetadataBeforeMutation()
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
                  "appId": "wrong-app",
                  "versionString": "1.2.0",
                  "platform": "iOS",
                  "locale": "en-US",
                  "metadata": {}
                }
                """);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.MetadataConfigPath = "metadata.json";
            var prepareCalls = 0;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("A config mismatch must not query final status."),
                    prepareAppleDistribution: _ =>
                    {
                        prepareCalls++;
                        throw new InvalidOperationException("Metadata mutation must not run.");
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Prepare
                    });

            Assert.False(result.Success);
            Assert.Equal(0, prepareCalls);
            Assert.Contains(
                "No App Store metadata config matches",
                Assert.IsType<string>(Assert.Single(result.AppleReceipt!.Targets).ErrorMessage),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("locale")]
    [InlineData("metadata")]
    public void Execute_ApplePrepare_PreflightsEveryMetadataTargetBeforeFirstRemoteMutation(
        string failureKind)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            CreateXcodeProject(root, "CasaRayMac.xcodeproj", "1.2.0", "9");
            File.WriteAllText(Path.Combine(root, "project.yml"), "name: CasaRay");
            var thirdRoot = Directory.CreateDirectory(Path.Combine(root, "Third"));
            File.WriteAllText(Path.Combine(thirdRoot.FullName, "project.yml"), "name: Third");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(
                Path.Combine(root, "metadata-ios.json"),
                """
                {
                  "appId": "6778025328",
                  "versionString": "1.2.0",
                  "platform": "iOS",
                  "locale": "en-US",
                  "metadata": {}
                }
                """);
            var macVersion = failureKind == "mapping" ? "9.9.9" : "1.2.0";
            var macLocale = failureKind == "locale" ? string.Empty : "en-US";
            var macMetadata = failureKind == "metadata" ? "null" : "{}";
            File.WriteAllText(
                Path.Combine(root, "metadata-mac.json"),
                $$"""
                {
                  "appId": "6778025328",
                  "versionString": "{{macVersion}}",
                  "platform": "macOS",
                  "locale": "{{macLocale}}",
                  "metadata": {{macMetadata}}
                }
                """);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.MetadataConfigPaths = new[]
            {
                "metadata-ios.json",
                "metadata-mac.json"
            };
            var iosApp = Assert.Single(spec.AppleApps.Apps);
            iosApp.RegenerateProject = true;
            spec.AppleApps.Apps = new[]
            {
                iosApp,
                new AppleAppConfiguration
                {
                    Name = "CasaRay Mac",
                    BundleId = "com.evotecit.casaray",
                    Platform = ApplePlatform.macOS,
                    ProjectPath = "CasaRayMac.xcodeproj",
                    Scheme = "CasaRayMac",
                    AppStoreConnectAppId = "6778025328"
                },
                new AppleAppConfiguration
                {
                    Name = "CasaRay Third",
                    BundleId = "com.evotecit.casaray.third",
                    Platform = ApplePlatform.iOS,
                    ProjectPath = "Third/Third.xcodeproj",
                    Scheme = "Third",
                    AppStoreConnectAppId = "6778025328",
                    GenerateProjectIfMissing = true
                }
            };
            var prepareCalls = 0;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Preflight failure must not query final status."),
                    prepareAppleDistribution: _ =>
                    {
                        prepareCalls++;
                        throw new InvalidOperationException("No remote mutation may start before all metadata preflights pass.");
                    },
                    generateAppleProject: plan => plan.RegenerateProject)
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Prepare
                    });

            Assert.False(result.Success);
            Assert.Equal(0, prepareCalls);
            var targets = result.AppleReceipt!.Targets;
            Assert.Equal(3, targets.Length);
            Assert.True(targets[0].ProjectGenerated);
            Assert.Contains("remoteActions", targets[0].SkippedSteps);
            Assert.Contains("remoteActions", targets[1].SkippedSteps);
            Assert.Contains("preflight", targets[2].SkippedSteps);
            Assert.Contains("remoteActions", targets[2].SkippedSteps);
            Assert.False(targets[2].ProjectGenerated);
            Assert.Null(targets[2].Version);
            Assert.Null(targets[2].Build);
            Assert.Contains(
                failureKind switch
                {
                    "mapping" => "No App Store metadata config matches",
                    "locale" => "must declare Locale",
                    _ => "must declare a Metadata object"
                },
                Assert.IsType<string>(result.AppleReceipt.ErrorMessage),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_AppleScreenshots_PreflightsEveryTargetBeforeFirstRemoteMutation(
        bool failQuality)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            CreateXcodeProject(root, "CasaRayMac.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII=");
            var iosFolder = Directory.CreateDirectory(Path.Combine(root, "ios-shots"));
            File.WriteAllBytes(Path.Combine(iosFolder.FullName, "01.png"), png);
            var macFolder = Directory.CreateDirectory(Path.Combine(root, "mac-shots"));
            File.WriteAllBytes(Path.Combine(macFolder.FullName, "01.png"), png);
            if (failQuality)
                File.WriteAllBytes(Path.Combine(macFolder.FullName, "02.png"), png);
            WriteScreenshotConfig(
                root,
                "screenshots-ios.json",
                "6778025328",
                "1.2.0",
                "iOS",
                "ios-shots",
                qualityEnabled: true);
            WriteScreenshotConfig(
                root,
                "screenshots-mac.json",
                "6778025328",
                failQuality ? "1.2.0" : "9.9.9",
                "macOS",
                "mac-shots",
                qualityEnabled: true);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.ScreenshotConfigPath = null;
            spec.AppleApps.ScreenshotConfigPaths = new[]
            {
                "screenshots-ios.json",
                "screenshots-mac.json"
            };
            spec.AppleApps.Apps = new[]
            {
                Assert.Single(spec.AppleApps.Apps),
                new AppleAppConfiguration
                {
                    Name = "CasaRay Mac",
                    BundleId = "com.evotecit.casaray",
                    Platform = ApplePlatform.macOS,
                    ProjectPath = "CasaRayMac.xcodeproj",
                    Scheme = "CasaRayMac",
                    AppStoreConnectAppId = "6778025328"
                }
            };
            var prepareCalls = 0;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Preflight failure must not query final status."),
                    prepareAppleDistribution: _ =>
                    {
                        prepareCalls++;
                        throw new InvalidOperationException("No remote mutation may start before all target preflights pass.");
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Screenshots
                    });

            Assert.False(result.Success);
            Assert.Equal(0, prepareCalls);
            Assert.Contains(
                failQuality ? "duplicate" : "No screenshot sync config matches",
                Assert.IsType<string>(result.AppleReceipt!.ErrorMessage),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleReceipt_RejectsParentSymlinkEscape()
    {
        var root = CreateSandbox();
        var outside = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var buildLink = Path.Combine(root, "build");
            try
            {
                Directory.CreateSymbolicLink(buildLink, outside);
            }
            catch (Exception linkCreationException) when (
                linkCreationException is PlatformNotSupportedException ||
                linkCreationException is UnauthorizedAccessException ||
                linkCreationException is IOException)
            {
                return;
            }

            var exception = Assert.Throws<InvalidOperationException>(() =>
                CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"))
                    .Execute(
                        CreateAppleAutomationSpec(root, keyPath),
                        new PowerForgeReleaseRequest
                        {
                            ConfigPath = Path.Combine(root, "powerforge.release.json"),
                            AppleAction = PowerForgeAppleReleaseAction.Status
                        }));

            Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(outside, "powerforge", "apple", "release-receipt.json")));
        }
        finally
        {
            var buildLink = Path.Combine(root, "build");
            if (Directory.Exists(buildLink))
                Directory.Delete(buildLink);
            TryDelete(root);
            TryDelete(outside);
        }
    }

    [Fact]
    public void Execute_AppleUpload_CleanupFailureStillWritesValidBuildReceipt()
    {
        var root = CreateSandbox();
        var outside = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var archiveLink = Path.Combine(root, "build", "powerforge", "apple", "archives");
            Directory.CreateDirectory(Path.GetDirectoryName(archiveLink)!);
            try
            {
                Directory.CreateSymbolicLink(archiveLink, outside);
            }
            catch (Exception linkCreationException) when (
                linkCreationException is PlatformNotSupportedException ||
                linkCreationException is UnauthorizedAccessException ||
                linkCreationException is IOException)
            {
                return;
            }
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.CleanupAfterProcessing = true;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"));
            var planned = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    PlanOnly = true
                });
            Assert.True(planned.AppleAppPlan!.Automation.CleanupAfterProcessing);
            var archivePath = Assert.Single(planned.AppleAppPlan.Apps).ArchivePath;
            Directory.CreateDirectory(
                Path.Combine(outside, Path.GetRelativePath(archiveLink, archivePath)));
            Assert.True(Directory.Exists(archivePath), archivePath);
            Assert.True(
                (File.GetAttributes(archiveLink) & FileAttributes.ReparsePoint) != 0,
                archiveLink);

            var result = service
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload
                    });

            Assert.False(result.Success, System.Text.Json.JsonSerializer.Serialize(result.AppleReceipt));
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            var target = Assert.Single(receipt.Targets);
            Assert.Equal("VALID", target.BuildProcessingState);
            Assert.Contains("cleanup failed", Assert.IsType<string>(target.ErrorMessage), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, receipt.ReceiptPath!)));
        }
        finally
        {
            var archiveLink = Path.Combine(root, "build", "powerforge", "apple", "archives");
            if (Directory.Exists(archiveLink))
                Directory.Delete(archiveLink);
            TryDelete(root);
            TryDelete(outside);
        }
    }

    [Fact]
    public void Execute_AppleCleanup_DoesNotRequireOrRegenerateTheXcodeProject()
    {
        var root = CreateSandbox();
        try
        {
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = Assert.Single(spec.AppleApps!.Apps);
            app.ProjectPath = "Missing.xcodeproj";
            app.RegenerateProject = true;
            app.MarketingVersion = "2.0.0";
            app.BuildNumberPolicy = AppleBuildNumberPolicy.IncrementExisting;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Cleanup must not query App Store Connect."),
                    generateAppleProject: _ => throw new InvalidOperationException("Cleanup must not invoke XcodeGen."))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Cleanup
                    });

            Assert.True(result.Success);
            Assert.True(result.AppleReceipt!.Success);
            var target = Assert.Single(result.AppleReceipt.Targets);
            Assert.False(target.ProjectGenerated);
            Assert.Null(target.Version);
            Assert.Null(target.Build);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleCleanup_IgnoresVersionPolicyForExistingWorkspace()
    {
        var root = CreateSandbox();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Existing.xcworkspace"));
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = Assert.Single(spec.AppleApps!.Apps);
            app.ProjectPath = "Existing.xcworkspace";
            app.MarketingVersion = "2.0.0";
            app.BuildNumber = "10";
            app.BuildNumberPolicy = AppleBuildNumberPolicy.IncrementExisting;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Cleanup must not query App Store Connect."),
                    generateAppleProject: _ => throw new InvalidOperationException("Cleanup must not invoke XcodeGen."))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Cleanup
                    });

            Assert.True(result.Success);
            Assert.True(result.AppleReceipt!.Success);
            var target = Assert.Single(result.AppleReceipt.Targets);
            Assert.Null(target.Version);
            Assert.Null(target.Build);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleCleanup_WritesOnlyProjectRelativeRemovedPaths()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.ArtifactRetentionDays = 7;
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Cleanup must not query App Store Connect."));
            var planResult = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Cleanup,
                    PlanOnly = true
                });
            var archiveRoot = Path.GetDirectoryName(Assert.Single(planResult.AppleAppPlan!.Apps).ArchivePath)!;
            var stale = Path.Combine(archiveRoot, "stale.xcarchive");
            Directory.CreateDirectory(stale);
            File.WriteAllText(Path.Combine(stale, "old.bin"), "old");
            Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-10));

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Cleanup
                });

            Assert.True(result.Success);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            Assert.True(receipt.Success);
            var removed = Assert.Single(receipt.Cleanup.RemovedPaths);
            Assert.False(Path.IsPathRooted(removed));
            Assert.DoesNotContain(root, removed, StringComparison.Ordinal);
            var json = File.ReadAllText(Path.Combine(root, receipt.ReceiptPath!));
            Assert.DoesNotContain(root, json, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

}
