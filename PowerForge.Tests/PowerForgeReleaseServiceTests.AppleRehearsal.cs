namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void ReleaseProgressPhase_PreservesPublishedNumericValues()
    {
        Assert.Equal(0, (int)PowerForgeReleaseProgressPhase.Versioning);
        Assert.Equal(1, (int)PowerForgeReleaseProgressPhase.Module);
        Assert.Equal(2, (int)PowerForgeReleaseProgressPhase.Packages);
        Assert.Equal(3, (int)PowerForgeReleaseProgressPhase.Tools);
        Assert.Equal(4, (int)PowerForgeReleaseProgressPhase.GitHub);
        Assert.Equal(5, (int)PowerForgeReleaseProgressPhase.VirusTotal);
        Assert.Equal(6, (int)PowerForgeReleaseProgressPhase.AppleApps);
    }

    [Theory]
    [InlineData("CasaRay.ipa", "file-content")]
    [InlineData("CasaRay.app", "filesystem-identity-v2")]
    public void Execute_AppleRehearse_archives_and_exports_without_remote_mutation(
        string artifactName,
        string expectedHashKind)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.7", "16");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.Automation.MinimumFreeSpaceGB = 0;
            spec.AppleApps.Automation.CleanupBeforeArchive = false;
            var exportRequests = new List<AppleAppArchiveUploadRequest>();
            var progress = new AppleRehearsalProgress();
            var service = CreateAppleAutomationService(
                _ => throw new InvalidOperationException("Rehearse must not query App Store Connect."),
                archiveAppleApp: CreateSuccessfulArchive,
                uploadAppleApp: request =>
                {
                    exportRequests.Add(request);
                    Assert.Equal("export", request.Destination);
                    Assert.Equal("app-store-connect", request.Method);
                    Assert.Null(request.RemoteMutationStarted);
                    Directory.CreateDirectory(request.ExportPath!);
                    var artifactPath = Path.Combine(request.ExportPath!, artifactName);
                    if (artifactName.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(artifactPath);
                        File.WriteAllText(Path.Combine(artifactPath, "CasaRay"), "signed rehearsal artifact");
                    }
                    else
                    {
                        File.WriteAllText(artifactPath, "signed rehearsal artifact");
                    }
                    return new AppleAppArchiveUploadResult
                    {
                        ArchivePath = request.ArchivePath,
                        ExportPath = request.ExportPath!,
                        ExportOptionsPlistPath = Path.Combine(request.ExportPath!, "ExportOptions.plist"),
                        ExportArtifactPath = artifactPath,
                        ExportArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(artifactPath),
                        ProcessResult = new ProcessRunResult(
                            0,
                            "export-ok",
                            string.Empty,
                            "xcodebuild",
                            TimeSpan.FromSeconds(1),
                            false)
                    };
                },
                notarizeAppleArtifact: _ => throw new InvalidOperationException("Rehearse must not submit for notarization."));

            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Rehearse,
                Progress = progress
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(result.AppleAppPlan!.Archive);
            Assert.True(result.AppleAppPlan.Rehearse);
            Assert.False(result.AppleAppPlan.Upload);
            Assert.Single(exportRequests);
            var target = Assert.Single(Assert.IsType<PowerForgeAppleReleaseReceipt>(result.AppleReceipt).Targets);
            Assert.True(target.ArchiveCreated);
            Assert.True(target.ExportRehearsed);
            Assert.False(target.UploadPerformed);
            Assert.EndsWith(artifactName, target.RehearsalArtifactPath, StringComparison.Ordinal);
            var rehearsalArtifactPath = Path.Combine(
                root,
                target.RehearsalArtifactPath!.Replace('/', Path.DirectorySeparatorChar));
            var expectedArtifactSha256 = File.Exists(rehearsalArtifactPath)
                ? AppleNotarizationService.ComputeFileSha256(rehearsalArtifactPath)
                : AppleNotarizationService.ComputeArtifactSha256(rehearsalArtifactPath);
            Assert.Equal(
                expectedArtifactSha256,
                target.RehearsalArtifactSha256);
            Assert.Equal(expectedHashKind, target.RehearsalArtifactSha256Kind);
            Assert.Null(target.UploadAttestationAttemptId);
            Assert.Null(target.UploadExecutionSha256);
            Assert.Contains("phase:start:AppleApps", progress.Events);
            Assert.Contains(progress.Events, entry => entry.Contains("item:Started:Preparing exact-source archive", StringComparison.Ordinal));
            Assert.Contains(progress.Events, entry => entry.Contains("item:Completed:Apple target completed", StringComparison.Ordinal));
            Assert.Contains("phase:complete:AppleApps", progress.Events);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ValidateAppleRehearsalArtifactEvidence_rejects_post_publication_mutation()
    {
        var root = CreateSandbox();
        try
        {
            var artifactPath = Path.Combine(root, "CasaRay.ipa");
            File.WriteAllText(artifactPath, "producer-bound bytes");
            var upload = new AppleAppArchiveUploadResult
            {
                ExportArtifactPath = artifactPath,
                RehearsalArtifactSha256 = AppleNotarizationService.ComputeFileSha256(artifactPath),
                RehearsalArtifactSha256Kind = "file-content"
            };
            File.WriteAllText(artifactPath, "mutated after publication");

            var error = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.ValidateAppleRehearsalArtifactEvidence(upload));

            Assert.Contains("changed before its receipt", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveAppleRehearsalArtifactEvidence_preserves_an_earlier_export_failure()
    {
        var upload = new AppleAppArchiveUploadResult
        {
            ProcessResult = new ProcessRunResult(
                0,
                "export-ok",
                string.Empty,
                "xcodebuild",
                TimeSpan.FromSeconds(1),
                false)
        };

        var evidence = PowerForgeReleaseService.ResolveAppleRehearsalArtifactEvidence(
            rehearse: true,
            targetSucceeded: false,
            upload);

        Assert.Null(evidence);
    }

    private sealed class AppleRehearsalProgress : IPowerForgeReleaseProgressReporterV2
    {
        internal List<string> Events { get; } = new();

        public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null)
            => Events.Add($"phase:start:{phase}");

        public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null)
            => Events.Add($"phase:complete:{phase}");

        public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null)
            => Events.Add($"phase:fail:{phase}");

        public void ItemsPlanned(PowerForgeReleaseProgressPhase phase, IReadOnlyList<PowerForgeReleaseProgressItem> items)
            => Events.Add($"items:{phase}:{items.Count}");

        public void ItemUpdated(
            PowerForgeReleaseProgressItem item,
            PowerForgeReleaseProgressItemState state,
            string? detail = null)
            => Events.Add($"item:{state}:{detail}");
    }
}
