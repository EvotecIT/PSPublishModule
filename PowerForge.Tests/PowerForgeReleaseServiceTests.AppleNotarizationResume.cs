using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
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
