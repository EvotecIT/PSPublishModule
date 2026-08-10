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

    [Fact]
    public void Execute_ConfiguredAppleUploadOnly_ResumesAfterAttestedArchiveWasRemoved()
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
                    request => CreateReleaseState(request, ++stateCalls == 1 ? null : "VALID"),
                    archiveAppleApp: request =>
                    {
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "payload"), "attested archive");
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: CreateSuccessfulUpload)
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload,
                        AppleSourceCommit = sourceCommit
                    });
            Assert.True(seeded.Success, seeded.ErrorMessage);
            var archivePath = Assert.Single(seeded.AppleAppPlan!.Apps).ArchivePath;
            Directory.Delete(archivePath, recursive: true);
            spec.AppleApps.Archive = false;
            spec.AppleApps.Upload = true;

            var resumed = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    archiveAppleApp: _ => throw new InvalidOperationException("Verified configured recovery must skip archive."),
                    uploadAppleApp: _ => throw new InvalidOperationException("Verified configured recovery must skip upload."))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Configured,
                        AppleSourceCommit = sourceCommit
                    });

            Assert.True(resumed.Success, resumed.ErrorMessage);
            Assert.True(Assert.Single(resumed.AppleApps).ResumedExistingBuild);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUploadExisting_RejectsAttestationForDifferentCheckpointArchive()
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
                    request => CreateReleaseState(request, ++stateCalls == 1 ? null : "VALID"),
                    archiveAppleApp: request =>
                    {
                        var archive = Directory.CreateDirectory(request.ArchivePath!);
                        File.WriteAllText(Path.Combine(archive.FullName, "payload"), "first archive");
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: CreateSuccessfulUpload)
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload,
                        AppleSourceCommit = sourceCommit
                    });
            Assert.True(seeded.Success, seeded.ErrorMessage);
            var archivePath = Assert.Single(seeded.AppleAppPlan!.Apps).ArchivePath;
            File.WriteAllText(Path.Combine(archivePath, "payload"), "second checkpoint archive");
            var expectedArchiveSha256 = AppleNotarizationService.ComputeArtifactSha256(archivePath);
            spec.AppleApps.Archive = false;
            spec.AppleApps.Upload = true;

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    uploadAppleApp: _ => throw new InvalidOperationException("A mismatched remote build must not be uploaded or resumed."))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.UploadExisting,
                        AppleSourceCommit = sourceCommit,
                        AppleExpectedArchiveSha256ByTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["CasaRay iOS"] = expectedArchiveSha256
                        }
                    });

            Assert.False(result.Success);
            Assert.False(Assert.Single(result.AppleApps).ResumedExistingBuild);
            Assert.Contains("no immutable local upload receipt", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ConfiguredRemoteMutation_RefreshesAuthoritativeFinalState()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Archive = false;
            spec.AppleApps!.PrepareDistribution = true;
            var reads = 0;

            var result = CreateAppleAutomationService(
                    request =>
                    {
                        reads++;
                        return CreateReleaseState(request, "VALID");
                    },
                    prepareAppleDistribution: CreateSuccessfulPreparation)
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Configured
                    });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, reads);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal("VALID", target.BuildProcessingState);
            Assert.True(target.BuildSelected);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ConfiguredRemoteMutation_FailsReceiptWhenFinalStateCannotBeRead()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Archive = false;
            spec.AppleApps!.PrepareDistribution = true;
            var prepared = false;

            var result = CreateAppleAutomationService(
                    _ => throw new IOException("final read unavailable"),
                    prepareAppleDistribution: request =>
                    {
                        prepared = true;
                        return CreateSuccessfulPreparation(request);
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Configured
                    });

            Assert.True(prepared);
            Assert.False(result.Success);
            Assert.Contains("final App Store Connect state", result.AppleReceipt!.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("final read unavailable", result.AppleReceipt.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Upload)]
    [InlineData(PowerForgeAppleReleaseAction.Configured)]
    public void Execute_AppleUploadAttestation_UsesResolvedProjectVersionAndBuild(
        PowerForgeAppleReleaseAction action)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Upload = action == PowerForgeAppleReleaseAction.Configured;
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            spec.AppleApps.Automation.CleanupAfterProcessing = false;
            spec.AppleApps.Automation.PollIntervalSeconds = 1;
            spec.AppleApps.Automation.ProcessingTimeoutSeconds = 2;
            var states = new Queue<string?>(new string?[] { null, "PROCESSING", "VALID" });
            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, states.Dequeue()),
                    delay: _ => { },
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
                        AppleAction = action,
                        AppleSourceCommit = "0123456789abcdef0123456789abcdef01234567"
                    });

            Assert.True(result.Success, result.ErrorMessage);
            var journal = new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!);
            var upload = Assert.Single(journal, receipt => receipt.OperationPhase == "UploadAttested");
            var uploadTarget = Assert.Single(upload.Targets);
            Assert.Equal("1.2.0", uploadTarget.Version);
            Assert.Equal("9", uploadTarget.Build);
            Assert.Equal("Completed", result.AppleReceipt!.OperationPhase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_RejectsAdoptionWhenResumeIsDisabled()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"))
                    .Execute(
                        CreateAppleAutomationSpec(root, keyPath),
                        new PowerForgeReleaseRequest
                        {
                            ConfigPath = Path.Combine(root, "powerforge.release.json"),
                            AppleAction = PowerForgeAppleReleaseAction.Upload,
                            AppleAdoptExistingBuild = true,
                            AppleActionConfirmed = true,
                            AppleResume = false
                        }));

            Assert.Contains("requires", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Resume=true", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePlan_RejectsEmptyReceiptHistoryPath()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.ReceiptHistoryPath = " ";

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        PlanOnly = true,
                        AppleAction = PowerForgeAppleReleaseAction.Upload
                    }));

            Assert.Contains("ReceiptHistoryPath is required", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("history-equals-receipt")]
    [InlineData("history-contains-plan")]
    [InlineData("history-under-lock")]
    [InlineData("receipt-equals-plan")]
    [InlineData("history-under-archive-root")]
    [InlineData("plan-overwrites-project")]
    public void Execute_ApplePlan_RejectsOverlappingAutomationOutputPaths(string scenario)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var automation = spec.AppleApps!.Automation;
            switch (scenario)
            {
                case "history-equals-receipt":
                    automation.ReceiptHistoryPath = automation.ReceiptPath;
                    break;
                case "history-contains-plan":
                    automation.ReceiptHistoryPath = "build/powerforge/apple";
                    break;
                case "history-under-lock":
                    automation.LockPath = "build/powerforge/apple/release";
                    automation.ReceiptHistoryPath = "build/powerforge/apple/release/history";
                    break;
                case "receipt-equals-plan":
                    automation.PlanReceiptPath = automation.ReceiptPath;
                    break;
                case "history-under-archive-root":
                    automation.ReceiptHistoryPath = "build/powerforge/apple/archives/receipts";
                    break;
                case "plan-overwrites-project":
                    automation.PlanReceiptPath = "CasaRay.xcodeproj/project.pbxproj";
                    break;
            }

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        PlanOnly = true,
                        AppleAction = PowerForgeAppleReleaseAction.Archive
                    }));

            Assert.True(
                exception.Message.Contains("ReceiptHistoryPath", StringComparison.Ordinal) ||
                exception.Message.Contains("distinct paths", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("automation output", StringComparison.OrdinalIgnoreCase),
                exception.Message);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_DirectNotarizationCrash_PersistsAcceptedSubmissionBeforeLocalPostProcessing()
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "EasyControlXAgent.xcodeproj", "1.0.0", "4");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.TeamId = "8ZPGZ79T7J";
            var app = Assert.Single(spec.AppleApps.Apps);
            app.Name = "EasyControlX Agent";
            app.ProjectPath = "EasyControlXAgent.xcodeproj";
            app.Scheme = "EasyControlXAgent";
            app.Platform = ApplePlatform.macOS;
            app.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            app.AppStoreConnectAppId = null;

            var service = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        Directory.CreateDirectory(Path.Combine(request.ExportPath!, "EasyControlX Agent.app"));
                        File.WriteAllText(
                            Path.Combine(request.ExportPath!, "EasyControlX Agent.app", "payload"),
                            "signed direct app");
                        return CreateSuccessfulUpload(request);
                    },
                    notarizeAppleArtifact: request =>
                    {
                        request.AcceptedCheckpoint!(new AppleNotarizationAcceptedCheckpoint
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath),
                            SubmissionPath = request.ArtifactPath + ".zip",
                            SubmissionId = "accepted-before-local-crash",
                            Status = "Accepted"
                        });
                        throw new InvalidOperationException("simulated process loss after Apple acceptance");
                    });
            var result = service.Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload,
                        AppleSourceCommit = sourceCommit
                    });

            Assert.False(result.Success);
            var accepted = Assert.Single(
                new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!),
                receipt => receipt.OperationPhase == "NotarizationAccepted");
            Assert.Equal(sourceCommit, accepted.SourceCommit);
            var target = Assert.Single(accepted.Targets);
            Assert.Equal("accepted-before-local-crash", target.NotarizationSubmissionId);
            Assert.Equal("Accepted", target.NotarizationStatus);
            Assert.False(accepted.Success);

            var storedArtifact = Assert.IsType<string>(target.DirectArtifactPath);
            Assert.False(Path.IsPathRooted(storedArtifact));
            var protectedArtifact = Path.Combine(root, storedArtifact);
            var cleanupCandidate = Directory.GetParent(protectedArtifact)!.FullName;
            Directory.SetLastWriteTimeUtc(cleanupCandidate, DateTime.UtcNow.AddDays(-30));
            spec.AppleApps.Automation.ArtifactRetentionDays = 0;
            var cleanup = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Cleanup,
                    AppleActionConfirmed = true,
                    AppleSourceCommit = sourceCommit
                });

            Assert.True(cleanup.Success, cleanup.ErrorMessage);
            Assert.True(Directory.Exists(protectedArtifact) || File.Exists(protectedArtifact));
            Assert.DoesNotContain(
                FrameworkCompatibility.GetRelativePath(root, cleanupCandidate).Replace('\\', '/'),
                cleanup.AppleReceipt!.Cleanup.RemovedPaths);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_DirectNotarizationCrash_PersistsExactPostStapleCheckpoint()
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "EasyControlXAgent.xcodeproj", "1.0.0", "4");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.TeamId = "8ZPGZ79T7J";
            var app = Assert.Single(spec.AppleApps.Apps);
            app.Name = "EasyControlX Agent";
            app.ProjectPath = "EasyControlXAgent.xcodeproj";
            app.Scheme = "EasyControlXAgent";
            app.Platform = ApplePlatform.macOS;
            app.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            app.AppStoreConnectAppId = null;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        Directory.CreateDirectory(Path.Combine(request.ExportPath!, "EasyControlX Agent.app"));
                        File.WriteAllText(Path.Combine(request.ExportPath!, "EasyControlX Agent.app", "payload"), "signed direct app");
                        return CreateSuccessfulUpload(request);
                    },
                    notarizeAppleArtifact: request =>
                    {
                        var hash = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath);
                        request.AcceptedCheckpoint!(new AppleNotarizationAcceptedCheckpoint
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = hash,
                            SubmissionPath = request.ArtifactPath + ".zip",
                            SubmissionId = "accepted-and-stapled",
                            Status = "Accepted"
                        });
                        File.AppendAllText(Path.Combine(request.ArtifactPath, "payload"), "-stapled-ticket");
                        var stapledHash = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath);
                        request.StapledCheckpoint!(new AppleNotarizationStapledCheckpoint
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = stapledHash,
                            SubmissionId = "accepted-and-stapled",
                            Status = "Accepted"
                        });
                        throw new InvalidOperationException("simulated process loss before Gatekeeper assessment");
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload,
                        AppleSourceCommit = sourceCommit
                    });

            Assert.False(result.Success);
            var stapled = Assert.Single(
                new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!),
                receipt => receipt.OperationPhase == "NotarizationStapled");
            Assert.Equal(sourceCommit, stapled.SourceCommit);
            var target = Assert.Single(stapled.Targets);
            Assert.True(target.Stapled);
            Assert.True(target.StapleValidated);
            Assert.Equal("accepted-and-stapled", target.NotarizationSubmissionId);
            Assert.False(Path.IsPathRooted(target.DirectArtifactPath));
            Assert.Equal(
                AppleNotarizationService.ComputeArtifactSha256(Path.Combine(root, target.DirectArtifactPath!)),
                target.DirectArtifactSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_DirectExport_UsesPrivateInputAndPublishesVerifiedArtifactBeforeNotarization()
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "EasyControlXAgent.xcodeproj", "1.0.0", "4");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.TeamId = "8ZPGZ79T7J";
            var app = Assert.Single(spec.AppleApps.Apps);
            app.Name = "EasyControlX Agent";
            app.ProjectPath = "EasyControlXAgent.xcodeproj";
            app.Scheme = "EasyControlXAgent";
            app.Platform = ApplePlatform.macOS;
            app.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            app.AppStoreConnectAppId = null;
            string? privateExport = null;
            AppleNotarizationRequest? notarizationRequest = null;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        privateExport = request.ExportPath;
                        Assert.Contains("apple-direct-exports", request.ExportPath!, StringComparison.Ordinal);
                        var package = Path.Combine(request.ExportPath!, "EasyControlX Agent.pkg");
                        File.WriteAllText(package, "approved developer-id export");
                        return CreateSuccessfulUpload(request);
                    },
                    notarizeAppleArtifact: request =>
                    {
                        notarizationRequest = request;
                        var hash = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath);
                        return new AppleNotarizationResult
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = hash,
                            SubmissionPath = request.ArtifactPath,
                            SubmissionId = "private-export-submission",
                            Status = "Accepted",
                            Submission = new ProcessRunResult(0, "accepted", string.Empty, "xcrun", TimeSpan.Zero, false),
                            Staple = new ProcessRunResult(0, "stapled", string.Empty, "xcrun", TimeSpan.Zero, false),
                            StapleValidation = new ProcessRunResult(0, "valid", string.Empty, "xcrun", TimeSpan.Zero, false),
                            Assessment = new ProcessRunResult(0, "accepted", string.Empty, "spctl", TimeSpan.Zero, false)
                        };
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload,
                        AppleSourceCommit = sourceCommit
                    });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(privateExport);
            Assert.False(Directory.Exists(privateExport));
            Assert.NotNull(notarizationRequest);
            var publicExport = result.AppleAppPlan!.Apps.Single().ExportPath;
            Assert.StartsWith(Path.GetFullPath(publicExport) + Path.DirectorySeparatorChar, notarizationRequest!.ArtifactPath, StringComparison.Ordinal);
            Assert.Equal("approved developer-id export", File.ReadAllText(notarizationRequest.ArtifactPath));
            Assert.Equal(
                AppleNotarizationService.ComputeArtifactSha256(notarizationRequest.ArtifactPath),
                notarizationRequest.ExpectedArtifactSha256);
            Assert.Equal(Path.GetFullPath(publicExport), result.AppleApps.Single().Upload!.ExportPath);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
