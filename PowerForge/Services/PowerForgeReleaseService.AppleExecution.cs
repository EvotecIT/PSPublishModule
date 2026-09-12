namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static bool HasConfiguredAppleRemoteMutation(PowerForgeAppleReleasePlan plan)
        => plan.Action == PowerForgeAppleReleaseAction.Configured &&
           (plan.Upload || HasAppleRemoteMutation(plan));

    private bool ExecuteAppleReleasePlan(
        PowerForgeAppleReleasePlan applePlan,
        PowerForgeReleaseRequest request,
        PowerForgeReleaseResult result,
        bool checkpointAppleApps,
        string? expectedPlanSha256 = null,
        IReadOnlyCollection<PowerForgeAppleAppReleaseResult>? checkpointResults = null,
        bool startProgress = true)
    {
        if (startProgress)
            StartAppleReleaseProgress(applePlan, request);

        using var operationLock = AppleReleaseOperationLock.Acquire(applePlan.LockPath, applePlan.Action);
        var cleanup = new PowerForgeAppleReleaseCleanupReceipt();
        PowerForgeAppleAppReleaseResult[] appleResults;
        PowerForgeAppleVersionReceipt? appleVersioning = null;
        var receiptJournalReady = !applePlan.Automation.WriteReceipt;
        try
        {
            VerifyExpectedAppleCheckpointArchives(applePlan);
            var expected = expectedPlanSha256 ?? request.AppleExpectedPlanSha256;
            var approvedPlan = AssertApplePlanStillApproved(applePlan, expected);
            PrepareAppleReceiptJournalForMutation(applePlan, expected);
            receiptJournalReady = true;
            if (applePlan.Action == PowerForgeAppleReleaseAction.Version)
            {
                AppleReleaseSourceSnapshot.ValidateCurrentSourceIfRequired(applePlan);
                appleVersioning = SelectAppleVersion(
                    applePlan,
                    approvedPlan?.Versioning ?? throw new InvalidOperationException(
                        "Apple Version execution requires one approved remote version observation."));
                appleResults = RunAppleVersion(applePlan);
            }
            else if (applePlan.Action == PowerForgeAppleReleaseAction.Ship &&
                     approvedPlan?.ShipPhase == PowerForgeAppleShipPhase.VersionCheckpoint)
            {
                AppleReleaseSourceSnapshot.ValidateCurrentSourceIfRequired(applePlan);
                appleVersioning = SelectAppleVersion(
                    applePlan,
                    approvedPlan.Versioning ?? throw new InvalidOperationException(
                        "Apple Ship version checkpoint requires one approved version plan."));
                appleResults = RunAppleVersion(applePlan);
            }
            else if (checkpointAppleApps)
            {
                appleResults = RunAppleArchiveCheckpoint(applePlan, out cleanup);
            }
            else if (applePlan.Action == PowerForgeAppleReleaseAction.Cleanup)
            {
                cleanup = _appleArtifactService.RemoveStaleArtifacts(
                    applePlan,
                    GetProtectedAppleRecoveryArtifactPaths(applePlan));
                appleResults = applePlan.Apps
                    .Select(app => new PowerForgeAppleAppReleaseResult
                    {
                        Plan = app,
                        Success = true
                    })
                    .ToArray();
            }
            else
            {
                appleResults = RunAppleRelease(applePlan, out cleanup);
            }
        }
        catch (Exception exception)
        {
            appleResults = applePlan.Apps
                .Select(app => new PowerForgeAppleAppReleaseResult
                {
                    Plan = app,
                    Success = false,
                    ErrorMessage = exception.Message,
                    RemoteState = exception is AppleBuildProcessingException processing
                        ? processing.State
                        : null
                })
                .ToArray();
        }

        if (checkpointResults is not null)
            MergeAppleCheckpointEvidence(checkpointResults, appleResults);
        result.AppleApps = appleResults;
        if (checkpointAppleApps && appleResults.All(static app => app.Success))
            result.AppleReceipt = CreateApplePlanReceipt(applePlan, appleResults);
        if (receiptJournalReady &&
            !checkpointAppleApps &&
            (applePlan.Action != PowerForgeAppleReleaseAction.Configured ||
             HasAppleExecutionMutation(applePlan) ||
             appleResults.Any(static app => !app.Success)))
            result.AppleReceipt = CompleteAppleReleaseReceipt(applePlan, appleResults, cleanup, appleVersioning);

        if (result.AppleReceipt is { Success: false } failedReceipt)
        {
            request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.AppleApps, failedReceipt.ErrorMessage);
            result.Success = false;
            result.ErrorMessage = failedReceipt.ErrorMessage ?? "Apple release diagnostics failed.";
            return false;
        }

        var failure = appleResults.FirstOrDefault(entry => !entry.Success);
        if (failure is not null)
        {
            request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.AppleApps, failure.ErrorMessage);
            result.Success = false;
            result.ErrorMessage = failure.ErrorMessage ?? $"Apple app release failed for '{failure.Plan.Name}'.";
            return false;
        }

        request.Progress?.PhaseCompleted(
            PowerForgeReleaseProgressPhase.AppleApps,
            $"{applePlan.Action} completed for {applePlan.Apps.Length} target(s)");
        return true;
    }

    private bool BeginDeferredAppleArchiveCheckpoint(
        PowerForgeAppleReleasePlan applePlan,
        PowerForgeReleaseRequest request,
        PowerForgeReleaseResult result)
    {
        StartAppleReleaseProgress(applePlan, request);
        using var operationLock = AppleReleaseOperationLock.Acquire(applePlan.LockPath, applePlan.Action);
        PowerForgeAppleAppReleaseResult[] appleResults;
        PowerForgeAppleReleaseReceipt? approvedPlan = null;
        try
        {
            VerifyExpectedAppleCheckpointArchives(applePlan);
            approvedPlan = AssertApplePlanStillApproved(applePlan, request.AppleExpectedPlanSha256) ??
                           CreateApplePlanReceipt(applePlan);
            appleResults = RunAppleArchiveCheckpoint(applePlan, out _);
        }
        catch (Exception exception)
        {
            appleResults = CreateFailedAppleResults(applePlan, exception);
        }

        result.AppleApps = appleResults;
        if (appleResults.All(static app => app.Success))
            result.AppleReceipt = CreateAppleCheckpointedPlanReceipt(applePlan, approvedPlan!, appleResults);

        var failure = appleResults.FirstOrDefault(static entry => !entry.Success);
        if (failure is null)
            return true;

        request.Progress?.PhaseFailed(PowerForgeReleaseProgressPhase.AppleApps, failure.ErrorMessage);
        result.Success = false;
        result.ErrorMessage = failure.ErrorMessage ?? $"Apple app archive checkpoint failed for '{failure.Plan.Name}'.";
        return false;
    }

    private static void StartAppleReleaseProgress(
        PowerForgeAppleReleasePlan applePlan,
        PowerForgeReleaseRequest request)
    {
        request.Progress?.PhaseStarted(
            PowerForgeReleaseProgressPhase.AppleApps,
            applePlan.Apps.Length,
            $"{applePlan.Action}: {applePlan.Apps.Length} Apple target(s)");
        if (request.Progress is not IPowerForgeReleaseProgressReporterV2 detailedAppleProgress)
            return;

        detailedAppleProgress.ItemsPlanned(
            PowerForgeReleaseProgressPhase.AppleApps,
            applePlan.Apps.Select((app, index) => new PowerForgeReleaseProgressItem
            {
                Phase = PowerForgeReleaseProgressPhase.AppleApps,
                Key = "apple:" + app.Name,
                Title = app.Name,
                Kind = applePlan.Action.ToString(),
                Target = app.Platform.ToString(),
                CounterLabel = "Target",
                Position = index + 1,
                Total = applePlan.Apps.Length
            }).ToArray());
    }

    private static PowerForgeAppleAppReleaseResult[] CreateFailedAppleResults(
        PowerForgeAppleReleasePlan applePlan,
        Exception exception)
        => applePlan.Apps
            .Select(app => new PowerForgeAppleAppReleaseResult
            {
                Plan = app,
                Success = false,
                ErrorMessage = exception.Message,
                RemoteState = exception is AppleBuildProcessingException processing
                    ? processing.State
                    : null
            })
            .ToArray();

    private static void MergeAppleCheckpointEvidence(
        IReadOnlyCollection<PowerForgeAppleAppReleaseResult> checkpointResults,
        IReadOnlyCollection<PowerForgeAppleAppReleaseResult> appleResults)
    {
        foreach (var result in appleResults)
        {
            var checkpoint = checkpointResults.Single(candidate =>
                candidate.Plan.Name.Equals(result.Plan.Name, StringComparison.OrdinalIgnoreCase));
            result.Archive ??= checkpoint.Archive;
            result.ArchiveSha256 ??= checkpoint.ArchiveSha256;
            result.ProjectGenerated |= checkpoint.ProjectGenerated;
        }
    }
}
