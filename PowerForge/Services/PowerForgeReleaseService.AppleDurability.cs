namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private void PrepareAppleReceiptJournalForMutation(
        PowerForgeAppleReleasePlan plan,
        string? expectedPlanSha256)
    {
        if (!plan.Automation.WriteReceipt)
            return;

        _appleReceiptStore.Validate(plan);
        if (!HasAppleExecutionMutation(plan))
            return;

        _appleReceiptStore.WriteAttempt(plan, new PowerForgeAppleReleaseReceipt
        {
            Action = plan.Action,
            SourceCommit = plan.SourceCommit,
            PlanSha256 = expectedPlanSha256,
            OperationPhase = "Started",
            Success = false,
            ErrorMessage = "Apple release operation started; inspect later receipts and remote state before retrying.",
            Targets = plan.Apps.Select(app => CreateAppleCheckpointTarget(plan, app)).ToArray()
        });
    }

    private void WriteAppleUploadAttestation(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        PowerForgeAppleAppReleaseResult result)
    {
        if (!plan.Automation.WriteReceipt || result.Upload?.Succeeded != true)
            return;

        var attemptId = Guid.NewGuid().ToString("N");
        result.UploadAttestationAttemptId = attemptId;
        var target = CreateAppleCheckpointTarget(plan, app);
        target.UploadPerformed = true;
        target.ArchivePath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ArchivePath).Replace('\\', '/');
        target.ArchiveSha256 = result.ArchiveSha256;
        target.BuildUploadId = result.Upload.BuildUploadId;
        target.UploadAttestationAttemptId = attemptId;
        target.UploadExecutionSha256 = ComputeAppleUploadExecutionSha256(plan, app);
        _appleReceiptStore.WriteAttempt(plan, new PowerForgeAppleReleaseReceipt
        {
            AttemptId = attemptId,
            Action = plan.Action,
            SourceCommit = plan.SourceCommit,
            OperationPhase = "UploadAttested",
            Success = true,
            Targets = new[] { target }
        });
    }

    private void WriteAppleNotarizationAttestation(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        PowerForgeAppleAppReleaseResult result)
    {
        if (!plan.Automation.WriteReceipt || result.Notarization is null)
            return;

        var target = CreateAppleCheckpointTarget(plan, app);
        target.ArchivePath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ArchivePath).Replace('\\', '/');
        target.ArchiveSha256 = result.ArchiveSha256 ?? app.ExpectedArchiveSha256;
        target.DirectArtifactPath = CreatePortableDirectArtifactPath(plan, app, result.Notarization.ArtifactPath);
        target.DirectArtifactSha256 = result.Notarization.ArtifactSha256;
        target.DirectExecutionSha256 = ComputeDirectExecutionSha256(plan, app);
        target.NotarizationSubmissionId = result.Notarization.SubmissionId;
        target.NotarizationStatus = result.Notarization.Status;
        target.Stapled = result.Notarization.Staple?.Succeeded;
        target.StapleValidated = result.Notarization.StapleValidation?.Succeeded;
        target.GatekeeperAccepted = result.Notarization.Assessment?.Succeeded;
        target.ErrorMessage = result.Notarization.Succeeded
            ? null
            : $"Direct notarization post-processing did not complete for '{app.Name}'.";
        _appleReceiptStore.WriteAttempt(plan, new PowerForgeAppleReleaseReceipt
        {
            Action = plan.Action,
            SourceCommit = plan.SourceCommit,
            OperationPhase = "NotarizationAttested",
            Success = result.Notarization.Succeeded,
            ErrorMessage = result.Notarization.Succeeded
                ? null
                : $"Direct notarization post-processing did not complete for '{app.Name}'.",
            Targets = new[] { target }
        });
    }

    private void WriteAppleNotarizationAcceptance(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        AppleNotarizationAcceptedCheckpoint checkpoint)
    {
        if (!plan.Automation.WriteReceipt)
            return;

        var target = CreateAppleCheckpointTarget(plan, app);
        target.ArchivePath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ArchivePath).Replace('\\', '/');
        target.ArchiveSha256 = app.ExpectedArchiveSha256;
        target.DirectArtifactPath = CreatePortableDirectArtifactPath(plan, app, checkpoint.ArtifactPath);
        target.DirectArtifactSha256 = checkpoint.ArtifactSha256;
        target.DirectExecutionSha256 = ComputeDirectExecutionSha256(plan, app);
        target.NotarizationSubmissionId = checkpoint.SubmissionId;
        target.NotarizationStatus = checkpoint.Status;
        target.ErrorMessage = $"Apple notarization accepted for '{app.Name}', but local post-processing is incomplete.";
        _appleReceiptStore.WriteAttempt(plan, new PowerForgeAppleReleaseReceipt
        {
            Action = plan.Action,
            SourceCommit = plan.SourceCommit,
            OperationPhase = "NotarizationAccepted",
            Success = false,
            ErrorMessage = target.ErrorMessage,
            Targets = new[] { target }
        });
    }

    private void WriteAppleNotarizationStapled(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        AppleNotarizationStapledCheckpoint checkpoint)
    {
        if (!plan.Automation.WriteReceipt)
            return;

        var target = CreateAppleCheckpointTarget(plan, app);
        target.ArchivePath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ArchivePath).Replace('\\', '/');
        target.ArchiveSha256 = app.ExpectedArchiveSha256;
        target.DirectArtifactPath = CreatePortableDirectArtifactPath(plan, app, checkpoint.ArtifactPath);
        target.DirectArtifactSha256 = checkpoint.ArtifactSha256;
        target.DirectExecutionSha256 = ComputeDirectExecutionSha256(plan, app);
        target.NotarizationSubmissionId = checkpoint.SubmissionId;
        target.NotarizationStatus = checkpoint.Status;
        target.Stapled = true;
        target.StapleValidated = true;
        target.ErrorMessage = $"Apple notarization was stapled and validated for '{app.Name}', but Gatekeeper assessment is incomplete.";
        _appleReceiptStore.WriteAttempt(plan, new PowerForgeAppleReleaseReceipt
        {
            Action = plan.Action,
            SourceCommit = plan.SourceCommit,
            OperationPhase = "NotarizationStapled",
            Success = false,
            ErrorMessage = target.ErrorMessage,
            Targets = new[] { target }
        });
    }

    private static PowerForgeAppleReleaseTargetReceipt CreateAppleCheckpointTarget(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app)
        => new()
        {
            Name = app.Name,
            BundleId = app.BundleId,
            Platform = app.Platform,
            Configuration = app.Configuration,
            ProjectPath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ProjectPath).Replace('\\', '/'),
            IsWorkspace = app.IsWorkspace,
            Scheme = app.Scheme,
            ArchiveVariant = app.ArchiveVariant,
            Destination = app.Destination,
            DistributionRoute = app.DistributionRoute,
            ProductRole = app.ProductRole,
            ParentTarget = app.ParentTarget,
            Capabilities = app.Capabilities,
            TestFlightPolicy = app.TestFlightPolicy,
            AppId = app.AppStoreConnectAppId,
            Version = app.MarketingVersion,
            Build = app.BuildNumber
        };

    internal static string ComputeDirectExecutionSha256(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        if (app is null)
            throw new ArgumentNullException(nameof(app));

        return ComputeStableSha256(new
        {
            plan.SourceCommit,
            ReleaseConfiguration = plan.Configuration,
            plan.XcodeBuildExecutable,
            plan.AllowProvisioningUpdates,
            plan.ManageAppVersionAndBuildNumber,
            plan.UploadSymbols,
            plan.GenerateAppStoreInformation,
            plan.SigningStyle,
            app.Name,
            app.BundleId,
            app.Platform,
            app.ArchiveVariant,
            app.DistributionRoute,
            TargetConfiguration = app.Configuration,
            ProjectPath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ProjectPath).Replace('\\', '/'),
            app.IsWorkspace,
            app.Scheme,
            app.Destination,
            ArchivePath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ArchivePath).Replace('\\', '/'),
            ExportPath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ExportPath).Replace('\\', '/'),
            app.TeamId,
            app.MarketingVersion,
            app.BuildNumber,
            app.GenerateProjectIfMissing,
            app.RegenerateProject,
            app.XcodeGenExecutable,
            app.ProjectGenerationTimeoutSeconds,
            RequiredEmbeddedBundleIds = app.RequiredEmbeddedBundleIds.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            RequiredPrivacyUsageDescriptionKeys = app.RequiredPrivacyUsageDescriptionKeys.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            DirectDistribution = new
            {
                plan.DirectDistribution.ExportMethod,
                plan.DirectDistribution.XcrunExecutable,
                plan.DirectDistribution.DittoExecutable,
                plan.DirectDistribution.SpctlExecutable,
                plan.DirectDistribution.KeychainProfile,
                plan.DirectDistribution.TimeoutSeconds,
                plan.DirectDistribution.Staple,
                plan.DirectDistribution.Assess
            },
            plan.AppStoreConnectApiKeyId,
            plan.AppStoreConnectApiIssuerId
        });
    }

    /// <summary>Computes the exact execution-policy identity required to reuse an App Store upload.</summary>
    internal static string ComputeAppleUploadExecutionSha256(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        if (app is null)
            throw new ArgumentNullException(nameof(app));

        return ComputeStableSha256(new
        {
            plan.SourceCommit,
            ReleaseConfiguration = plan.Configuration,
            plan.XcodeBuildExecutable,
            plan.AllowProvisioningUpdates,
            plan.ManageAppVersionAndBuildNumber,
            plan.UploadSymbols,
            plan.GenerateAppStoreInformation,
            plan.SigningStyle,
            app.Name,
            app.BundleId,
            app.Platform,
            app.ArchiveVariant,
            app.DistributionRoute,
            TargetConfiguration = app.Configuration,
            ProjectPath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ProjectPath).Replace('\\', '/'),
            app.IsWorkspace,
            app.Scheme,
            app.Destination,
            ArchivePath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ArchivePath).Replace('\\', '/'),
            ExportPath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ExportPath).Replace('\\', '/'),
            app.TeamId,
            app.GenerateProjectIfMissing,
            app.RegenerateProject,
            app.XcodeGenExecutable,
            app.ProjectGenerationTimeoutSeconds,
            RequiredEmbeddedBundleIds = app.RequiredEmbeddedBundleIds.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            RequiredPrivacyUsageDescriptionKeys = app.RequiredPrivacyUsageDescriptionKeys.OrderBy(static value => value, StringComparer.Ordinal).ToArray()
        });
    }

    private static string CreatePortableDirectArtifactPath(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        string artifactPath)
    {
        var validated = ValidateDirectRecoveryArtifactPath(plan, app, artifactPath);
        return FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, validated).Replace('\\', '/');
    }

    private static bool HasAppleExecutionMutation(PowerForgeAppleReleasePlan plan)
        => plan.Action == PowerForgeAppleReleaseAction.Version ||
           plan.Action == PowerForgeAppleReleaseAction.Cleanup ||
           plan.Archive ||
           plan.Upload ||
           HasAppleRemoteMutation(plan);

    private static void VerifyAppleArchiveUnchangedAfterUpload(
        PowerForgeAppleAppReleaseTargetPlan app,
        PowerForgeAppleAppReleaseResult result)
    {
        // A successful real xcodebuild archive always leaves the archive on disk. A null value is
        // retained only for injected test/process adapters that do not materialize their artifact.
        if (string.IsNullOrWhiteSpace(result.ArchiveSha256))
            return;
        if (!File.Exists(app.ArchivePath) && !Directory.Exists(app.ArchivePath))
            throw new InvalidOperationException($"The archive disappeared while uploading '{app.Name}': {app.ArchivePath}");

        var afterUpload = AppleNotarizationService.ComputeArtifactSha256(app.ArchivePath);
        if (!afterUpload.Equals(result.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The archive for '{app.Name}' changed during upload. Expected SHA-256 " +
                $"'{result.ArchiveSha256}', received '{afterUpload}'. The upload cannot be used as release evidence.");
        }
    }
}
