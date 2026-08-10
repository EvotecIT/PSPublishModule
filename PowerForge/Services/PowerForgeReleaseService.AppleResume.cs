namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private string[] GetProtectedAppleRecoveryArtifactPaths(PowerForgeAppleReleasePlan plan)
    {
        if (!plan.Automation.WriteReceipt)
            return Array.Empty<string>();

        var protectedPaths = new List<string>();
        var seenSubmissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var receipt in _appleReceiptStore.ReadAll(plan))
        {
            foreach (var target in receipt.Targets)
            {
                if (target.DistributionRoute != AppleDistributionRoute.DirectNotarized ||
                    string.IsNullOrWhiteSpace(target.NotarizationSubmissionId))
                {
                    continue;
                }

                var key = string.Join(
                    "|",
                    target.Name,
                    target.BundleId,
                    target.Platform,
                    target.NotarizationSubmissionId);
                if (!seenSubmissions.Add(key))
                    continue;

                var accepted = string.Equals(target.NotarizationStatus, "Accepted", StringComparison.OrdinalIgnoreCase);
                var stapleCompleted = !plan.DirectDistribution.Staple ||
                                      (target.Stapled == true && target.StapleValidated == true);
                var assessmentCompleted = !plan.DirectDistribution.Assess ||
                                          target.GatekeeperAccepted == true;
                if (!accepted || (stapleCompleted && assessmentCompleted) ||
                    string.IsNullOrWhiteSpace(target.DirectArtifactPath))
                {
                    continue;
                }

                var app = plan.Apps.SingleOrDefault(candidate =>
                    candidate.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.BundleId, target.BundleId, StringComparison.OrdinalIgnoreCase) &&
                    candidate.Platform == target.Platform &&
                    candidate.DistributionRoute == target.DistributionRoute);
                if (app is null || receipt.SchemaVersion < 4 || string.IsNullOrWhiteSpace(receipt.ReceiptSha256))
                    continue;
                var artifactPath = ValidateDirectRecoveryArtifactPath(plan, app, target.DirectArtifactPath!);
                if (File.Exists(artifactPath) || Directory.Exists(artifactPath))
                    protectedPaths.Add(artifactPath);
            }
        }

        return protectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

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
        var attestation = FindVerifiedAppleUploadAttestation(plan, app, state, platform.MatchedBuild);
        if (platform.MatchedBuild is null)
        {
            if (attestation is not null)
            {
                EnsureExplicitAppleRecoveryAdoption(plan, app, "an attested upload that is not yet visible as a build");
                if (plan.Automation.WaitForProcessing)
                    state = WaitForAppleBuild(plan, app, state, attestation.Target.BuildUploadId);
                else
                    EnsureAppleBuildUploadIsNotTerminal(plan, app, state, attestation.Target.BuildUploadId!);
                PopulateResumedAppleUpload(result, state, attestation, adopted: false);
                return true;
            }
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

        if (!plan.AdoptExistingBuild)
        {
            var evidence = attestation is null
                ? "no immutable local upload receipt can independently authorize it"
                : "the local upload receipt is continuity evidence, not authority against another process running as this account";
            throw new InvalidOperationException(
                $"App Store Connect already contains build {state.VersionString} ({state.BuildNumber}) for '{app.Name}', " +
                $"but {evidence}. Increment the build number and upload a new archive, or deliberately adopt the existing build with " +
                "--apple-adopt-existing-build --confirm-apple-action after verifying it outside PowerForge.");
        }

        if (plan.Automation.WaitForProcessing &&
            !string.Equals(platform.MatchedBuild.ProcessingState, "VALID", StringComparison.OrdinalIgnoreCase))
        {
            state = WaitForAppleBuild(plan, app, state);
        }

        PopulateResumedAppleUpload(result, state, attestation, adopted: attestation is null);
        return true;
    }

    private static void PopulateResumedAppleUpload(
        PowerForgeAppleAppReleaseResult result,
        AppStoreConnectReleaseStateResult state,
        AppleUploadAttestation? attestation,
        bool adopted)
    {
        result.RemoteState = state;
        result.ResumedExistingBuild = true;
        result.AdoptedExistingBuild = adopted;
        result.ResumedUploadAttestation = attestation?.Target;
        result.ResumedUploadAttestationAttemptId = attestation?.Target.UploadAttestationAttemptId ?? attestation?.Receipt.AttemptId;
        result.ArchiveSha256 = attestation?.Target.ArchiveSha256;
        result.SkippedSteps = new[] { "archive", "upload" };
    }

    private static void EnsureExplicitAppleRecoveryAdoption(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        string recoveredOperation)
    {
        if (plan.AdoptExistingBuild)
            return;

        throw new InvalidOperationException(
            $"Apple recovery for '{app.Name}' found {recoveredOperation}, but local receipt files cannot authorize a cross-process recovery. " +
            "Verify the remote operation and exact source/archive evidence, then rerun with --apple-adopt-existing-build " +
            "--confirm-apple-action, or disable resume and start a new version/build.");
    }

    private void EnsureAppleBuildUploadIsNotTerminal(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        AppStoreConnectReleaseStateResult state,
        string buildUploadId)
    {
        var upload = _getAppleBuildUpload(CreateAppStoreConnectCredential(plan), buildUploadId);
        if (upload is null || !IsTerminalAppleBuildFailure(upload.State))
            return;

        var issues = upload.Errors
            .Select(static issue => FormatAppleBuildUploadIssue(issue))
            .Where(static issue => !string.IsNullOrWhiteSpace(issue))
            .ToArray();
        var issueDetail = issues.Length == 0 ? string.Empty : $" {string.Join(" ", issues)}";
        throw new AppleBuildProcessingException(
            $"App Store Connect rejected uploaded build {state.VersionString} ({state.BuildNumber}) " +
            $"for '{app.Name}' in build-upload state '{upload.State}'.{issueDetail}",
            state);
    }

    private AppleUploadAttestation? FindVerifiedAppleUploadAttestation(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        AppStoreConnectReleaseStateResult state,
        AppStoreConnectBuildInfo? remoteBuild)
    {
        if (string.IsNullOrWhiteSpace(plan.SourceCommit))
            return null;

        var receipts = _appleReceiptStore.ReadAll(plan);
        foreach (var receipt in receipts)
        {
            var target = FindMatchingAppleUploadAttestationTarget(
                receipts,
                receipt,
                plan,
                app,
                state.VersionString,
                state.BuildNumber);
            if (target is null)
                continue;

            if (remoteBuild is null)
            {
                if (string.IsNullOrWhiteSpace(target.BuildUploadId))
                    continue;
                return new AppleUploadAttestation(receipt, target);
            }

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

    private bool HasPotentialVerifiedAppleUploadAttestation(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app)
    {
        if (string.IsNullOrWhiteSpace(plan.SourceCommit))
        {
            return false;
        }

        var receipts = _appleReceiptStore.ReadAll(plan);
        return receipts.Any(receipt => FindMatchingAppleUploadAttestationTarget(
            receipts,
            receipt,
            plan,
            app,
            app.MarketingVersion,
            app.BuildNumber) is not null);
    }

    private static PowerForgeAppleReleaseTargetReceipt? FindMatchingAppleUploadAttestationTarget(
        IReadOnlyCollection<PowerForgeAppleReleaseReceipt> receipts,
        PowerForgeAppleReleaseReceipt receipt,
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        string? version,
        string? build)
    {
        if (receipt.PlanOnly ||
            receipt.SchemaVersion < 4 ||
            string.IsNullOrWhiteSpace(receipt.ReceiptSha256) ||
            !string.Equals(receipt.SourceCommit, plan.SourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return receipt.Targets.SingleOrDefault(candidate =>
            candidate.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.BundleId, app.BundleId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.AppId, app.AppStoreConnectAppId, StringComparison.OrdinalIgnoreCase) &&
            candidate.Platform == app.Platform &&
            string.Equals(candidate.Configuration, app.Configuration, StringComparison.OrdinalIgnoreCase) &&
            AppleReleasePathsEqual(candidate.ProjectPath, FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ProjectPath).Replace('\\', '/')) &&
            candidate.IsWorkspace == app.IsWorkspace &&
            string.Equals(candidate.Scheme, app.Scheme, StringComparison.Ordinal) &&
            candidate.ArchiveVariant == app.ArchiveVariant &&
            string.Equals(candidate.Destination, app.Destination, StringComparison.Ordinal) &&
            candidate.DistributionRoute == app.DistributionRoute &&
            (string.IsNullOrWhiteSpace(version) ||
             string.Equals(candidate.Version, version, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(build) ||
             string.Equals(candidate.Build, build, StringComparison.OrdinalIgnoreCase)) &&
            !candidate.AdoptedExistingBuild &&
            candidate.UploadPerformed &&
            IsSha256(candidate.ArchiveSha256) &&
            IsSha256(candidate.UploadExecutionSha256) &&
            string.Equals(
                candidate.UploadExecutionSha256,
                ComputeAppleUploadExecutionSha256(plan, app),
                StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(app.ExpectedArchiveSha256) ||
             string.Equals(candidate.ArchiveSha256, app.ExpectedArchiveSha256, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(candidate.ArchivePath) &&
            IsVerifiedUploadCheckpoint(receipts, receipt, candidate));
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
            string.Equals(candidate.Configuration, target.Configuration, StringComparison.OrdinalIgnoreCase) &&
            AppleReleasePathsEqual(candidate.ProjectPath, target.ProjectPath) &&
            candidate.IsWorkspace == target.IsWorkspace &&
            string.Equals(candidate.Scheme, target.Scheme, StringComparison.Ordinal) &&
            candidate.ArchiveVariant == target.ArchiveVariant &&
            string.Equals(candidate.Destination, target.Destination, StringComparison.Ordinal) &&
            candidate.DistributionRoute == target.DistributionRoute &&
            AppleReleasePathsEqual(candidate.ArchivePath, target.ArchivePath) &&
            string.Equals(candidate.ArchiveSha256, target.ArchiveSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.UploadExecutionSha256, target.UploadExecutionSha256, StringComparison.OrdinalIgnoreCase));
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
                receipt.SchemaVersion >= 4 &&
                !string.IsNullOrWhiteSpace(receipt.ReceiptSha256) &&
                !string.IsNullOrWhiteSpace(plan.SourceCommit) &&
                string.Equals(receipt.SourceCommit, plan.SourceCommit, StringComparison.OrdinalIgnoreCase))
            .SelectMany(receipt => receipt.Targets.Where(target => IsMatchingDirectReceiptTarget(plan, target, app)))
            .FirstOrDefault(target =>
                string.Equals(target.NotarizationStatus, "Accepted", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(target.NotarizationSubmissionId) &&
                !string.IsNullOrWhiteSpace(target.DirectArtifactPath) &&
                IsSha256(target.DirectArtifactSha256));
        if (prior is null)
            return false;
        EnsureExplicitAppleRecoveryAdoption(plan, app, "an accepted direct notarization submission");

        var artifactPath = ValidateDirectRecoveryArtifactPath(plan, app, prior.DirectArtifactPath!);
        if (!File.Exists(artifactPath) && !Directory.Exists(artifactPath))
            return false;
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
                SubmissionSha256 = prior.NotarizationSubmissionSha256,
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
            prior.NotarizationSubmissionSha256,
            prior.Stapled == true);
        result.ResumedAcceptedNotarization = true;
        result.SkippedSteps = MergeAppleSkippedSteps(
            result.SkippedSteps,
            new[] { "archive", "export", "notarySubmission" });
        if (!result.Notarization.Succeeded)
            throw CreateAppleNotarizationFailure(app, result.Notarization);

        return true;
    }

    internal static bool IsMatchingDirectReceiptTarget(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleReleaseTargetReceipt target,
        PowerForgeAppleAppReleaseTargetPlan app)
        => target.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(target.BundleId, app.BundleId, StringComparison.OrdinalIgnoreCase) &&
           target.Platform == app.Platform &&
           string.Equals(target.Configuration, app.Configuration, StringComparison.OrdinalIgnoreCase) &&
           AppleReleasePathsEqual(
               target.ProjectPath,
               FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ProjectPath).Replace('\\', '/')) &&
           target.IsWorkspace == app.IsWorkspace &&
           string.Equals(target.Scheme, app.Scheme, StringComparison.Ordinal) &&
           target.ArchiveVariant == app.ArchiveVariant &&
           string.Equals(target.Destination, app.Destination, StringComparison.Ordinal) &&
           target.DistributionRoute == AppleDistributionRoute.DirectNotarized &&
           string.Equals(target.Version, app.MarketingVersion, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(target.Build, app.BuildNumber, StringComparison.OrdinalIgnoreCase) &&
           IsSha256(target.DirectExecutionSha256) &&
           string.Equals(
               target.DirectExecutionSha256,
               ComputeDirectExecutionSha256(plan, app),
               StringComparison.OrdinalIgnoreCase) &&
           (!IsSha256(app.ExpectedArchiveSha256) ||
            string.Equals(target.ArchiveSha256, app.ExpectedArchiveSha256, StringComparison.OrdinalIgnoreCase));

    private static bool IsSha256(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value!.Length == 64 &&
           value.All(static character => Uri.IsHexDigit(character));

    internal static bool AppleReleasePathsEqual(string? left, string? right)
        => string.Equals(
            left,
            right,
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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
