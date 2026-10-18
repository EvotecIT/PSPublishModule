#if NET8_0_OR_GREATER
namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    // Exact legacy messages are admitted only from integrity-validated canonical
    // history. Unknown errors and incomplete mutations still require an operator.
    private const string LegacyWorkerGateClosed = "TestFlight worker is Off, paused, or has lost its request heartbeat. The next mutating stage is held.";
    private const string LegacyAppPaused = "Apple operations for this app are paused by the local operator.";

    private static bool IsKnownGateHold(PowerForgeAppleReleaseTargetReceipt target) =>
        !string.IsNullOrEmpty(target.ErrorMessage) && (target.MutationGateClosed ||
            target.ErrorMessage is LegacyWorkerGateClosed or LegacyAppPaused);

    internal static bool IsCompletedGateHeldShip(PowerForgeAppleReleaseReceipt receipt, AppleTestFlightWorkerPolicy policy)
    {
        if (receipt.Success || receipt.PlanOnly || receipt.OperationPhase != "Completed" ||
            receipt.Action != PowerForgeAppleReleaseAction.Ship || receipt.ShipPhase != PowerForgeAppleShipPhase.Release ||
            !GitObjectId.IsFull(receipt.SourceCommit) || receipt.Targets.Any(target => target.ShipToAppStoreReview)) return false;
        var selected = SelectedDeliveryTargets(receipt, policy).ToArray();
        if (selected.Length != policy.Targets.Length || selected.Length == 0 ||
            policy.Targets.Any(target => selected.Count(value => value.Name == target.Name && value.BundleId == target.BundleId &&
                value.Platform == target.Platform && value.AppId == target.AppId && value.ShipToTestFlight) != 1)) return false;
        return HasOnlyGateFailures(receipt);
    }

    private static bool HasOnlyGateFailures(PowerForgeAppleReleaseReceipt receipt) =>
        receipt.OperationPhase == "Completed" && receipt.Targets.Any(target => target.ErrorMessage == receipt.ErrorMessage && IsKnownGateHold(target)) &&
        receipt.Targets.All(target => string.IsNullOrEmpty(target.ErrorMessage) || IsKnownGateHold(target));

    // This is a migration of a known completed hold, not a general NeedsOperator
    // retry. ReadBetaHistory validates the immutable receipt hash chain.
    private void RecoverCompletedGateHolds()
    {
        var held = _state.Jobs.Where(job => job.Stage == AppleTestFlightJobStage.NeedsOperator).ToArray();
        if (held.Length != 1 || held[0].Outcomes.All(target => !IsKnownGateHold(target))) return;
        var receipt = ReadBetaHistory().FirstOrDefault(value => !value.PlanOnly &&
            value.Action is not (PowerForgeAppleReleaseAction.Status or PowerForgeAppleReleaseAction.Doctor));
        if (receipt is null || !CanRecoverCompletedGateHold(held[0], receipt, _policy)) return;
        held[0].Outcomes = receipt.Targets;
        held[0].Stage = AppleTestFlightJobStage.AppleProcessingPending;
        held[0].Diagnostic = "Validated the completed canonical gate hold; retain this exact build and recheck the gate before resuming delivery.";
        held[0].UpdatedAt = DateTimeOffset.UtcNow;
        Save();
    }

    internal static bool CanRecoverCompletedGateHold(AppleTestFlightJob job, PowerForgeAppleReleaseReceipt receipt,
        AppleTestFlightWorkerPolicy policy)
    {
        if (job.Stage != AppleTestFlightJobStage.NeedsOperator || string.IsNullOrEmpty(job.Version) || string.IsNullOrEmpty(job.Build) ||
            receipt.Versioning is null || receipt.SchemaVersion < 4 || receipt.ReceiptSha256?.Length != 64 ||
            !IsCompletedGateHeldShip(receipt, policy) ||
            receipt.SourceCommit != job.Source || receipt.PlanSha256?.Length != 64 || receipt.PlanSha256 != job.PlanSha256 ||
            receipt.Versioning?.BuildNumber != job.Build || receipt.Versioning.MarketingVersion != job.Version ||
            receipt.CheckedAt > job.UpdatedAt) return false;
        var failed = SelectedDeliveryTargets(receipt, policy).First(IsKnownGateHold);
        // Only the old DeliveryOutcome acknowledgement may be migrated. A crash,
        // missing receipt or any subsequent ambiguous failure retains its hold.
        if (job.Diagnostic != $"{failed.Name}: {failed.ErrorMessage}. Preserve and reconcile this delivery before requesting another build.") return false;
        return SelectedDeliveryTargets(receipt, policy).All(target => target.Version == job.Version && target.Build == job.Build &&
            target.BuildExpired != true && target.BuildProcessingState is null or "" or "VALID" or "PROCESSING" or "PROCESSING_PENDING" &&
            !RequiresBetaOperatorAction(target));
    }
}
#endif
