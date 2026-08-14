namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_ApplePlan_BindsEffectiveAutomationPolicy()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Upload planning without adoption must not query App Store Connect."));

            var resumable = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleResume = true,
                    PlanOnly = true
                });
            var nonResumable = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleResume = false,
                    PlanOnly = true
                });

            Assert.NotEqual(resumable.AppleReceipt!.MutationInputsSha256, nonResumable.AppleReceipt!.MutationInputsSha256);
            Assert.NotEqual(resumable.AppleReceipt.PlanSha256, nonResumable.AppleReceipt.PlanSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePlan_BindsEffectiveBuildConfiguration()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Planning must not query App Store Connect."));
            spec.AppleApps!.Configuration = "Release";
            var release = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Upload,
                PlanOnly = true
            });
            spec.AppleApps.Configuration = "Debug";
            var debug = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Upload,
                PlanOnly = true
            });

            Assert.NotEqual(release.AppleReceipt!.MutationInputsSha256, debug.AppleReceipt!.MutationInputsSha256);
            Assert.NotEqual(release.AppleReceipt.PlanSha256, debug.AppleReceipt.PlanSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePlan_BindsEffectiveXcodeTargetSelectors()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Planning must not query App Store Connect."));
            var approved = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Archive,
                PlanOnly = true
            });
            Assert.Single(spec.AppleApps!.Apps).Scheme = "CasaRay-Alternate";
            var changed = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Archive,
                PlanOnly = true
            });

            Assert.NotEqual(approved.AppleReceipt!.PlanSha256, changed.AppleReceipt!.PlanSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ApprovedAppleMutationConfig_UsesCapturedBytesAfterSourceReplacement()
    {
        var root = CreateSandbox();
        try
        {
            var metadataPath = Path.Combine(root, "metadata.json");
            File.WriteAllText(metadataPath, "{ \"value\": \"approved\" }");
            var approvedHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(metadataPath)))
                .ToLowerInvariant();
            var plan = new PowerForgeAppleReleasePlan
            {
                ProjectRoot = root,
                SyncMetadata = true,
                MetadataConfigPath = metadataPath,
                ApprovedMutationInputFilesSha256 = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["metadata.json"] = approvedHash
                }
            };

            PowerForgeReleaseService.CaptureApprovedMutationInputContents(plan);
            File.WriteAllText(metadataPath, "{ \"value\": \"replaced\" }");

            Assert.Equal(
                "{ \"value\": \"approved\" }",
                PowerForgeReleaseService.ReadApprovedMutationInputText(plan, metadataPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ApprovedAppleMutationConfig_CapturesOnlyInputsUsedByTheAction()
    {
        var plan = new PowerForgeAppleReleasePlan
        {
            ProjectRoot = CreateSandbox(),
            Action = PowerForgeAppleReleaseAction.Archive,
            MetadataConfigPath = "missing-metadata.json",
            AppInfoConfigPath = "missing-app-info.json",
            GovernanceConfigPath = "missing-governance.json",
            ScreenshotConfigPath = "missing-screenshots.json",
            VersionSourcePath = "missing-version-source.xcconfig"
        };
        try
        {
            PowerForgeReleaseService.CaptureApprovedMutationInputContents(plan);

            Assert.Empty(plan.ApprovedMutationInputContents);
        }
        finally
        {
            TryDelete(plan.ProjectRoot);
        }
    }

    [Fact]
    public void ApprovedAppleMutationConfig_PreservesPlatformPathIdentityAndRemovesUtf8Bom()
    {
        var root = CreateSandbox();
        try
        {
            var metadataPath = Path.Combine(root, "Metadata.json");
            var payload = System.Text.Encoding.UTF8.GetBytes("{ \"value\": \"approved\" }");
            File.WriteAllBytes(metadataPath, System.Text.Encoding.UTF8.GetPreamble().Concat(payload).ToArray());
            var approvedHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(metadataPath)))
                .ToLowerInvariant();
            var plan = new PowerForgeAppleReleasePlan
            {
                ProjectRoot = root,
                SyncMetadata = true,
                MetadataConfigPath = metadataPath,
                ApprovedMutationInputFilesSha256 = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Metadata.json"] = approvedHash
                }
            };

            PowerForgeReleaseService.CaptureApprovedMutationInputContents(plan);

            Assert.Equal("{ \"value\": \"approved\" }", PowerForgeReleaseService.ReadApprovedMutationInputText(plan, metadataPath));
            Assert.Equal(
                Path.DirectorySeparatorChar == '\\',
                plan.ApprovedMutationInputContents.ContainsKey(Path.Combine(root, "metadata.json")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePlan_BindsDirectNotarizationControls()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = Assert.Single(spec.AppleApps!.Apps);
            app.Platform = ApplePlatform.macOS;
            app.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Direct-distribution planning must not query App Store Connect."));
            var approved = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Upload,
                PlanOnly = true
            });
            spec.AppleApps.DirectDistribution.Staple = false;
            spec.AppleApps.DirectDistribution.Assess = false;
            var changed = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Upload,
                PlanOnly = true
            });

            Assert.NotEqual(approved.AppleReceipt!.MutationInputsSha256, changed.AppleReceipt!.MutationInputsSha256);
            Assert.NotEqual(approved.AppleReceipt.PlanSha256, changed.AppleReceipt.PlanSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePlan_BindsCompleteXcodeExecutionControls()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Archive planning must not query App Store Connect."));
            var approved = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Archive,
                PlanOnly = true
            });
            spec.AppleApps!.XcodeBuildExecutable = "/reviewed/tools/xcodebuild";
            spec.AppleApps.TeamId = "CHANGEDTEAM";
            var changed = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Archive,
                PlanOnly = true
            });

            Assert.NotEqual(approved.AppleReceipt!.MutationInputsSha256, changed.AppleReceipt!.MutationInputsSha256);
            Assert.NotEqual(approved.AppleReceipt.PlanSha256, changed.AppleReceipt.PlanSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePlan_BindsRequiredScreenshotApprovalManifestBytes()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var screenshotFolder = Directory.CreateDirectory(Path.Combine(root, "screenshots"));
            var screenshotPath = Path.Combine(screenshotFolder.FullName, "home.png");
            File.WriteAllBytes(screenshotPath, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII="));
            const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
            var screenshotSpec = new AppStoreConnectScreenshotSyncSpec
            {
                AppId = "6778025328",
                VersionString = "1.2.0",
                Platform = ApplePlatform.iOS,
                Locale = "en-US",
                ScreenshotSets =
                [
                    new AppStoreConnectScreenshotSetSyncSpec
                    {
                        ScreenshotDisplayType = "APP_IPHONE_67",
                        Path = "screenshots",
                        AllowedDimensions = ["1x1"]
                    }
                ],
                Quality = new AppStoreConnectScreenshotQualitySpec
                {
                    Enabled = true,
                    MinimumFileBytes = 1,
                    MinimumKilobytesPerMegapixel = 0,
                    RequireApprovalManifest = true,
                    ApprovalManifestPath = "approval.json"
                }
            };
            File.WriteAllText(
                Path.Combine(root, "screenshots.json"),
                System.Text.Json.JsonSerializer.Serialize(screenshotSpec));
            var approval = new AppStoreConnectScreenshotApprovalService().Create(
                new AppStoreConnectScreenshotApprovalRequest
                {
                    Spec = screenshotSpec,
                    BaseDirectory = root,
                    AllowedRoot = screenshotFolder.FullName,
                    VersionString = "1.2.0",
                    SourceCommit = sourceCommit,
                    ApprovedBy = "release-owner"
                });
            var approvalPath = Path.Combine(root, "approval.json");
            File.WriteAllText(approvalPath, System.Text.Json.JsonSerializer.Serialize(approval));
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.SyncScreenshots = true;
            spec.AppleApps.ScreenshotConfigPath = "screenshots.json";
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                checkAppleReleaseReadiness: (_, request) => CreateReadyReleaseReadiness(request));

            var approved = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Screenshots,
                AppleSourceCommit = sourceCommit,
                PlanOnly = true
            });
            approval.ApprovalEvidence = "reviewed-evidence-changed";
            File.WriteAllText(approvalPath, System.Text.Json.JsonSerializer.Serialize(approval));
            var changed = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Screenshots,
                AppleSourceCommit = sourceCommit,
                PlanOnly = true
            });

            Assert.NotEqual(approved.AppleReceipt!.MutationInputsSha256, changed.AppleReceipt!.MutationInputsSha256);
            Assert.Contains("approval.json", approved.AppleReceipt.MutationInputFiles.Keys);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("generate")]
    [InlineData("regenerate")]
    [InlineData("executable")]
    [InlineData("timeout")]
    public void Execute_ApplePlan_BindsProjectGenerationControls(string changedControl)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            File.WriteAllText(Path.Combine(root, "project.yml"), "name: CasaRay");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Archive planning must not query App Store Connect."));
            var approved = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Archive,
                PlanOnly = true
            });
            var app = Assert.Single(spec.AppleApps!.Apps);
            switch (changedControl)
            {
                case "generate":
                    app.GenerateProjectIfMissing = true;
                    break;
                case "regenerate":
                    app.RegenerateProject = true;
                    break;
                case "executable":
                    app.XcodeGenExecutable = "/reviewed/tools/xcodegen";
                    break;
                case "timeout":
                    app.ProjectGenerationTimeoutSeconds++;
                    break;
            }
            var changed = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Archive,
                PlanOnly = true
            });

            Assert.NotEqual(approved.AppleReceipt!.PlanSha256, changed.AppleReceipt!.PlanSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Prepare)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight)]
    [InlineData(PowerForgeAppleReleaseAction.Advance)]
    public void AppleDistributionPlanActions_RequireObservedRemoteBuildState(PowerForgeAppleReleaseAction action)
    {
        var plan = new PowerForgeAppleReleasePlan { Action = action };
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            AppStoreConnectAppId = "6778025328",
            DistributionRoute = AppleDistributionRoute.AppStore
        };

        Assert.True(PowerForgeReleaseService.RequiresObservedApplePlanState(plan, app));
    }

    [Fact]
    public void Execute_AppleDistributionPlan_RejectsUnselectedRemoteBuild()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.TestFlightBetaGroupNames = ["Internal"];
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null));

            var exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.TestFlight,
                    PlanOnly = true
                }));

            Assert.Contains("uniquely selected", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void AppleArchiveUploadSnapshot_RejectsEscapingSymbolicLinks()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = CreateSandbox();
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root, "CasaRay.xcarchive"));
            var outside = Path.Combine(root, "outside-payload");
            File.WriteAllText(outside, "outside");
            File.CreateSymbolicLink(Path.Combine(archive.FullName, "escaped"), outside);
            var expected = AppleNotarizationService.ComputeArtifactSha256(archive.FullName);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AppleArchiveUploadSnapshot.Create(archive.FullName, expected));

            Assert.Contains("inside the archive", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleCheckpoint_ArchivesFromDetachedExactSourceSnapshot()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            File.WriteAllText(
                Path.Combine(root, "Package.swift"),
                "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"CasaRay\")\n");
            var sourceFile = Path.Combine(root, "CasaRay.xcodeproj", "project.pbxproj");
            var committedContents = File.ReadAllText(sourceFile);
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "exact source");
            var sourceCommit = RunSnapshotGit(root, "rev-parse", "HEAD").Trim();
            string? archivedProjectPath = null;
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Archive-only checkpoint must not query App Store Connect."),
                archiveAppleApp: request =>
                {
                    archivedProjectPath = request.ProjectPath;
                    var snapshotRoot = Path.GetDirectoryName(request.ProjectPath)!;
                    Assert.True(Directory.Exists(Path.Combine(snapshotRoot, ".swiftpm", "configuration")));
                    Assert.True(Directory.Exists(Path.Combine(snapshotRoot, ".swiftpm", "xcode")));
                    File.AppendAllText(sourceFile, "\n// concurrent original-worktree mutation");
                    Assert.Equal(committedContents, File.ReadAllText(Path.Combine(request.ProjectPath, "project.pbxproj")));
                    var archive = Directory.CreateDirectory(request.ArchivePath!);
                    File.WriteAllText(Path.Combine(archive.FullName, "payload"), "archive from immutable source");
                    return CreateSuccessfulArchive(request);
                });

            var result = service.Execute(
                CreateAppleAutomationSpec(root, keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Archive,
                    AppleSourceCommit = sourceCommit,
                    RequireImmutableAppleSourceSnapshot = true
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(archivedProjectPath);
            Assert.False(archivedProjectPath!.StartsWith(root, StringComparison.Ordinal));
            Assert.False(Directory.Exists(Path.GetDirectoryName(archivedProjectPath!)!));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string RunSnapshotGit(string root, params string[] arguments)
    {
        var result = new GitClient(defaultTimeout: TimeSpan.FromMinutes(1))
            .RunRawAsync(root, arguments, TimeSpan.FromMinutes(1))
            .GetAwaiter()
            .GetResult();
        Assert.True(result.Succeeded, result.StdErr);
        return result.StdOut;
    }

}
