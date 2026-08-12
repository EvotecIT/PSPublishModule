namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_AppleDirectNotarizationAmbiguityBlocksAutomaticResubmission(bool pinSource)
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
            var app = Assert.Single(spec.AppleApps.Apps);
            app.Name = "CasaRay Mac";
            app.Platform = ApplePlatform.macOS;
            app.DistributionRoute = AppleDistributionRoute.DirectNotarized;
            app.AppStoreConnectAppId = null;

            var initial = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        var artifact = Directory.CreateDirectory(Path.Combine(request.ExportPath!, "CasaRay.app"));
                        File.WriteAllText(Path.Combine(artifact.FullName, "payload"), "ambiguous submission bytes");
                        return CreateSuccessfulUpload(request);
                    },
                    notarizeAppleArtifact: request =>
                    {
                        request.AmbiguousCheckpoint!(new AppleNotarizationAmbiguousCheckpoint
                        {
                            ArtifactPath = request.ArtifactPath,
                            ArtifactSha256 = AppleNotarizationService.ComputeArtifactSha256(request.ArtifactPath),
                            SubmissionPath = request.ArtifactPath + ".notarization.zip",
                            SubmissionSha256 = new string('a', 64)
                        });
                        throw new InvalidOperationException("simulated ambiguous successful notarytool response");
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = pinSource ? sourceCommit : null
                });

            Assert.False(initial.Success);
            Assert.Contains(
                new AppleReleaseReceiptStore().ReadAll(initial.AppleAppPlan!),
                receipt => receipt.OperationPhase == "NotarizationAmbiguous");

            var mutationCalls = 0;
            var resumed = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: request =>
                    {
                        mutationCalls++;
                        return CreateSuccessfulArchive(request);
                    },
                    uploadAppleApp: request =>
                    {
                        mutationCalls++;
                        return CreateSuccessfulUpload(request);
                    },
                    notarizeAppleArtifact: _ =>
                    {
                        mutationCalls++;
                        throw new InvalidOperationException("Ambiguous notarization must block a second submission.");
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload,
                    AppleSourceCommit = pinSource ? sourceCommit : null
                });

            Assert.False(resumed.Success);
            Assert.Contains("ambiguous", resumed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("must not be submitted again", resumed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, mutationCalls);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
