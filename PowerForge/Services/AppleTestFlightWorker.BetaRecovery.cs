#if NET8_0_OR_GREATER
namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    // Ship waits after each upload. A timeout can leave later platforms untouched;
    // beta-only commands cannot finish those uploads. Keep ordinary Ship recovery
    // (and its latest-main gate) until every target has positive delivery evidence.
    internal static bool CanRecoverExistingBeta(AppleTestFlightJob job, AppleTestFlightWorkerPolicy policy)
        => job.Stage == AppleTestFlightJobStage.AppleProcessingPending && policy.SubmitBetaReviewAutomatically &&
           !policy.BetaGroupIsInternal && policy.Targets.All(target => job.UploadedTargets.Contains(target.Name) || job.Outcomes.Any(value =>
               value.Name == target.Name && value.BundleId == target.BundleId && value.AppId == target.AppId && value.Platform == target.Platform &&
               value.Version == job.Version && value.Build == job.Build && !value.SkippedSteps.Contains("notAttempted") &&
               (value.UploadPerformed || !string.IsNullOrEmpty(value.UploadAttestationAttemptId) || !string.IsNullOrEmpty(value.BuildId))));

    private void AdvanceExistingBeta(AppleTestFlightJob job)
    {
        ReadSpec();
        // This transition is admitted only from positive Ship delivery evidence.
        // Status receipts intentionally do not repeat archive/upload flags.
        job.UploadedTargets = _policy.Targets.Select(value => value.Name).ToArray();
        Save();
        var status = InvokeBeta(PowerForgeAppleReleaseAction.Status, _policy.Targets.Select(value => value.Name).ToArray(), plan: false);
        if (!status.Success) throw new AppleTestFlightToolException("Could not observe the uploaded beta builds; retain their source and retry observation.");
        ValidateExistingBetaReceipt(status, job, _policy, _policy.Targets.Select(value => value.Name).ToArray(), requireValid: false);
        BindBetaBuildIds(job, status);
        job.Outcomes = status.Targets;
        Save();
        var action = NextBetaAction(status, _policy);
        if (action.Action is null)
        {
            SetStage(AppleTestFlightJobStage.AppleProcessingPending, action.Diagnostic);
            return;
        }
        var plan = InvokeBeta(action.Action.Value, action.Targets, plan: true);
        ValidateBetaPlan(plan, job, _policy, action.Action.Value, action.Targets);
        job.BetaPlanSha256 = plan.PlanSha256;
        job.BetaActionTargets = action.Targets;
        job.BetaActionStartedAt = DateTimeOffset.UtcNow;
        SetStage(action.Action == PowerForgeAppleReleaseAction.SubmitTestFlightReview
            ? AppleTestFlightJobStage.SubmittingBetaReview : AppleTestFlightJobStage.DistributingTestFlight);
        var result = InvokeBeta(action.Action.Value, action.Targets, plan: false);
        CompleteBetaMutation(job, result, action.Action.Value);
    }

    private void CompleteBetaMutation(AppleTestFlightJob job, PowerForgeAppleReleaseReceipt result, PowerForgeAppleReleaseAction action)
    {
        ValidateExistingBetaReceipt(result, job, _policy, job.BetaActionTargets, requireValid: true);
        if (job.BetaActionStartedAt is null || job.BetaPlanSha256?.Length != 64 || result.Action != action || result.PlanOnly || result.CheckedAt < job.BetaActionStartedAt ||
            !string.Equals(result.PlanSha256, job.BetaPlanSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Beta result does not match the retained source/build-bound approval.");
        if (!result.Success && !HasOnlyGateFailures(result))
            throw new InvalidOperationException(result.ErrorMessage ?? "Beta action failed; reconcile the retained exact builds before resuming.");
        job.Outcomes = MergeBetaOutcomes(job.Outcomes, result.Targets);
        SetStage(action == PowerForgeAppleReleaseAction.TestFlight && IsExistingBetaReady(result, job, _policy)
            ? AppleTestFlightJobStage.ReadyInTestFlight : AppleTestFlightJobStage.AppleProcessingPending,
            !result.Success
                ? "The local mutation gate closed; observe the retained builds and recheck the gate before continuing this beta action."
                : action == PowerForgeAppleReleaseAction.SubmitTestFlightReview
                ? "Exact uploaded builds submitted to Beta App Review; waiting for Apple's decision."
                : "Verify all selected builds and enrolled group access after Apple's state converges.");
    }

    // A crash after entry can have sent a request. Only the validated immutable
    // completed attempt can acknowledge that stage; absence is not retry authority.
    private void RecoverBetaMutation(AppleTestFlightJob job)
    {
        var action = job.Stage == AppleTestFlightJobStage.SubmittingBetaReview
            ? PowerForgeAppleReleaseAction.SubmitTestFlightReview : PowerForgeAppleReleaseAction.TestFlight;
        var receipt = ReadBetaHistory().FirstOrDefault(value => value.Action == action && !value.PlanOnly &&
            value.OperationPhase == "Completed" && value.SourceCommit == job.Source &&
            job.BetaActionStartedAt is not null && value.CheckedAt >= job.BetaActionStartedAt &&
            string.Equals(value.PlanSha256, job.BetaPlanSha256, StringComparison.OrdinalIgnoreCase));
        if (receipt is null)
        {
            SetStage(AppleTestFlightJobStage.NeedsOperator, "Worker stopped during a beta mutation without a matching completed canonical receipt. Reconcile the exact existing builds; do not allocate another build or blindly resubmit.");
            return;
        }
        CompleteBetaMutation(job, receipt, action);
    }

    internal static (PowerForgeAppleReleaseAction? Action, string[] Targets, string? Diagnostic) NextBetaAction(
        PowerForgeAppleReleaseReceipt status, AppleTestFlightWorkerPolicy policy)
    {
        var targets = SelectedDeliveryTargets(status, policy).ToArray();
        foreach (var target in targets)
        {
            if (target.BuildProcessingState is not (null or "" or "VALID" or "PROCESSING" or "PROCESSING_PENDING") ||
                target.BuildExpired == true || RequiresBetaOperatorAction(target))
                throw new InvalidOperationException($"{target.Name}: Apple rejected, expired, or requires compliance for this build. Operator action is required; preserve its delivery history.");
            if (target.BuildProcessingState == "VALID" && target.BuildExpired != false)
                throw new InvalidOperationException($"{target.Name}: explicit nonexpiry evidence is required before any beta mutation.");
        }
        var submit = targets.Where(value => value.BuildProcessingState == "VALID" && !IsReviewPending(value) &&
            value.TestFlightExternalState == "READY_FOR_BETA_SUBMISSION" && value.TestFlightReviewState != "APPROVED")
            .Select(value => value.Name).ToArray();
        if (submit.Length != 0)
        {
            if (!policy.SubmitBetaReviewAutomatically || policy.BetaGroupIsInternal)
                throw new InvalidOperationException("This local policy does not authorize automatic external Beta App Review.");
            return (PowerForgeAppleReleaseAction.SubmitTestFlightReview, submit, null);
        }
        if (targets.Any(value => value.BuildProcessingState != "VALID" || IsReviewPending(value)))
            return (null, Array.Empty<string>(), "Waiting for Apple build processing or Beta App Review; no duplicate submission.");
        if (targets.All(value => value.TestFlightExternalState is "BETA_APPROVED" or "READY_FOR_BETA_TESTING" or "IN_BETA_TESTING" ||
                                 value.TestFlightReviewState == "APPROVED"))
            return (PowerForgeAppleReleaseAction.TestFlight, targets.Select(value => value.Name).ToArray(), null);
        throw new InvalidOperationException("Apple's external beta state is not known to be approved. Inspect the exact build before distributing it.");
    }

    internal static void ValidateExistingBetaReceipt(PowerForgeAppleReleaseReceipt receipt, AppleTestFlightJob job,
        AppleTestFlightWorkerPolicy policy, string[] targets, bool requireValid)
    {
        if (!GitObjectId.IsFull(job.Source) || receipt.SourceCommit != job.Source || receipt.Versioning is not null ||
            string.IsNullOrWhiteSpace(job.Version) || string.IsNullOrWhiteSpace(job.Build) || targets.Length == 0 ||
            targets.Distinct().Count() != targets.Length ||
            !receipt.Targets.Select(value => value.Name).Order().SequenceEqual(targets.Order()))
            throw new InvalidOperationException("Existing beta receipt has stale source, versioning, or unexpected targets.");
        foreach (var name in targets)
        {
            var enrolled = policy.Targets.Single(value => value.Name == name);
            var value = receipt.Targets.Single(value => value.Name == name);
            var priorId = job.BuildIds.GetValueOrDefault(name) ?? job.Outcomes.FirstOrDefault(old => old.Name == name)?.BuildId;
            if (value.BundleId != enrolled.BundleId || value.AppId != enrolled.AppId || value.Platform != enrolled.Platform ||
                value.TestFlightPolicy != AppleTestFlightPolicy.External || value.Version != job.Version || value.Build != job.Build ||
                value.ArchiveCreated || value.UploadPerformed || value.ProjectGenerated || value.ShipToAppStoreReview ||
                (!string.IsNullOrEmpty(priorId) && value.BuildId != priorId) ||
                (requireValid && (string.IsNullOrWhiteSpace(value.BuildId) || value.BuildProcessingState != "VALID" || value.BuildExpired != false)))
                throw new InvalidOperationException($"{name}: beta recovery changed the exact enrolled build identity or exceeded beta-only authority.");
            if (requireValid && RequiresBetaOperatorAction(value))
                throw new InvalidOperationException($"{name}: Apple rejected this beta build or requires export compliance. Operator action is required before another beta action.");
        }
    }

    // A processing-valid build can still require a human review or compliance
    // decision. Reject it at both plan and completed-result boundaries, including
    // a gate-held result, rather than recording a misleading retryable stage.
    private static bool RequiresBetaOperatorAction(PowerForgeAppleReleaseTargetReceipt target)
        => target.TestFlightExternalState is "BETA_REJECTED" or "MISSING_EXPORT_COMPLIANCE" ||
           target.TestFlightReviewState == "REJECTED";

    internal static void ValidateBetaPlan(PowerForgeAppleReleaseReceipt plan, AppleTestFlightJob job,
        AppleTestFlightWorkerPolicy policy, PowerForgeAppleReleaseAction action, string[] targets)
    {
        if (action is not (PowerForgeAppleReleaseAction.TestFlight or PowerForgeAppleReleaseAction.SubmitTestFlightReview) ||
            action == PowerForgeAppleReleaseAction.SubmitTestFlightReview && !policy.SubmitBetaReviewAutomatically ||
            !plan.Success || !plan.PlanOnly || plan.Action != action || plan.PlanSha256 is null ||
            plan.PlanSha256.Length != 64 || plan.PlanSha256.Any(value => !Uri.IsHexDigit(value)))
            throw new InvalidOperationException("Only a successful exact beta-only plan may be automatically confirmed.");
        ValidateExistingBetaReceipt(plan, job, policy, targets, requireValid: true);
        if (targets.Any(name => !job.BuildIds.ContainsKey(name)))
            throw new InvalidOperationException("Observe and retain exact build IDs before approving the beta plan.");
    }

    private static void BindBetaBuildIds(AppleTestFlightJob job, PowerForgeAppleReleaseReceipt receipt)
    {
        foreach (var value in receipt.Targets.Where(value => !string.IsNullOrEmpty(value.BuildId)))
            job.BuildIds[value.Name] = value.BuildId!;
    }

    private static PowerForgeAppleReleaseTargetReceipt[] MergeBetaOutcomes(PowerForgeAppleReleaseTargetReceipt[] previous, PowerForgeAppleReleaseTargetReceipt[] current)
        => previous.Where(value => !current.Any(next => next.Name == value.Name)).Concat(current).ToArray();

    internal static bool IsExistingBetaReady(PowerForgeAppleReleaseReceipt receipt, AppleTestFlightJob job, AppleTestFlightWorkerPolicy policy)
        => receipt.Success && policy.Targets.All(target => receipt.Targets.Any(value => value.Name == target.Name &&
            value.BuildId == job.BuildIds.GetValueOrDefault(target.Name) && !string.IsNullOrEmpty(value.BuildId) &&
            value.Version == job.Version && value.Build == job.Build && value.BuildProcessingState == "VALID" && value.BuildExpired == false &&
            value.TestFlightExternalState == "IN_BETA_TESTING" && value.DistributedTestFlightBetaGroups.Any(group =>
                group.Id == policy.BetaGroupId && group.Name == policy.BetaGroupName && group.IsInternalGroup == false)));
}
#endif
