namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
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
                        AppleSourceCommit = sourceCommit,
                        AppleAdoptExistingBuild = true,
                        AppleActionConfirmed = true
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
    [InlineData("receipt-contains-plan")]
    [InlineData("lock-under-plan")]
    [InlineData("history-under-archive-root")]
    [InlineData("archive-overlaps-receipt-journal-lock")]
    [InlineData("plan-overwrites-project")]
    [InlineData("archive-root-overlaps-export-root")]
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
                case "receipt-contains-plan":
                    automation.ReceiptPath = "build/powerforge/apple/state";
                    automation.PlanReceiptPath = "build/powerforge/apple/state/plan.json";
                    break;
                case "lock-under-plan":
                    automation.PlanReceiptPath = "build/powerforge/apple/plan";
                    automation.LockPath = "build/powerforge/apple/plan/release.lock";
                    break;
                case "history-under-archive-root":
                    automation.ReceiptHistoryPath = "build/powerforge/apple/archives/receipts";
                    break;
                case "archive-overlaps-receipt-journal-lock":
                    spec.AppleApps.ArchiveRoot = FrameworkCompatibility.GetRelativePath(
                        root,
                        Path.GetDirectoryName(AppleReleaseReceiptJournalLease.CreateLockPath(
                            Path.Combine(root, automation.ReceiptPath!)))!);
                    break;
                case "plan-overwrites-project":
                    automation.PlanReceiptPath = "CasaRay.xcodeproj/project.pbxproj";
                    break;
                case "archive-root-overlaps-export-root":
                    spec.AppleApps.ExportRoot = spec.AppleApps.ArchiveRoot;
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
                exception.Message.Contains("automation output", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("archive and export roots", StringComparison.OrdinalIgnoreCase),
                exception.Message);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePlan_RejectsCaseEquivalentNestedOutputsOnCaseInsensitiveVolume()
    {
        var root = CreateSandbox();
        try
        {
            if (FrameworkCompatibility.GetPathStringComparison(root) != StringComparison.OrdinalIgnoreCase)
                return;

            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.ReceiptPath = "build/powerforge/apple/state";
            spec.AppleApps.Automation.PlanReceiptPath = "build/powerforge/apple/STATE/plan.json";

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        PlanOnly = true,
                        AppleAction = PowerForgeAppleReleaseAction.Archive
                    }));

            Assert.Contains("automation output files", exception.Message, StringComparison.OrdinalIgnoreCase);
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
                            SubmissionSha256 = new string('b', 64),
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
            Assert.Equal(new string('b', 64), target.NotarizationSubmissionSha256);
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
            Assert.Equal(notarizationRequest.ArtifactPath, result.AppleApps.Single().Upload!.ExportArtifactPath);
            Assert.Equal(notarizationRequest.ExpectedArtifactSha256, result.AppleApps.Single().Upload!.ExportArtifactSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
