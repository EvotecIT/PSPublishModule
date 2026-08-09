namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
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

            var result = CreateAppleAutomationService(
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
            var accepted = Assert.Single(
                new AppleReleaseReceiptStore().ReadAll(result.AppleAppPlan!),
                receipt => receipt.OperationPhase == "NotarizationAccepted");
            Assert.Equal(sourceCommit, accepted.SourceCommit);
            var target = Assert.Single(accepted.Targets);
            Assert.Equal("accepted-before-local-crash", target.NotarizationSubmissionId);
            Assert.Equal("Accepted", target.NotarizationStatus);
            Assert.False(accepted.Success);
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
            Assert.Equal(
                AppleNotarizationService.ComputeArtifactSha256(target.DirectArtifactPath!),
                target.DirectArtifactSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
