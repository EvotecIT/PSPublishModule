namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Status, true)]
    [InlineData(PowerForgeAppleReleaseAction.Doctor, true)]
    [InlineData(PowerForgeAppleReleaseAction.TestFlight, true)]
    [InlineData(PowerForgeAppleReleaseAction.Prepare, false)]
    [InlineData(PowerForgeAppleReleaseAction.Screenshots, false)]
    [InlineData(PowerForgeAppleReleaseAction.SubmitAppReview, false)]
    [InlineData(PowerForgeAppleReleaseAction.Release, false)]
    public void TestFlightOnlyRoute_ExecutesOnlyItsSupportedControlPlaneActions(
        PowerForgeAppleReleaseAction action,
        bool expected)
    {
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            DistributionRoute = AppleDistributionRoute.TestFlightOnly,
            TestFlightPolicy = AppleTestFlightPolicy.Internal
        };

        Assert.Equal(expected, PowerForgeReleaseService.ShouldExecuteAppleTarget(action, app));
    }

    [Theory]
    [InlineData(AppleTestFlightPolicy.Disabled, false)]
    [InlineData(AppleTestFlightPolicy.Internal, false)]
    [InlineData(AppleTestFlightPolicy.External, true)]
    public void TestFlightReview_ExecutesOnlyForExternalAudience(AppleTestFlightPolicy policy, bool expected)
    {
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            DistributionRoute = AppleDistributionRoute.TestFlightOnly,
            TestFlightPolicy = policy
        };

        Assert.Equal(
            expected,
            PowerForgeReleaseService.ShouldExecuteAppleTarget(
                PowerForgeAppleReleaseAction.SubmitTestFlightReview,
                app));
    }

    [Fact]
    public void AppleReleaseDoctor_FindsControlPlaneFailuresBeforeSubmission()
    {
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            Name = "CasaRay",
            BundleId = "com.evotec.casarray",
            AppStoreConnectAppId = "app-1",
            DistributionRoute = AppleDistributionRoute.AppStore,
            ProjectPath = Path.GetTempFileName()
        };
        try
        {
            var diagnostics = AppleReleaseDoctor.Evaluate(
                new PowerForgeAppleReleasePlan { Apps = new[] { app } },
                app,
                new AppStoreConnectControlPlaneState { AppId = "app-1" });

            Assert.Contains(diagnostics, item => item.Code == "APPLE_REVIEW_DETAILS_MISSING");
            Assert.Contains(diagnostics, item => item.Code == "APPLE_AGE_RATING_MISSING");
            Assert.Contains(diagnostics, item => item.Code == "APPLE_PRICE_SCHEDULE_MISSING");
            Assert.Contains(diagnostics, item => item.Code == "APPLE_AVAILABILITY_MISSING");
            Assert.Contains(diagnostics, item => item.Code == "APPLE_WEBHOOK_MISSING");
        }
        finally
        {
            File.Delete(app.ProjectPath);
        }
    }

    [Fact]
    public void Execute_AppleDoctor_DiscoversAppIdAndWritesActionableReceipt()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = Assert.Single(spec.AppleApps!.Apps);
            app.AppStoreConnectAppId = null;
            app.Capabilities = new[] { "AppleIntelligence", "AppIntents" };

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    findAppleApps: (_, bundleId) => new[]
                    {
                        new AppStoreConnectAppInfo
                        {
                            Id = "6778025328",
                            BundleId = bundleId,
                            Name = "CasaRay"
                        }
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Doctor,
                        AppleSummaryOnly = true
                    });

            Assert.True(result.Success);
            var receipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt);
            Assert.Equal(2, receipt.SchemaVersion);
            var target = Assert.Single(receipt.Targets);
            Assert.Equal(AppleDistributionRoute.AppStore, target.DistributionRoute);
            Assert.Equal("6778025328", target.AppId);
            Assert.True(target.AppIdDiscovered);
            Assert.Contains("AppleIntelligence", target.Capabilities);
            Assert.Contains(target.Diagnostics, diagnostic => diagnostic.Code == "APPLE_APP_ID_DISCOVERED");
            Assert.Contains(target.NextActions, action => action.Contains("Persist the discovered", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleDoctor_DetectsMissingEmbeddedProductBeforePublication()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var parent = Assert.Single(spec.AppleApps!.Apps);
            parent.RequiredEmbeddedBundleIds = new[] { "com.evotecit.casaray.watchkitapp" };

            var result = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Doctor
                    });

            Assert.False(result.Success);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Contains(target.Diagnostics, diagnostic => diagnostic.Code == "APPLE_EMBEDDED_BUNDLE_MISSING");
            Assert.Contains("required embedded bundle", result.AppleReceipt.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void AppleReleaseDoctor_InspectsProjectsReferencedByWorkspace()
    {
        var root = CreateSandbox();
        try
        {
            var workspace = Directory.CreateDirectory(Path.Combine(root, "CasaRay.xcworkspace"));
            File.WriteAllText(
                Path.Combine(workspace.FullName, "contents.xcworkspacedata"),
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Workspace version="1.0">
                  <FileRef location="group:CasaRay.xcodeproj"></FileRef>
                </Workspace>
                """);
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            var embeddedBundleId = "com.evotecit.casaray.watchkitapp";
            var projectFile = Path.Combine(root, "CasaRay.xcodeproj", "project.pbxproj");
            File.AppendAllText(projectFile, Environment.NewLine + $"PRODUCT_BUNDLE_IDENTIFIER = {embeddedBundleId};");
            var app = new PowerForgeAppleAppReleaseTargetPlan
            {
                Name = "CasaRay Workspace",
                BundleId = "com.evotecit.casaray",
                AppStoreConnectAppId = "6778025328",
                DistributionRoute = AppleDistributionRoute.AppStore,
                ProjectPath = workspace.FullName,
                RequiredEmbeddedBundleIds = new[] { embeddedBundleId }
            };

            var diagnostics = AppleReleaseDoctor.Evaluate(
                new PowerForgeAppleReleasePlan { Apps = new[] { app } },
                app);

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "APPLE_EMBEDDED_BUNDLE_MISSING");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleStatus_DisabledTestFlightRemovesBetaAdvice()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            Assert.Single(spec.AppleApps!.Apps).TestFlightPolicy = AppleTestFlightPolicy.Disabled;

            var result = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Status
                    });

            Assert.True(result.Success);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal(AppleTestFlightPolicy.Disabled, target.TestFlightPolicy);
            Assert.DoesNotContain(target.NextActions, action =>
                action.Contains("TestFlight", StringComparison.OrdinalIgnoreCase) ||
                action.Contains("Beta App Review", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleTestFlightPassesTargetAudiencePolicyToDistribution()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.TestFlightBetaGroupNames = new[] { "Home" };
            Assert.Single(spec.AppleApps.Apps).TestFlightPolicy = AppleTestFlightPolicy.Internal;
            AppStoreConnectTestFlightDistributionRequest? captured = null;

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    distributeTestFlight: request =>
                    {
                        captured = request;
                        return new AppStoreConnectTestFlightDistributionResult();
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.TestFlight
                    });

            Assert.True(result.Success);
            Assert.NotNull(captured);
            Assert.Equal(AppleTestFlightPolicy.Internal, captured!.TestFlightPolicy);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_EmbeddedCompanionRequiresKnownParent()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Watch.xcodeproj", "1.0.0", "1");
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    new PowerForgeReleaseSpec
                    {
                        AppleApps = new PowerForgeAppleReleaseOptions
                        {
                            ProjectRoot = root,
                            Apps = new[]
                            {
                                new AppleAppConfiguration
                                {
                                    Name = "CasaRay Watch",
                                    BundleId = "com.evotecit.casaray.watchkitapp",
                                    Platform = ApplePlatform.watchOS,
                                    ProjectPath = "Watch.xcodeproj",
                                    Scheme = "CasaRayWatch",
                                    DistributionRoute = AppleDistributionRoute.EmbeddedCompanion,
                                    ProductRole = AppleProductRole.CompanionApp
                                }
                            }
                        }
                    },
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Archive
                    }));

            Assert.Contains("requires ParentTarget", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleTopologyRejectsCyclicParentTargets()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "WatchA.xcodeproj", "1.0.0", "1");
            CreateXcodeProject(root, "WatchB.xcodeproj", "1.0.0", "1");
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    new PowerForgeReleaseSpec
                    {
                        AppleApps = new PowerForgeAppleReleaseOptions
                        {
                            ProjectRoot = root,
                            Apps = new[]
                            {
                                new AppleAppConfiguration
                                {
                                    Name = "Watch A",
                                    BundleId = "com.example.watch-a",
                                    Platform = ApplePlatform.watchOS,
                                    ProjectPath = "WatchA.xcodeproj",
                                    Scheme = "WatchA",
                                    DistributionRoute = AppleDistributionRoute.EmbeddedCompanion,
                                    ParentTarget = "Watch B"
                                },
                                new AppleAppConfiguration
                                {
                                    Name = "Watch B",
                                    BundleId = "com.example.watch-b",
                                    Platform = ApplePlatform.watchOS,
                                    ProjectPath = "WatchB.xcodeproj",
                                    Scheme = "WatchB",
                                    DistributionRoute = AppleDistributionRoute.EmbeddedCompanion,
                                    ParentTarget = "Watch A"
                                }
                            }
                        }
                    },
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Archive
                    }));

            Assert.Contains("cyclic ParentTarget", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void DirectNotarizationCredentialsAreRequiredOnlyForUploadActions()
    {
        var apps = new[]
        {
            new PowerForgeAppleAppReleaseTargetPlan
            {
                Name = "Agent",
                DistributionRoute = AppleDistributionRoute.DirectNotarized
            }
        };

        Assert.False(PowerForgeReleaseService.RequiresDirectNotarizationCredentials(
            PowerForgeAppleReleaseAction.Archive,
            configuredUpload: false,
            apps));
        Assert.False(PowerForgeReleaseService.RequiresDirectNotarizationCredentials(
            PowerForgeAppleReleaseAction.Doctor,
            configuredUpload: false,
            apps));
        Assert.True(PowerForgeReleaseService.RequiresDirectNotarizationCredentials(
            PowerForgeAppleReleaseAction.Upload,
            configuredUpload: false,
            apps));
        Assert.True(PowerForgeReleaseService.RequiresDirectNotarizationCredentials(
            PowerForgeAppleReleaseAction.Advance,
            configuredUpload: false,
            apps));
        Assert.True(PowerForgeReleaseService.RequiresDirectNotarizationCredentials(
            PowerForgeAppleReleaseAction.Configured,
            configuredUpload: true,
            apps));
    }

    [Fact]
    public void Execute_AppleUpload_DirectMacExportsNotarizesAndRecordsReceipt()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "EasyControlXAgent.xcodeproj", "1.0.0", "4");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.TeamId = "8ZPGZ79T7J";
            spec.AppleApps.AllowProvisioningUpdates = false;
            spec.AppleApps.DirectDistribution.KeychainProfile = null;
            var app = Assert.Single(spec.AppleApps.Apps);
            app.Name = "EasyControlX Agent";
            app.ProjectPath = "EasyControlXAgent.xcodeproj";
            app.Scheme = "EasyControlXAgent";
            app.Platform = ApplePlatform.macOS;
            app.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            app.AppStoreConnectAppId = null;
            var stateCalls = 0;
            AppleNotarizationRequest? notarizationRequest = null;

            var result = CreateAppleAutomationService(
                    _ =>
                    {
                        stateCalls++;
                        throw new InvalidOperationException("Direct distribution must not query App Store release state.");
                    },
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        var appPath = Directory.CreateDirectory(Path.Combine(request.ExportPath!, "EasyControlX Agent.app"));
                        return new AppleAppArchiveUploadResult
                        {
                            ArchivePath = request.ArchivePath,
                            ExportPath = request.ExportPath!,
                            ExportOptionsPlistPath = Path.Combine(request.ExportPath!, "ExportOptions.plist"),
                            ProcessResult = new ProcessRunResult(0, "export-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                        };
                    },
                    notarizeAppleArtifact: request =>
                    {
                        notarizationRequest = request;
                        return new AppleNotarizationResult
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = "direct-artifact-sha",
                            SubmissionPath = request.ArtifactPath + ".zip",
                            SubmissionId = "notary-1",
                            Status = "Accepted",
                            Submission = new ProcessRunResult(0, "accepted", string.Empty, "xcrun", TimeSpan.FromSeconds(1), false),
                            Staple = new ProcessRunResult(0, "stapled", string.Empty, "xcrun", TimeSpan.FromSeconds(1), false),
                            StapleValidation = new ProcessRunResult(0, "valid", string.Empty, "xcrun", TimeSpan.FromSeconds(1), false),
                            Assessment = new ProcessRunResult(0, "accepted", string.Empty, "spctl", TimeSpan.FromSeconds(1), false)
                        };
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload
                    });

            Assert.True(result.Success);
            Assert.Equal(0, stateCalls);
            Assert.NotNull(notarizationRequest);
            Assert.Null(notarizationRequest!.KeychainProfile);
            Assert.Equal(keyPath, notarizationRequest.ApiKeyPath);
            Assert.Equal("TESTKEY123", notarizationRequest.ApiKeyId);
            Assert.Equal("issuer-id", notarizationRequest.ApiIssuerId);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal(AppleDistributionRoute.DirectNotarized, target.DistributionRoute);
            Assert.Equal("notary-1", target.NotarizationSubmissionId);
            Assert.Equal("Accepted", target.NotarizationStatus);
            Assert.True(target.Stapled);
            Assert.True(target.GatekeeperAccepted);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleUpload_PreservesAcceptedNotarizationWhenStaplingFails()
    {
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
            app.Platform = ApplePlatform.macOS;
            app.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            app.AppStoreConnectAppId = null;

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        var appPath = Directory.CreateDirectory(Path.Combine(request.ExportPath!, "EasyControlX Agent.app"));
                        return new AppleAppArchiveUploadResult
                        {
                            ArchivePath = request.ArchivePath,
                            ExportPath = request.ExportPath!,
                            ExportOptionsPlistPath = Path.Combine(request.ExportPath!, "ExportOptions.plist"),
                            ProcessResult = new ProcessRunResult(0, "export-ok", string.Empty, "xcodebuild", TimeSpan.Zero, false)
                        };
                    },
                    notarizeAppleArtifact: request => new AppleNotarizationResult
                    {
                        ArtifactPath = request.ArtifactPath,
                        ArtifactSha256 = "failed-artifact-sha",
                        SubmissionPath = request.ArtifactPath + ".zip",
                        SubmissionId = "accepted-then-staple-failed",
                        Status = "Accepted",
                        Submission = new ProcessRunResult(0, "accepted", string.Empty, "xcrun", TimeSpan.Zero, false),
                        Staple = new ProcessRunResult(1, string.Empty, "CloudKit unavailable", "xcrun", TimeSpan.Zero, false)
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload
                    });

            Assert.False(result.Success);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal("Accepted", target.NotarizationStatus);
            Assert.Equal("accepted-then-staple-failed", target.NotarizationSubmissionId);
            Assert.False(target.Stapled);
            Assert.Contains("ticket stapling", target.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            AppleNotarizationRequest? resumedRequest = null;
            var resumeService = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: _ => throw new InvalidOperationException("Accepted notarization resume must skip archive."),
                    uploadAppleApp: _ => throw new InvalidOperationException("Accepted notarization resume must skip export."),
                    notarizeAppleArtifact: request =>
                    {
                        resumedRequest = request;
                        return new AppleNotarizationResult
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = request.ExpectedArtifactSha256!,
                            SubmissionPath = request.ArtifactPath,
                            SubmissionId = request.AcceptedSubmissionId,
                            Status = "Accepted",
                            ResumedAcceptedSubmission = true,
                            Submission = new ProcessRunResult(0, "resumed", string.Empty, "xcrun", TimeSpan.Zero, false),
                            Staple = new ProcessRunResult(0, "stapled", string.Empty, "xcrun", TimeSpan.Zero, false),
                            StapleValidation = new ProcessRunResult(0, "valid", string.Empty, "xcrun", TimeSpan.Zero, false),
                            Assessment = new ProcessRunResult(0, "accepted", string.Empty, "spctl", TimeSpan.Zero, false)
                        };
                    });
            var configuredResumed = resumeService.Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Configured
                    });

            Assert.True(configuredResumed.Success);
            Assert.NotNull(resumedRequest);
            var configuredTarget = Assert.Single(configuredResumed.AppleApps);
            Assert.True(configuredTarget.ResumedAcceptedNotarization);
            Assert.Contains("notarySubmission", configuredTarget.SkippedSteps);

            resumedRequest = null;
            var resumed = resumeService.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            Assert.True(resumed.Success);
            Assert.NotNull(resumedRequest);
            Assert.Equal("accepted-then-staple-failed", resumedRequest!.AcceptedSubmissionId);
            Assert.Equal("failed-artifact-sha", resumedRequest.ExpectedArtifactSha256);
            var resumedTarget = Assert.Single(resumed.AppleReceipt!.Targets);
            Assert.True(resumedTarget.ResumedAcceptedNotarization);
            Assert.Contains("notarySubmission", resumedTarget.SkippedSteps);

            // Simulate another target failing after this target completed. Aggregate failure
            // must not turn a locally successful target into a notarization-resume candidate.
            var receiptPath = Path.Combine(root, "build", "powerforge", "apple", "release-receipt.json");
            var successfulReceipt = File.ReadAllText(receiptPath);
            Assert.Contains("\"success\": true", successfulReceipt, StringComparison.Ordinal);
            File.WriteAllText(
                receiptPath,
                successfulReceipt.Replace("\"success\": true", "\"success\": false", StringComparison.Ordinal));

            var archiveCalls = 0;
            var exportCalls = 0;
            AppleNotarizationRequest? nextReleaseRequest = null;
            var nextRelease = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: request =>
                    {
                        archiveCalls++;
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: request =>
                    {
                        exportCalls++;
                        Directory.CreateDirectory(Path.Combine(request.ExportPath!, "EasyControlX Agent.app"));
                        return new AppleAppArchiveUploadResult
                        {
                            ArchivePath = request.ArchivePath,
                            ExportPath = request.ExportPath!,
                            ExportOptionsPlistPath = Path.Combine(request.ExportPath!, "ExportOptions.plist"),
                            ProcessResult = new ProcessRunResult(0, "export-ok", string.Empty, "xcodebuild", TimeSpan.Zero, false)
                        };
                    },
                    notarizeAppleArtifact: request =>
                    {
                        nextReleaseRequest = request;
                        return new AppleNotarizationResult
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = "new-artifact-sha",
                            SubmissionPath = request.ArtifactPath + ".zip",
                            SubmissionId = "new-submission",
                            Status = "Accepted",
                            Submission = new ProcessRunResult(0, "accepted", string.Empty, "xcrun", TimeSpan.Zero, false)
                        };
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Upload
                    });

            Assert.True(nextRelease.Success);
            Assert.Equal(1, archiveCalls);
            Assert.Equal(1, exportCalls);
            Assert.NotNull(nextReleaseRequest);
            Assert.Null(nextReleaseRequest!.AcceptedSubmissionId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("No profiles for com.evotecit.casaray were found", "APPLE_PROVISIONING", "signing")]
    [InlineData("Asset validation failed ITMS-90161", "APPLE_ASSET_VALIDATION", "validation")]
    [InlineData("App Store Connect timed out with 503", "APPLE_TRANSIENT", "transient")]
    public void AppleReleaseFailureClassifier_ProducesStableOperatorGuidance(
        string message,
        string expectedCode,
        string expectedCategory)
    {
        var diagnostic = Assert.Single(AppleReleaseFailureClassifier.Classify(message));

        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Equal(expectedCategory, diagnostic.Category);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Action));
    }
}
