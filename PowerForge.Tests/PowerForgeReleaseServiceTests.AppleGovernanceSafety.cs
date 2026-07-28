using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_ApplePrepare_PreflightsEveryGovernanceTargetBeforeRemotePublication()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.5.0", "13");
            CreateXcodeProject(root, "Tactra.xcodeproj", "1.5.0", "13");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(Path.Combine(root, "casa-governance.json"),
                """{ "schemaVersion": 1, "appId": "app-casa", "accessibility": [ { "deviceFamily": "IPHONE", "supportsVoiceover": true } ] }""");
            File.WriteAllText(Path.Combine(root, "tactra-governance.json"),
                """{ "schemaVersion": 1, "appId": "app-tactra", "accessibility": [ { "deviceFamily": "IPHONE", "supportsVoiceover": true } ] }""");

            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.CheckGovernance = true;
            spec.AppleApps.GovernanceConfigPaths = ["casa-governance.json", "tactra-governance.json"];
            var casa = Assert.Single(spec.AppleApps.Apps);
            casa.AppStoreConnectAppId = "app-casa";
            spec.AppleApps.Apps =
            [
                casa,
                new AppleAppConfiguration
                {
                    Name = "Tactra iOS",
                    BundleId = "com.evotecit.tactra",
                    Platform = ApplePlatform.iOS,
                    ProjectPath = "Tactra.xcodeproj",
                    Scheme = "Tactra",
                    AppStoreConnectAppId = "app-tactra"
                }
            ];
            var prepareCalls = 0;
            var governanceCalls = 0;

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    prepareAppleDistribution: _ =>
                    {
                        prepareCalls++;
                        return new AppStoreConnectReleasePreparationResult();
                    },
                    planAppleGovernance: (_, governance) =>
                    {
                        governanceCalls++;
                        return new AppStoreConnectGovernancePlan
                        {
                            AppId = governance.AppId,
                            CheckedAtUtc = DateTimeOffset.UtcNow,
                            Changes = governance.AppId == "app-tactra"
                                ?
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
                                : Array.Empty<AppStoreConnectGovernanceChange>()
                        };
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Prepare
                });

            Assert.False(result.Success);
            Assert.Equal(2, governanceCalls);
            Assert.Equal(0, prepareCalls);
            Assert.All(result.AppleReceipt!.Targets, target => Assert.Contains("remoteActions", target.SkippedSteps));
            Assert.Contains(result.AppleReceipt.Targets, target => target.Governance?.DriftCount == 1);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_DirectNotarizationFailureRetainsTheActualFailedStepOutput()
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

            var result = CreateAppleAutomationService(
                    _ => throw new InvalidOperationException("Direct distribution must not query App Store release state."),
                    archiveAppleApp: CreateSuccessfulArchive,
                    uploadAppleApp: request =>
                    {
                        Directory.CreateDirectory(Path.Combine(request.ExportPath!, "EasyControlX Agent.app"));
                        return CreateSuccessfulUpload(request);
                    },
                    notarizeAppleArtifact: request => new AppleNotarizationResult
                    {
                        ArtifactPath = request.ArtifactPath,
                        ArtifactSha256 = "direct-artifact-sha",
                        SubmissionPath = request.ArtifactPath + ".zip",
                        SubmissionId = "notary-1",
                        Status = "Accepted",
                        Submission = new ProcessRunResult(0, "accepted", string.Empty, "xcrun", TimeSpan.Zero, false),
                        Staple = new ProcessRunResult(1, string.Empty, "stapler failed: ticket missing", "xcrun", TimeSpan.Zero, false),
                        StapleValidation = new ProcessRunResult(0, "valid", string.Empty, "xcrun", TimeSpan.Zero, false),
                        Assessment = new ProcessRunResult(0, "assessment accepted", string.Empty, "spctl", TimeSpan.Zero, false)
                    })
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Upload
                });

            Assert.False(result.Success);
            Assert.Contains("ticket stapling", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stapler failed: ticket missing", result.ErrorMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("assessment accepted", result.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
