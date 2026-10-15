#if NET8_0_OR_GREATER
namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    internal static bool IsExternalReviewPending(PowerForgeAppleReleaseReceipt receipt, AppleTestFlightWorkerPolicy policy)
        => !policy.BetaGroupIsInternal && SelectedDeliveryTargets(receipt, policy).Any(value =>
            value.TestFlightExternalState is "IN_BETA_REVIEW" or "WAITING_FOR_BETA_REVIEW");

    internal static (AppleTestFlightJobStage Stage, string? Diagnostic) DeliveryOutcome(
        PowerForgeAppleReleaseReceipt receipt, AppleTestFlightWorkerPolicy policy)
    {
        if (IsReady(receipt, policy))
            return (policy.BetaGroupIsInternal ? AppleTestFlightJobStage.ReadyInHome : AppleTestFlightJobStage.ReadyInTestFlight, null);

        var selected = SelectedDeliveryTargets(receipt, policy).ToArray();
        var failed = selected.FirstOrDefault(value => !string.IsNullOrEmpty(value.BuildProcessingState) &&
            value.BuildProcessingState is not ("VALID" or "PROCESSING" or "PROCESSING_PENDING"));
        if (failed is not null)
            return (AppleTestFlightJobStage.NeedsOperator, $"{failed.Name}: {failed.ErrorMessage ?? "Apple build processing ended in " + failed.BuildProcessingState}. Preserve and reconcile this delivery before requesting another build.");

        if (!policy.BetaGroupIsInternal && selected.Any(value => value.BuildProcessingState == "VALID" &&
                value.TestFlightExternalState is "READY_FOR_BETA_SUBMISSION" or "BETA_REJECTED" or "MISSING_EXPORT_COMPLIANCE"))
            return (AppleTestFlightJobStage.NeedsOperator,
                "External TestFlight requires operator action. Inspect Beta App Review or export compliance; submit beta review only after separate operator approval. Preserve and reconcile this delivery before requesting another build.");

        if (IsExternalReviewPending(receipt, policy))
            return (AppleTestFlightJobStage.AppleProcessingPending, "Apple Beta App Review is in progress; observe its outcome without repeating build assignment.");

        if (receipt.ShipPhase == PowerForgeAppleShipPhase.Release &&
            (receipt.Success || receipt.Targets.Any(value => value.BuildProcessingState is "PROCESSING" or "PROCESSING_PENDING")) &&
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
