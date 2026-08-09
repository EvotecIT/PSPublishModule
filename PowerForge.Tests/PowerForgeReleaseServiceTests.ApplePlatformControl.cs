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
    [InlineData(AppleTestFlightPolicy.Automatic, false)]
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
    public void Execute_AppleTargetRejectsProjectPathOutsideProjectRoot()
    {
        var root = CreateSandbox();
        var externalRoot = CreateSandbox();
        try
        {
            CreateXcodeProject(externalRoot, "Outside.xcodeproj", "1.0.0", "1");
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
                                    Name = "Outside",
                                    ProjectPath = Path.Combine(externalRoot, "Outside.xcodeproj"),
                                    Scheme = "Outside"
                                }
                            }
                        }
                    },
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Archive
                    }));

            Assert.Contains("ProjectPath must remain inside AppleApps.ProjectRoot", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
            TryDelete(externalRoot);
        }
    }

    [Fact]
    public void Execute_AppleApps_RequiresExplicitExternalPolicyForBetaReviewSubmission()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var keyPath = Path.Combine(root, "AuthKey_ABC123DEFG.p8");
            File.WriteAllText(keyPath, "private-key");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    new PowerForgeReleaseSpec
                    {
                        AppleApps = new PowerForgeAppleReleaseOptions
                        {
                            ProjectRoot = ".",
                            SubmitTestFlightBetaReview = true,
                            AppStoreConnectApiKeyPath = keyPath,
                            AppStoreConnectApiKeyId = "ABC123DEFG",
                            AppStoreConnectApiIssuerId = "issuer-id",
                            Apps = new[]
                            {
                                new AppleAppConfiguration
                                {
                                    Name = "Tactra",
                                    ProjectPath = "Tactra.xcodeproj",
                                    Scheme = "Tactra",
                                    Platform = ApplePlatform.iOS,
                                    TestFlightPolicy = AppleTestFlightPolicy.Automatic,
                                    AppStoreConnectAppId = "app-1"
                                }
                            }
                        }
                    },
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.SubmitTestFlightReview,
                        PlanOnly = true
                    }));

            Assert.Contains("TestFlightPolicy=External", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void AppleReleaseDoctor_FindsControlPlaneFailuresBeforeSubmission()
    {
        var root = CreateSandbox();
        CreateXcodeProject(root, "CasaRay.xcodeproj");
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            Name = "CasaRay",
            BundleId = "com.evotec.casarray",
            AppStoreConnectAppId = "app-1",
            DistributionRoute = AppleDistributionRoute.AppStore,
            ProjectPath = Path.Combine(root, "CasaRay.xcodeproj")
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
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("Info.plist", "<key>ITSAppUsesNonExemptEncryption</key><false/>", false)]
    [InlineData("project.yml", "ITSAppUsesNonExemptEncryption: false", false)]
    [InlineData("project.pbxproj", "INFOPLIST_KEY_ITSAppUsesNonExemptEncryption = NO;", false)]
    [InlineData("Info.plist", "<key>ITSAppUsesNonExemptEncryption</key><true/>", true)]
    public void AppleReleaseDoctor_RecognizesExplicitExemptEncryptionEvidence(
        string fileName,
        string evidence,
        bool expectsWarning)
    {
        var root = CreateSandbox();
        try
        {
            var evidencePath = Path.Combine(root, fileName);
            File.WriteAllText(evidencePath, evidence);
            var app = new PowerForgeAppleAppReleaseTargetPlan
            {
                Name = "Store App",
                BundleId = "com.evotecit.storeapp",
                AppStoreConnectAppId = "app-1",
                DistributionRoute = AppleDistributionRoute.AppStore,
                ProjectPath = evidencePath
            };

            var diagnostics = AppleReleaseDoctor.Evaluate(
                new PowerForgeAppleReleasePlan { Apps = new[] { app } },
                app,
                new AppStoreConnectControlPlaneState { AppId = "app-1" });

            Assert.Equal(
                expectsWarning,
                diagnostics.Any(item => item.Code == "APPLE_ENCRYPTION_ATTESTATION_UNTRACKED"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void AppleReleaseDoctor_DiagnosesMissingDirectNotarizationAuthentication()
    {
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            Name = "EasyControlX Agent",
            BundleId = "com.evotecit.easycontrolx.agent",
            TeamId = "8ZPGZ79T7J",
            DistributionRoute = AppleDistributionRoute.DirectNotarized
        };
        var diagnostics = AppleReleaseDoctor.Evaluate(
            new PowerForgeAppleReleasePlan { Apps = new[] { app } },
            app);

        Assert.Contains(diagnostics, item => item.Code == "APPLE_NOTARIZATION_AUTH_MISSING");
    }

    [Fact]
    public void AppleReleaseDoctor_DoesNotRequirePublicMetadataForTestFlightOnlyTarget()
    {
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            Name = "Internal Preview",
            BundleId = "com.evotecit.preview",
            AppStoreConnectAppId = "app-1",
            DistributionRoute = AppleDistributionRoute.TestFlightOnly,
            TestFlightPolicy = AppleTestFlightPolicy.Internal
        };
        var diagnostics = AppleReleaseDoctor.Evaluate(
            new PowerForgeAppleReleasePlan { Apps = new[] { app } },
            app,
            new AppStoreConnectControlPlaneState());

        Assert.DoesNotContain(diagnostics, item => item.Code == "APPLE_METADATA_UNMANAGED");
        Assert.DoesNotContain(diagnostics, item => item.Code == "APPLE_APP_INFO_UNMANAGED");
        Assert.DoesNotContain(diagnostics, item => item.Code == "APPLE_ACCESSIBILITY_UNDECLARED");
        Assert.DoesNotContain(diagnostics, item => item.Code == "APPLE_AGE_RATING_MISSING");
    }

    [Fact]
    public void Execute_NamedAppleMutationFailsWhenNoSelectedTargetCanExecuteIt()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.0.0", "1");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = Assert.Single(spec.AppleApps!.Apps);
            app.DistributionRoute = AppleDistributionRoute.TestFlightOnly;
            app.TestFlightPolicy = AppleTestFlightPolicy.Internal;

            var result = CreateAppleAutomationService(_ => throw new InvalidOperationException("Remote state must not be read."))
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.SubmitAppReview,
                        AppleActionConfirmed = true
                    });

            Assert.False(result.Success);
            Assert.Contains("cannot execute for any selected target", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.All(result.AppleApps, target => Assert.False(target.Success));
        }
        finally
        {
            TryDelete(root);
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
            Assert.Equal(4, receipt.SchemaVersion);
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
    public void Execute_AppleDoctor_RetainsGovernanceDriftAndBlocksBeforePublication()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var governancePath = Path.Combine(root, "governance.json");
            File.WriteAllText(governancePath,
                """{ "schemaVersion": 1, "appId": "6778025328", "accessibility": [ { "deviceFamily": "IPHONE", "supportsVoiceover": true } ] }""");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.GovernanceConfigPath = "governance.json";

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    planAppleGovernance: (_, governance) => new AppStoreConnectGovernancePlan
                    {
                        AppId = governance.AppId,
                        CheckedAtUtc = DateTimeOffset.UtcNow,
                        Changes =
                        [
                            new AppStoreConnectGovernanceChange
                            {
                                Section = "Accessibility",
                                ResourceType = "AccessibilityDeclaration",
                                Key = "IPHONE",
                                Action = AppStoreConnectGovernanceChangeAction.Update,
                                Summary = "Update reviewed accessibility facts."
                            }
                        ]
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Doctor
                });

            Assert.False(result.Success);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.NotNull(target.Governance);
            Assert.Equal(1, target.Governance!.DriftCount);
            Assert.Contains(target.Diagnostics, diagnostic => diagnostic.Code == "APPLE_GOVERNANCE_DRIFT");
            Assert.Contains(target.NextActions, action => action.Contains("apple-governance apply", StringComparison.OrdinalIgnoreCase));
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
            Directory.CreateDirectory(Path.Combine(root, "EasyControlXAgent.xcworkspace"));
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.TeamId = "8ZPGZ79T7J";
            spec.AppleApps.AllowProvisioningUpdates = false;
            spec.AppleApps.DirectDistribution.KeychainProfile = null;
            var app = Assert.Single(spec.AppleApps.Apps);
            app.Name = "EasyControlX Agent";
            app.ProjectPath = "EasyControlXAgent.xcworkspace";
            app.Scheme = "EasyControlXAgent";
            app.Platform = ApplePlatform.macOS;
            app.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            app.AppStoreConnectAppId = null;
            app.MarketingVersion = "1.0.0";
            app.BuildNumber = "4";
            app.BuildNumberPolicy = AppleBuildNumberPolicy.KeepExisting;
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
            Assert.Equal("1.0.0", target.Version);
            Assert.Equal("4", target.Build);
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
                        ArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath),
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
                        AppleSourceCommit = sourceCommit,
                        AppleAction = PowerForgeAppleReleaseAction.Upload
                    });

            Assert.False(result.Success);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal("Accepted", target.NotarizationStatus);
            Assert.Equal("accepted-then-staple-failed", target.NotarizationSubmissionId);
            Assert.False(target.Stapled);
            Assert.Contains("ticket stapling", target.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(sourceCommit, result.AppleReceipt.SourceCommit);

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
                            ArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath),
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
                        AppleSourceCommit = sourceCommit,
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
                    AppleSourceCommit = sourceCommit,
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            Assert.True(resumed.Success);
            Assert.Null(resumedRequest);
            var resumedTarget = Assert.Single(resumed.AppleReceipt!.Targets);
            Assert.True(resumedTarget.ResumedAcceptedNotarization);
            Assert.Contains("archive", resumedTarget.SkippedSteps);
            Assert.Contains("export", resumedTarget.SkippedSteps);
            Assert.Contains("notarySubmission", resumedTarget.SkippedSteps);
            Assert.Contains("staple", resumedTarget.SkippedSteps);
            Assert.Contains("stapleValidation", resumedTarget.SkippedSteps);
            Assert.Contains("gatekeeperAssessment", resumedTarget.SkippedSteps);

            // Simulate another target failing after this target completed. Aggregate failure
            // must retain and reuse the fully verified direct target.
            var aggregateFailureReceipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(resumed.AppleReceipt);
            aggregateFailureReceipt.AttemptId = null;
            aggregateFailureReceipt.CheckedAt = default;
            aggregateFailureReceipt.Success = false;
            aggregateFailureReceipt.ErrorMessage = "Another release target failed after notarization completed.";
            aggregateFailureReceipt.ReceiptSha256 = null;
            aggregateFailureReceipt.PreviousReceiptSha256 = null;
            new AppleReleaseReceiptStore().WriteAttempt(resumed.AppleAppPlan!, aggregateFailureReceipt);

            var archiveCalls = 0;
            var exportCalls = 0;
            AppleNotarizationRequest? nextReleaseRequest = null;
            var nextReleaseService = CreateAppleAutomationService(
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
                    });
            var nextRelease = nextReleaseService.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleSourceCommit = sourceCommit,
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            Assert.True(nextRelease.Success);
            Assert.Equal(0, archiveCalls);
            Assert.Equal(0, exportCalls);
            Assert.Null(nextReleaseRequest);
            var retainedTarget = Assert.Single(nextRelease.AppleReceipt!.Targets);
            Assert.True(retainedTarget.ResumedAcceptedNotarization);
            Assert.True(retainedTarget.Stapled);
            Assert.True(retainedTarget.StapleValidated);
            Assert.True(retainedTarget.GatekeeperAccepted);
            Assert.Contains("gatekeeperAssessment", retainedTarget.SkippedSteps);

            // Disabled post-notarization checks are complete by policy even though
            // their receipt flags remain null. A mixed-target retry must still reuse
            // the retained artifact instead of submitting it again.
            spec.AppleApps.DirectDistribution.Staple = false;
            spec.AppleApps.DirectDistribution.Assess = false;
            var disabledChecksReceipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(nextRelease.AppleReceipt);
            disabledChecksReceipt.AttemptId = null;
            disabledChecksReceipt.CheckedAt = default;
            disabledChecksReceipt.Success = false;
            disabledChecksReceipt.ErrorMessage = "Another release target failed with post-notarization checks disabled.";
            disabledChecksReceipt.ReceiptSha256 = null;
            disabledChecksReceipt.PreviousReceiptSha256 = null;
            var disabledChecksTargetEvidence = Assert.Single(disabledChecksReceipt.Targets);
            disabledChecksTargetEvidence.Stapled = null;
            disabledChecksTargetEvidence.StapleValidated = null;
            disabledChecksTargetEvidence.GatekeeperAccepted = null;
            new AppleReleaseReceiptStore().WriteAttempt(nextRelease.AppleAppPlan!, disabledChecksReceipt);
            archiveCalls = 0;
            exportCalls = 0;
            nextReleaseRequest = null;

            var disabledChecksRetry = nextReleaseService.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleSourceCommit = sourceCommit,
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            Assert.True(disabledChecksRetry.Success);
            Assert.Equal(0, archiveCalls);
            Assert.Equal(0, exportCalls);
            Assert.Null(nextReleaseRequest);
            var disabledChecksTarget = Assert.Single(disabledChecksRetry.AppleReceipt!.Targets);
            Assert.True(disabledChecksTarget.ResumedAcceptedNotarization);
            Assert.Null(disabledChecksTarget.Stapled);
            Assert.Null(disabledChecksTarget.StapleValidated);
            Assert.Null(disabledChecksTarget.GatekeeperAccepted);

            var retainedReceipt = Assert.IsType<PowerForgeAppleReleaseReceipt>(disabledChecksRetry.AppleReceipt);
            retainedReceipt.AttemptId = null;
            retainedReceipt.CheckedAt = default;
            retainedReceipt.Success = false;
            retainedReceipt.ErrorMessage = "Another release target failed after the artifact was retained.";
            retainedReceipt.ReceiptSha256 = null;
            retainedReceipt.PreviousReceiptSha256 = null;
            new AppleReleaseReceiptStore().WriteAttempt(disabledChecksRetry.AppleAppPlan!, retainedReceipt);
            File.WriteAllText(Path.Combine(retainedTarget.DirectArtifactPath!, "changed-after-release.txt"), "changed");
            var changedArtifact = nextReleaseService.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleSourceCommit = sourceCommit,
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });
            Assert.False(changedArtifact.Success);
            Assert.Contains(
                "changed after release",
                Assert.Single(changedArtifact.AppleReceipt!.Targets).ErrorMessage,
                StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void AppleReleaseFailureClassifier_DoesNotTreatItmsNumbersAsHttpFailures()
    {
        var diagnostics = AppleReleaseFailureClassifier.Classify("Asset validation failed ITMS-90535");

        Assert.Contains(diagnostics, item => item.Code == "APPLE_ASSET_VALIDATION");
        Assert.DoesNotContain(diagnostics, item => item.Code == "APPLE_TRANSIENT");
    }

    [Theory]
    [InlineData("Asset validation failed for build 403")]
    [InlineData("Asset validation failed ITMS-90401")]
    public void AppleReleaseFailureClassifier_DoesNotTreatUnscopedNumbersAsAuthenticationFailures(string message)
    {
        var diagnostics = AppleReleaseFailureClassifier.Classify(message);

        Assert.DoesNotContain(diagnostics, item => item.Code == "APPLE_AUTH");
    }

    [Theory]
    [InlineData("HTTP status 401")]
    [InlineData("Response: 403")]
    [InlineData("Unauthorized App Store Connect request")]
    public void AppleReleaseFailureClassifier_RecognizesContextualAuthenticationFailures(string message)
    {
        Assert.Contains(
            AppleReleaseFailureClassifier.Classify(message),
            item => item.Code == "APPLE_AUTH");
    }
}
