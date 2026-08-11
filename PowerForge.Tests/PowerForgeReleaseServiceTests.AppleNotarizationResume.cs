using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void DirectNotarizationResume_ReusesStapleOnlyAfterDurableValidation(
        bool stapled,
        bool stapleValidated,
        bool expected)
    {
        var receipt = new PowerForgeAppleReleaseTargetReceipt
        {
            Stapled = stapled,
            StapleValidated = stapleValidated
        };

        Assert.Equal(expected, PowerForgeReleaseService.HasDurablePublishedStaple(receipt));
    }

    [Fact]
    public void DirectNotarizationResume_RequiresCheckpointArchiveIdentity()
    {
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            Name = "EasyControlX Agent",
            BundleId = "com.evotecit.easycontrolx.agent",
            Platform = ApplePlatform.macOS,
            DistributionRoute = AppleDistributionRoute.DirectNotarized,
            MarketingVersion = "1.0.0",
            BuildNumber = "4",
            ExpectedArchiveSha256 = new string('b', 64)
        };
        var receipt = new PowerForgeAppleReleaseTargetReceipt
        {
            Name = app.Name,
            BundleId = app.BundleId,
            Platform = app.Platform,
            DistributionRoute = app.DistributionRoute,
            Version = app.MarketingVersion,
            Build = app.BuildNumber,
            ArchiveSha256 = new string('a', 64)
        };

        var plan = new PowerForgeAppleReleasePlan
        {
            ProjectRoot = Directory.GetCurrentDirectory(),
            SourceCommit = "0123456789abcdef0123456789abcdef01234567"
        };
        app.ProjectPath = Path.Combine(plan.ProjectRoot, "EasyControlXAgent.xcodeproj");
        app.ArchivePath = Path.Combine(plan.ProjectRoot, "EasyControlXAgent.xcarchive");
        app.ExportPath = Path.Combine(plan.ProjectRoot, "export");
        receipt.ProjectPath = "EasyControlXAgent.xcodeproj";
        receipt.Scheme = app.Scheme;
        receipt.Configuration = app.Configuration;
        receipt.Destination = app.Destination;
        receipt.DirectExecutionSha256 = PowerForgeReleaseService.ComputeDirectExecutionSha256(plan, app);
        Assert.False(PowerForgeReleaseService.IsMatchingDirectReceiptTarget(plan, receipt, app));
        receipt.ArchiveSha256 = app.ExpectedArchiveSha256;
        Assert.True(PowerForgeReleaseService.IsMatchingDirectReceiptTarget(plan, receipt, app));
    }

    [Fact]
    public void DirectNotarizationResume_UsesPlatformCorrectProjectPathIdentity()
    {
        var plan = new PowerForgeAppleReleasePlan
        {
            ProjectRoot = Directory.GetCurrentDirectory(),
            SourceCommit = "0123456789abcdef0123456789abcdef01234567"
        };
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            Name = "CasaRay",
            BundleId = "com.evotecit.casaray",
            Platform = ApplePlatform.macOS,
            ProjectPath = Path.Combine(plan.ProjectRoot, "CasaRay.xcodeproj"),
            ArchivePath = Path.Combine(plan.ProjectRoot, "CasaRay.xcarchive"),
            ExportPath = Path.Combine(plan.ProjectRoot, "export"),
            DistributionRoute = AppleDistributionRoute.DirectNotarized,
            MarketingVersion = "1.0.0",
            BuildNumber = "1"
        };
        var receipt = new PowerForgeAppleReleaseTargetReceipt
        {
            Name = app.Name,
            BundleId = app.BundleId,
            Platform = app.Platform,
            ProjectPath = "casaray.xcodeproj",
            Scheme = app.Scheme,
            Configuration = app.Configuration,
            Destination = app.Destination,
            DistributionRoute = app.DistributionRoute,
            Version = app.MarketingVersion,
            Build = app.BuildNumber
        };
        receipt.DirectExecutionSha256 = PowerForgeReleaseService.ComputeDirectExecutionSha256(plan, app);

        var matches = PowerForgeReleaseService.IsMatchingDirectReceiptTarget(plan, receipt, app);

        Assert.Equal(Path.DirectorySeparatorChar == '\\', matches);
    }

    [Theory]
    [InlineData("team")]
    [InlineData("signing")]
    [InlineData("export")]
    [InlineData("staple")]
    [InlineData("generate")]
    [InlineData("regenerate")]
    [InlineData("xcodegen")]
    [InlineData("generation-timeout")]
    public void DirectNotarizationResume_RejectsChangedExecutionPolicy(string changedControl)
    {
        var root = Directory.GetCurrentDirectory();
        var plan = new PowerForgeAppleReleasePlan
        {
            ProjectRoot = root,
            SourceCommit = "0123456789abcdef0123456789abcdef01234567",
            SigningStyle = "automatic",
            DirectDistribution = new PowerForgeAppleDirectDistributionOptions
            {
                ExportMethod = "developer-id",
                Staple = true,
                Assess = true
            }
        };
        var app = new PowerForgeAppleAppReleaseTargetPlan
        {
            Name = "CasaRay",
            BundleId = "com.evotecit.casaray",
            Platform = ApplePlatform.macOS,
            DistributionRoute = AppleDistributionRoute.DirectNotarized,
            ProjectPath = Path.Combine(root, "CasaRay.xcodeproj"),
            ArchivePath = Path.Combine(root, "CasaRay.xcarchive"),
            ExportPath = Path.Combine(root, "export"),
            TeamId = "TEAMONE",
            MarketingVersion = "1.0.0",
            BuildNumber = "1"
        };
        var receipt = new PowerForgeAppleReleaseTargetReceipt
        {
            Name = app.Name,
            BundleId = app.BundleId,
            Platform = app.Platform,
            DistributionRoute = app.DistributionRoute,
            ProjectPath = "CasaRay.xcodeproj",
            Scheme = app.Scheme,
            Configuration = app.Configuration,
            Destination = app.Destination,
            Version = app.MarketingVersion,
            Build = app.BuildNumber,
            DirectExecutionSha256 = PowerForgeReleaseService.ComputeDirectExecutionSha256(plan, app)
        };

        switch (changedControl)
        {
            case "team":
                app.TeamId = "TEAMTWO";
                break;
            case "signing":
                plan.SigningStyle = "manual";
                break;
            case "export":
                plan.DirectDistribution.ExportMethod = "release-testing";
                break;
            case "staple":
                plan.DirectDistribution.Staple = false;
                break;
            case "generate":
                app.GenerateProjectIfMissing = true;
                break;
            case "regenerate":
                app.RegenerateProject = true;
                break;
            case "xcodegen":
                app.XcodeGenExecutable = "/reviewed/tools/xcodegen";
                break;
            case "generation-timeout":
                app.ProjectGenerationTimeoutSeconds++;
                break;
        }

        Assert.False(PowerForgeReleaseService.IsMatchingDirectReceiptTarget(plan, receipt, app));
    }

    [Fact]
    public void UploadAttestationResume_UsesPlatformCorrectProjectPathIdentity()
    {
        var matches = PowerForgeReleaseService.AppleReleasePathsEqual(
            "CasaRay.xcodeproj",
            "casaray.xcodeproj");

        Assert.Equal(Path.DirectorySeparatorChar == '\\', matches);
    }

    [Fact]
    public void Execute_AppleDirectNotarizationResume_FollowsRelativeReceiptAfterCheckoutRelocation()
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var originalRoot = CreateSandbox();
        var relocatedRoot = originalRoot + "-relocated";
        try
        {
            CreateXcodeProject(originalRoot, "EasyControlXAgent.xcodeproj", "1.0.0", "4");
            var keyPath = Path.Combine(originalRoot, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(originalRoot, keyPath);
            spec.AppleApps!.TeamId = "8ZPGZ79T7J";
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            var app = Assert.Single(spec.AppleApps.Apps);
            app.Name = "EasyControlX Agent";
            app.ProjectPath = "EasyControlXAgent.xcodeproj";
            app.Scheme = "EasyControlXAgent";
            app.Platform = ApplePlatform.macOS;
            app.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            app.AppStoreConnectAppId = null;

            var failed = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        var artifact = Directory.CreateDirectory(Path.Combine(request.ExportPath!, "EasyControlX Agent.app"));
                        File.WriteAllText(Path.Combine(artifact.FullName, "payload"), "portable accepted bytes");
                        return CreateSuccessfulUpload(request);
                    },
                    notarizeAppleArtifact: request =>
                    {
                        request.AcceptedCheckpoint!(new AppleNotarizationAcceptedCheckpoint
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath),
                            SubmissionPath = request.ArtifactPath + ".zip",
                            SubmissionSha256 = new string('a', 64),
                            SubmissionId = "portable-submission",
                            Status = "Accepted"
                        });
                        throw new InvalidOperationException("simulated process loss after acceptance");
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(originalRoot, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });
            Assert.False(failed.Success);
            var accepted = Assert.Single(
                new AppleReleaseReceiptStore().ReadAll(failed.AppleAppPlan!),
                receipt => receipt.OperationPhase == "NotarizationAccepted");
            Assert.False(Path.IsPathRooted(Assert.Single(accepted.Targets).DirectArtifactPath));

            Directory.Move(originalRoot, relocatedRoot);
            spec.AppleApps.ProjectRoot = relocatedRoot;
            spec.AppleApps.AppStoreConnectApiKeyPath = Path.Combine(relocatedRoot, "AuthKey_TEST.p8");
            AppleNotarizationRequest? resumedRequest = null;
            var resumeService = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: _ => throw new InvalidOperationException("Relocated accepted artifact must skip archive."),
                    uploadAppleApp: _ => throw new InvalidOperationException("Relocated accepted artifact must skip export."),
                    notarizeAppleArtifact: request =>
                    {
                        resumedRequest = request;
                        return new AppleNotarizationResult
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath),
                            SubmissionPath = request.ArtifactPath + ".zip",
                            SubmissionId = request.AcceptedSubmissionId!,
                            Status = "Accepted",
                            ResumedAcceptedSubmission = true,
                            Submission = new ProcessRunResult(0, "accepted", string.Empty, "xcrun", TimeSpan.Zero, false),
                            Staple = new ProcessRunResult(0, "stapled", string.Empty, "xcrun", TimeSpan.Zero, false),
                            StapleValidation = new ProcessRunResult(0, "valid", string.Empty, "xcrun", TimeSpan.Zero, false),
                            Assessment = new ProcessRunResult(0, "accepted", string.Empty, "spctl", TimeSpan.Zero, false)
                        };
                    });
            var blocked = resumeService.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(relocatedRoot, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Upload,
                AppleSourceCommit = sourceCommit
            });
            Assert.False(blocked.Success);
            Assert.Contains(
                "cannot authorize a cross-process recovery",
                Assert.Single(blocked.AppleReceipt!.Targets).ErrorMessage,
                StringComparison.OrdinalIgnoreCase);

            var resumed = resumeService.Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(relocatedRoot, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit,
                    AppleAdoptExistingBuild = true,
                    AppleActionConfirmed = true
                });

            Assert.True(resumed.Success, resumed.ErrorMessage);
            Assert.Equal("portable-submission", resumedRequest!.AcceptedSubmissionId);
            Assert.Equal(new string('a', 64), resumedRequest.AcceptedSubmissionSha256);
            Assert.StartsWith(relocatedRoot, resumedRequest.ArtifactPath, StringComparison.Ordinal);
            Assert.True(Assert.Single(resumed.AppleApps).ResumedAcceptedNotarization);
        }
        finally
        {
            TryDelete(originalRoot);
            TryDelete(relocatedRoot);
        }
    }

    [Fact]
    public void Execute_AppleDirectNotarizationResume_MissingRetainedArtifactFallsBackToFreshArchive()
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
            spec.AppleApps.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            var app = Assert.Single(spec.AppleApps.Apps);
            app.Name = "EasyControlX Agent";
            app.ProjectPath = "EasyControlXAgent.xcodeproj";
            app.Scheme = "EasyControlXAgent";
            app.Platform = ApplePlatform.macOS;
            app.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            app.AppStoreConnectAppId = null;

            var interrupted = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        var artifact = Directory.CreateDirectory(Path.Combine(request.ExportPath!, "EasyControlX Agent.app"));
                        File.WriteAllText(Path.Combine(artifact.FullName, "payload"), "accepted bytes that will be lost");
                        return CreateSuccessfulUpload(request);
                    },
                    notarizeAppleArtifact: request =>
                    {
                        request.AcceptedCheckpoint!(new AppleNotarizationAcceptedCheckpoint
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath),
                            SubmissionPath = request.ArtifactPath + ".zip",
                            SubmissionSha256 = new string('a', 64),
                            SubmissionId = "missing-artifact-submission",
                            Status = "Accepted"
                        });
                        throw new InvalidOperationException("simulated process loss after acceptance");
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });
            Assert.False(interrupted.Success);
            var retainedArtifact = Path.Combine(
                Assert.Single(interrupted.AppleAppPlan!.Apps).ExportPath,
                "EasyControlX Agent.app");
            Assert.True(Directory.Exists(retainedArtifact));
            Directory.Delete(retainedArtifact, recursive: true);

            var archiveCalls = 0;
            var exportCalls = 0;
            AppleNotarizationRequest? freshNotarization = null;
            var resumed = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: request =>
                    {
                        archiveCalls++;
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: request =>
                    {
                        exportCalls++;
                        var artifact = Directory.CreateDirectory(Path.Combine(request.ExportPath!, "EasyControlX Agent.app"));
                        File.WriteAllText(Path.Combine(artifact.FullName, "payload"), "fresh exported bytes");
                        return CreateSuccessfulUpload(request);
                    },
                    notarizeAppleArtifact: request =>
                    {
                        freshNotarization = request;
                        return new AppleNotarizationResult
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath),
                            SubmissionPath = request.ArtifactPath + ".zip",
                            SubmissionId = "fresh-submission",
                            Status = "Accepted",
                            Submission = new ProcessRunResult(0, "accepted", string.Empty, "xcrun", TimeSpan.Zero, false),
                            Staple = new ProcessRunResult(0, "stapled", string.Empty, "xcrun", TimeSpan.Zero, false),
                            StapleValidation = new ProcessRunResult(0, "valid", string.Empty, "xcrun", TimeSpan.Zero, false),
                            Assessment = new ProcessRunResult(0, "accepted", string.Empty, "spctl", TimeSpan.Zero, false)
                        };
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });

            Assert.True(resumed.Success, resumed.ErrorMessage);
            Assert.Equal(1, archiveCalls);
            Assert.Equal(1, exportCalls);
            Assert.NotNull(freshNotarization);
            Assert.Null(freshNotarization!.AcceptedSubmissionId);
            Assert.False(Assert.Single(resumed.AppleApps).ResumedAcceptedNotarization);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleDirectNotarizationResume_RejectsChainedReceiptOutsideCurrentExportRoot()
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
            var configured = Assert.Single(spec.AppleApps.Apps);
            configured.Name = "EasyControlX Agent";
            configured.ProjectPath = "EasyControlXAgent.xcodeproj";
            configured.Scheme = "EasyControlXAgent";
            configured.Platform = ApplePlatform.macOS;
            configured.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            configured.AppStoreConnectAppId = null;
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."));
            var planned = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = sourceCommit
                });
            var plan = Assert.IsType<PowerForgeAppleReleasePlan>(planned.AppleAppPlan);
            var app = Assert.Single(plan.Apps);
            var outside = Directory.CreateDirectory(Path.Combine(root, "retained", "EasyControlX Agent.app"));
            File.WriteAllText(Path.Combine(outside.FullName, "payload"), "accepted elsewhere");
            new AppleReleaseReceiptStore().WriteAttempt(plan, new PowerForgeAppleReleaseReceipt
            {
                Action = PowerForgeAppleReleaseAction.Upload,
                SourceCommit = sourceCommit,
                OperationPhase = "NotarizationAccepted",
                Success = false,
                Targets =
                [
                    new PowerForgeAppleReleaseTargetReceipt
                    {
                        Name = app.Name,
                        BundleId = app.BundleId,
                        Platform = app.Platform,
                        DistributionRoute = app.DistributionRoute,
                        DirectArtifactPath = outside.FullName,
                        DirectArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(outside.FullName),
                        NotarizationSubmissionId = "copied-submission",
                        NotarizationStatus = "Accepted"
                    }
                ]
            });
            var archiveCalls = 0;
            service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                archiveAppleApp: request =>
                {
                    archiveCalls++;
                    return CreateSuccessfulArchive(request);
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
            Assert.Equal(0, archiveCalls);
            Assert.Contains("outside its current export root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleDirectNotarizationResume_RejectsReceiptFromDifferentSourceCommit()
    {
        const string priorSourceCommit = "0123456789abcdef0123456789abcdef01234567";
        const string currentSourceCommit = "89abcdef0123456789abcdef0123456789abcdef";
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

            var retainedArtifact = Directory.CreateDirectory(Path.Combine(root, "retained", "EasyControlX Agent.app"));
            File.WriteAllText(Path.Combine(retainedArtifact.FullName, "payload"), "prior-source");
            var receiptPath = Path.Combine(root, "build", "powerforge", "apple", "release-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(new PowerForgeAppleReleaseReceipt
            {
                SchemaVersion = 3,
                Action = PowerForgeAppleReleaseAction.Upload,
                SourceCommit = priorSourceCommit,
                Success = false,
                ErrorMessage = "Ticket stapling failed.",
                Targets =
                [
                    new PowerForgeAppleReleaseTargetReceipt
                    {
                        Name = app.Name,
                        BundleId = app.BundleId,
                        Platform = app.Platform,
                        DistributionRoute = AppleDistributionRoute.DirectNotarized,
                        Version = "1.0.0",
                        Build = "4",
                        DirectArtifactPath = retainedArtifact.FullName,
                        DirectArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(retainedArtifact.FullName),
                        NotarizationSubmissionId = "prior-submission",
                        NotarizationStatus = "Accepted",
                        Stapled = false,
                        ErrorMessage = "Ticket stapling failed."
                    }
                ]
            }));

            var archiveCalls = 0;
            var exportCalls = 0;
            AppleNotarizationRequest? notarizationRequest = null;
            var result = CreateAppleAutomationService(
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
                        notarizationRequest = request;
                        return new AppleNotarizationResult
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath),
                            SubmissionPath = request.ArtifactPath + ".zip",
                            SubmissionId = "current-submission",
                            Status = "Accepted",
                            Submission = new ProcessRunResult(0, "accepted", string.Empty, "xcrun", TimeSpan.Zero, false),
                            Staple = new ProcessRunResult(0, "stapled", string.Empty, "xcrun", TimeSpan.Zero, false),
                            StapleValidation = new ProcessRunResult(0, "valid", string.Empty, "xcrun", TimeSpan.Zero, false),
                            Assessment = new ProcessRunResult(0, "accepted", string.Empty, "spctl", TimeSpan.Zero, false)
                        };
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleSourceCommit = currentSourceCommit,
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            Assert.True(result.Success);
            Assert.Equal(1, archiveCalls);
            Assert.Equal(1, exportCalls);
            Assert.NotNull(notarizationRequest);
            Assert.Null(notarizationRequest!.AcceptedSubmissionId);
            Assert.Equal(currentSourceCommit, result.AppleReceipt!.SourceCommit);
            Assert.False(Assert.Single(result.AppleReceipt.Targets).ResumedAcceptedNotarization);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
