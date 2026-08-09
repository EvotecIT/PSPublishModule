namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private bool TryResumeAppleUpload(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        PowerForgeAppleAppReleaseResult result)
    {
        if (!IsUploadExecution(plan) || !plan.Automation.Resume)
            return false;
        if (string.IsNullOrWhiteSpace(app.AppStoreConnectAppId))
            return false;

        var state = ReadAppleReleaseState(plan, app);
        var platform = AssertSinglePlatformState(state, app);
        if (platform.MatchedBuild is null)
        {
            if (plan.AdoptExistingBuild)
            {
                throw new InvalidOperationException(
                    $"No exact App Store Connect build exists to adopt for '{app.Name}' " +
                    $"at {state.VersionString} ({state.BuildNumber}). Remove --apple-adopt-existing-build and upload a new archive.");
            }

            return false;
        }
        if (IsTerminalAppleBuildFailure(platform.MatchedBuild.ProcessingState))
        {
            throw new AppleBuildProcessingException(
                $"App Store Connect already contains build {state.VersionString} ({state.BuildNumber}) " +
                $"in terminal processing state '{platform.MatchedBuild.ProcessingState}' for '{app.Name}'. " +
                "Diagnose the processing failure and increment the build number before uploading again.",
                state);
        }

        var attestation = FindVerifiedAppleUploadAttestation(plan, app, state, platform.MatchedBuild);
        if (attestation is null && !plan.AdoptExistingBuild)
        {
            throw new InvalidOperationException(
                $"App Store Connect already contains build {state.VersionString} ({state.BuildNumber}) for '{app.Name}', " +
                "but no immutable local upload receipt binds that build to the current source commit and archive SHA-256. " +
                "Increment the build number and upload a new archive, or deliberately adopt the existing build with " +
                "--apple-adopt-existing-build --confirm-apple-action after verifying it outside PowerForge.");
        }

        if (plan.Automation.WaitForProcessing &&
            !string.Equals(platform.MatchedBuild.ProcessingState, "VALID", StringComparison.OrdinalIgnoreCase))
        {
            state = WaitForAppleBuild(plan, app, state);
        }

        result.RemoteState = state;
        result.ResumedExistingBuild = true;
        result.AdoptedExistingBuild = attestation is null;
        result.ResumedUploadAttestation = attestation?.Target;
        result.ResumedUploadAttestationAttemptId = attestation?.Target.UploadAttestationAttemptId ?? attestation?.Receipt.AttemptId;
        result.ArchiveSha256 = attestation?.Target.ArchiveSha256;
        result.SkippedSteps = new[] { "archive", "upload" };
        return true;
    }

    private AppleUploadAttestation? FindVerifiedAppleUploadAttestation(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        AppStoreConnectReleaseStateResult state,
        AppStoreConnectBuildInfo remoteBuild)
    {
        if (string.IsNullOrWhiteSpace(plan.SourceCommit))
            return null;

        var receipts = _appleReceiptStore.ReadAll(plan);
        foreach (var receipt in receipts)
        {
            if (receipt.PlanOnly ||
                string.IsNullOrWhiteSpace(receipt.ReceiptSha256) ||
                !string.Equals(receipt.SourceCommit, plan.SourceCommit, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = receipt.Targets.SingleOrDefault(candidate =>
                candidate.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.BundleId, app.BundleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.AppId, app.AppStoreConnectAppId, StringComparison.OrdinalIgnoreCase) &&
                candidate.Platform == app.Platform &&
                candidate.DistributionRoute == app.DistributionRoute &&
                string.Equals(candidate.Version, state.VersionString, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Build, state.BuildNumber, StringComparison.OrdinalIgnoreCase) &&
                !candidate.AdoptedExistingBuild &&
                candidate.UploadPerformed &&
                IsSha256(candidate.ArchiveSha256) &&
                !string.IsNullOrWhiteSpace(candidate.ArchivePath) &&
                IsVerifiedUploadCheckpoint(receipts, receipt, candidate));
            if (target is null)
                continue;

            if (!string.IsNullOrWhiteSpace(target.BuildId) &&
                !string.Equals(target.BuildId, remoteBuild.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(target.BuildId))
            {
                if (string.IsNullOrWhiteSpace(target.BuildUploadId))
                    continue;
                var upload = _getAppleBuildUpload(CreateAppStoreConnectCredential(plan), target.BuildUploadId!);
                if (upload is null ||
                    !string.Equals(upload.MarketingVersion, state.VersionString, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(upload.BuildNumber, state.BuildNumber, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        upload.Platform,
                        AppStoreConnectClient.ToAppStoreConnectPlatform(app.Platform),
                        StringComparison.OrdinalIgnoreCase) ||
                    IsTerminalAppleBuildFailure(upload.State))
                {
                    continue;
                }
            }

            return new AppleUploadAttestation(receipt, target);
        }

        return null;
    }

    private static bool IsVerifiedUploadCheckpoint(
        IReadOnlyCollection<PowerForgeAppleReleaseReceipt> receipts,
        PowerForgeAppleReleaseReceipt receipt,
        PowerForgeAppleReleaseTargetReceipt target)
    {
        if (string.Equals(target.UploadAttestationAttemptId, receipt.AttemptId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(target.UploadAttestationAttemptId))
            return false;

        var checkpoint = receipts.SingleOrDefault(candidate =>
            string.Equals(candidate.AttemptId, target.UploadAttestationAttemptId, StringComparison.OrdinalIgnoreCase));
        if (checkpoint is null ||
            !string.Equals(checkpoint.OperationPhase, "UploadAttested", StringComparison.Ordinal) ||
            !string.Equals(checkpoint.SourceCommit, receipt.SourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return checkpoint.Targets.Any(candidate =>
            candidate.UploadPerformed &&
            string.Equals(candidate.UploadAttestationAttemptId, checkpoint.AttemptId, StringComparison.OrdinalIgnoreCase) &&
            candidate.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.BundleId, target.BundleId, StringComparison.OrdinalIgnoreCase) &&
            candidate.Platform == target.Platform &&
            candidate.DistributionRoute == target.DistributionRoute &&
            string.Equals(candidate.ArchivePath, target.ArchivePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.ArchiveSha256, target.ArchiveSha256, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryResumeDirectAppleNotarization(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        PowerForgeAppleAppReleaseResult result)
    {
        if (!IsUploadExecution(plan) || !plan.Automation.Resume)
            return false;

        var prior = _appleReceiptStore.ReadAll(plan)
            .Where(receipt =>
                !receipt.PlanOnly &&
                !string.IsNullOrWhiteSpace(plan.SourceCommit) &&
                string.Equals(receipt.SourceCommit, plan.SourceCommit, StringComparison.OrdinalIgnoreCase))
            .SelectMany(receipt => receipt.Targets.Where(target => IsMatchingDirectReceiptTarget(target, app)))
            .FirstOrDefault(target =>
                string.Equals(target.NotarizationStatus, "Accepted", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(target.NotarizationSubmissionId) &&
                !string.IsNullOrWhiteSpace(target.DirectArtifactPath) &&
                IsSha256(target.DirectArtifactSha256) &&
                (File.Exists(target.DirectArtifactPath) || Directory.Exists(target.DirectArtifactPath)));
        if (prior is null)
            return false;

        var artifactPath = Path.GetFullPath(prior.DirectArtifactPath!);
        var stapleCompleted = !plan.DirectDistribution.Staple ||
                              (prior.Stapled == true && prior.StapleValidated == true);
        var assessmentCompleted = !plan.DirectDistribution.Assess ||
                                  prior.GatekeeperAccepted == true;
        var completed = stapleCompleted && assessmentCompleted;
        if (completed)
        {
            var artifactSha256 = AppleNotarizationService.ComputeArtifactSha256(artifactPath);
            if (!string.Equals(artifactSha256, prior.DirectArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The completed direct Apple artifact changed after release. Expected SHA-256 " +
                    $"'{prior.DirectArtifactSha256}', received '{artifactSha256}'. Archive, export, and notarize the changed artifact as a new release attempt.");
            }

            static ProcessRunResult CompletedStep(string message, string executable)
                => new(0, message, string.Empty, executable, TimeSpan.Zero, false);

            result.Notarization = new AppleNotarizationResult
            {
                ArtifactPath = artifactPath,
                ArtifactSha256 = artifactSha256,
                SubmissionPath = artifactPath,
                SubmissionId = prior.NotarizationSubmissionId,
                Status = "Accepted",
                ResumedAcceptedSubmission = true,
                Submission = CompletedStep("Reused the retained accepted notarization submission.", "xcrun"),
                Staple = plan.DirectDistribution.Staple
                    ? CompletedStep("Reused completed ticket stapling.", "xcrun")
                    : null,
                StapleValidation = plan.DirectDistribution.Staple
                    ? CompletedStep("Reused completed staple validation.", "xcrun")
                    : null,
                Assessment = plan.DirectDistribution.Assess
                    ? CompletedStep("Reused completed Gatekeeper assessment.", "spctl")
                    : null
            };
            result.ResumedAcceptedNotarization = true;
            result.SkippedSteps = MergeAppleSkippedSteps(
                result.SkippedSteps,
                new[] { "archive", "export", "notarySubmission", "staple", "stapleValidation", "gatekeeperAssessment" });
            return true;
        }

        if (string.IsNullOrWhiteSpace(prior.ErrorMessage))
            return false;

        result.Notarization = NotarizeDirectAppleExport(
            plan,
            app,
            artifactPath,
            prior.NotarizationSubmissionId,
            prior.DirectArtifactSha256,
            prior.Stapled == true);
        result.ResumedAcceptedNotarization = true;
        result.SkippedSteps = MergeAppleSkippedSteps(
            result.SkippedSteps,
            new[] { "archive", "export", "notarySubmission" });
        if (!result.Notarization.Succeeded)
            throw CreateAppleNotarizationFailure(app, result.Notarization);

        return true;
    }

    private static bool IsMatchingDirectReceiptTarget(
        PowerForgeAppleReleaseTargetReceipt target,
        PowerForgeAppleAppReleaseTargetPlan app)
        => target.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(target.BundleId, app.BundleId, StringComparison.OrdinalIgnoreCase) &&
           target.Platform == app.Platform &&
           target.DistributionRoute == AppleDistributionRoute.DirectNotarized &&
           string.Equals(target.Version, app.MarketingVersion, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(target.Build, app.BuildNumber, StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value!.Length == 64 &&
           value.All(static character => Uri.IsHexDigit(character));

    private sealed class AppleUploadAttestation
    {
        internal AppleUploadAttestation(
            PowerForgeAppleReleaseReceipt receipt,
            PowerForgeAppleReleaseTargetReceipt target)
        {
            Receipt = receipt;
            Target = target;
        }

        internal PowerForgeAppleReleaseReceipt Receipt { get; }

        internal PowerForgeAppleReleaseTargetReceipt Target { get; }
    }
}
