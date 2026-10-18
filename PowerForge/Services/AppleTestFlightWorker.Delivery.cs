#if NET8_0_OR_GREATER
namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    internal static bool IsExternalReviewPending(PowerForgeAppleReleaseReceipt receipt, AppleTestFlightWorkerPolicy policy)
        => !policy.BetaGroupIsInternal && SelectedDeliveryTargets(receipt, policy).Any(IsReviewPending);

    private static bool IsReviewPending(PowerForgeAppleReleaseTargetReceipt target)
        => target.TestFlightExternalState is "IN_BETA_REVIEW" or "WAITING_FOR_BETA_REVIEW" ||
           target.TestFlightReviewState is "WAITING_FOR_REVIEW" or "IN_REVIEW";

    internal static (AppleTestFlightJobStage Stage, string? Diagnostic) DeliveryOutcome(
        PowerForgeAppleReleaseReceipt receipt, AppleTestFlightWorkerPolicy policy)
    {
        if (IsReady(receipt, policy))
            return (policy.BetaGroupIsInternal ? AppleTestFlightJobStage.ReadyInHome : AppleTestFlightJobStage.ReadyInTestFlight, null);

        var selected = SelectedDeliveryTargets(receipt, policy).ToArray();
        var held = IsCompletedGateHeldShip(receipt, policy);
        var deliveryFailure = selected.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value.ErrorMessage) &&
            !value.WaitingForAppleBuildProcessing && !(held && IsKnownGateHold(value)));
        if (deliveryFailure is not null)
            return (AppleTestFlightJobStage.NeedsOperator,
                $"{deliveryFailure.Name}: {deliveryFailure.ErrorMessage}. Preserve and reconcile this delivery before requesting another build.");
        if (!receipt.Success && !held && !selected.Any(value => value.WaitingForAppleBuildProcessing))
            return (AppleTestFlightJobStage.NeedsOperator, receipt.ErrorMessage ?? "TestFlight delivery failed; inspect the per-platform outcomes before resuming.");

        var failed = selected.FirstOrDefault(value => value.BuildExpired == true ||
            !string.IsNullOrEmpty(value.BuildProcessingState) && value.BuildProcessingState is not ("VALID" or "PROCESSING" or "PROCESSING_PENDING"));
        if (failed is not null)
            return (AppleTestFlightJobStage.NeedsOperator, $"{failed.Name}: {failed.ErrorMessage ?? "Apple build processing ended in " + failed.BuildProcessingState}. Preserve and reconcile this delivery before requesting another build.");

        if (!policy.BetaGroupIsInternal && selected.Any(value => value.BuildProcessingState == "VALID" &&
                (RequiresBetaOperatorAction(value) ||
                 value.TestFlightExternalState == "READY_FOR_BETA_SUBMISSION" && !IsReviewPending(value) && !policy.SubmitBetaReviewAutomatically)))
            return (AppleTestFlightJobStage.NeedsOperator,
                "External TestFlight requires operator action. Inspect Beta App Review or export compliance; submit beta review only after separate operator approval. Preserve and reconcile this delivery before requesting another build.");

        if (IsExternalReviewPending(receipt, policy))
            return (AppleTestFlightJobStage.AppleProcessingPending, "Apple Beta App Review is in progress; observe its outcome and verify the enrolled group access before assigning a build.");

        if (held)
            return (AppleTestFlightJobStage.AppleProcessingPending,
                "The local mutation gate closed. Retain this exact delivery and resume only after fresh gate checks; do not allocate another build.");

        if (receipt.ShipPhase == PowerForgeAppleShipPhase.Release &&
            (receipt.Success || selected.Any(value => value.WaitingForAppleBuildProcessing)) &&
            receipt.Targets.All(value => string.IsNullOrEmpty(value.BuildProcessingState) || value.BuildProcessingState is "VALID" or "PROCESSING" or "PROCESSING_PENDING"))
            return (AppleTestFlightJobStage.AppleProcessingPending, receipt.ErrorMessage);

        return (AppleTestFlightJobStage.NeedsOperator, receipt.ErrorMessage ??
            "Selected builds are not yet verified available in the enrolled beta group; inspect the per-platform outcomes.");
    }

    private static IEnumerable<PowerForgeAppleReleaseTargetReceipt> SelectedDeliveryTargets(
        PowerForgeAppleReleaseReceipt receipt, AppleTestFlightWorkerPolicy policy)
        => receipt.Targets.Where(value => policy.Targets.Any(target => target.Name == value.Name));
}
#endif
