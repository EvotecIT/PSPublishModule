namespace PowerForge.Tests;

public sealed partial class AppleTestFlightWorkerTests
{
    private static (AppleTestFlightWorkerPolicy Policy, AppleTestFlightJob Job, PowerForgeAppleReleaseReceipt Receipt) GateHold(bool legacy = false)
    {
        var (policy, job, receipt) = AutomaticBeta();
        job.Stage = AppleTestFlightJobStage.NeedsOperator;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        job.PlanSha256 = new string('b', 64);
        receipt.Action = PowerForgeAppleReleaseAction.Ship;
        receipt.ShipPhase = PowerForgeAppleShipPhase.Release;
        receipt.OperationPhase = "Completed";
        receipt.SchemaVersion = 6;
        receipt.ReceiptSha256 = new string('d', 64); // ReadBetaHistory verifies the real stored hash before migration.
        receipt.Success = false;
        receipt.PlanSha256 = job.PlanSha256;
        receipt.CheckedAt = job.UpdatedAt.AddSeconds(-1);
        receipt.Versioning = new() { MarketingVersion = job.Version!, BuildNumber = job.Build! };
        foreach (var target in receipt.Targets) target.ShipToTestFlight = true;
        var phone = receipt.Targets[0];
        phone.UploadPerformed = true;
        phone.MutationGateClosed = !legacy;
        phone.ErrorMessage = legacy
            ? "TestFlight worker is Off, paused, or has lost its request heartbeat. The next mutating stage is held."
            : "TestFlight mutation gate closed: Request or release-hold observation failed.";
        receipt.ErrorMessage = phone.ErrorMessage;
        var mac = receipt.Targets[1];
        mac.BuildId = null;
        mac.BuildProcessingState = null;
        mac.BuildExpired = null;
        mac.TestFlightExternalState = null;
        mac.SkippedSteps = ["notAttempted"];
        job.Outcomes = receipt.Targets;
        job.Diagnostic = $"{phone.Name}: {phone.ErrorMessage}. Preserve and reconcile this delivery before requesting another build.";
        return (policy, job, receipt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedGateHoldRetainsPartialDelivery_AndCannotSkipTheUntouchedPlatform(bool legacy)
    {
        var (policy, job, receipt) = GateHold(legacy);
        Assert.Equal(AppleTestFlightJobStage.AppleProcessingPending, AppleTestFlightWorker.DeliveryOutcome(receipt, policy).Stage);
        job.Stage = AppleTestFlightJobStage.AppleProcessingPending;
        Assert.False(AppleTestFlightWorker.CanRecoverExistingBeta(job, policy));
        Assert.Equal("phone31", receipt.Targets[0].BuildId);
        Assert.True(receipt.Targets[0].UploadPerformed);
        Assert.Equal("31", job.Build);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("started")]
    [InlineData("invalid")]
    [InlineData("compliance")]
    [InlineData("expired")]
    [InlineData("review-rejected")]
    public void UnknownOrIncompleteMutationAndAppleRejectionCannotBecomeARetryableGateHold(string failure)
    {
        var (policy, _, receipt) = GateHold();
        switch (failure)
        {
            case "unknown": receipt.Targets[0].MutationGateClosed = false; break;
            case "started": receipt.OperationPhase = "Started"; break;
            case "invalid": receipt.Targets[0].BuildProcessingState = "INVALID"; break;
            case "compliance": receipt.Targets[0].TestFlightExternalState = "MISSING_EXPORT_COMPLIANCE"; break;
            case "expired": receipt.Targets[0].BuildExpired = true; break;
            case "review-rejected": receipt.Targets[0].TestFlightReviewState = "REJECTED"; break;
        }
        Assert.Equal(AppleTestFlightJobStage.NeedsOperator, AppleTestFlightWorker.DeliveryOutcome(receipt, policy).Stage);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("plan")]
    [InlineData("build")]
    [InlineData("started")]
    [InlineData("ambiguous")]
    [InlineData("newer")]
    [InlineData("unsigned")]
    public void LegacyGateRecoveryBindsTheAcknowledgedCompletedAttempt_AndRejectsStaleOrAmbiguousEvidence(string mismatch)
    {
        var (policy, job, receipt) = GateHold(legacy: true);
        Assert.True(AppleTestFlightWorker.CanRecoverCompletedGateHold(job, receipt, policy));
        switch (mismatch)
        {
            case "source": receipt.SourceCommit = new string('c', 40); break;
            case "plan": receipt.PlanSha256 = new string('c', 64); break;
            case "build": receipt.Versioning!.BuildNumber = "32"; break;
            case "started": receipt.OperationPhase = "Started"; break;
            case "ambiguous": job.Diagnostic = "Worker stopped during mutation; reconcile canonical receipts."; break;
            case "newer": receipt.CheckedAt = job.UpdatedAt.AddSeconds(1); break;
            case "unsigned": receipt.ReceiptSha256 = null; break;
        }
        Assert.False(AppleTestFlightWorker.CanRecoverCompletedGateHold(job, receipt, policy));
    }

    [Fact]
    public void MutationGateRetainsObservationFailureReason_AndExpiryStillClosesIt()
    {
        var root = Sandbox();
        try
        {
            var path = Path.Combine(root, "gate.json");
            var now = DateTimeOffset.UtcNow;
            AppleReleaseAppState.AtomicWrite(path, new { Allowed = false, ExpiresAt = now.AddMinutes(3), Reason = "GitHub request observation failed." });
            Assert.False(AppleReleaseAppState.GateAllowsMutation(path, now, out var reason));
            Assert.Equal("GitHub request observation failed.", reason);
            AppleReleaseAppState.AtomicWrite(path, new { Allowed = true, ExpiresAt = now.AddSeconds(-1) });
            Assert.False(AppleReleaseAppState.GateAllowsMutation(path, now, out reason));
            Assert.Contains("expired", reason);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LaterEvidenceFailureCannotInheritPermissionToResumeAGateHold()
    {
        var (policy, _, receipt) = GateHold();
        var result = new PowerForgeAppleAppReleaseResult { ErrorMessage = receipt.ErrorMessage, MutationGateClosed = true };
        result.ErrorMessage += " Unable to capture upload export evidence.";
        receipt.Targets[0].ErrorMessage = receipt.ErrorMessage = result.ErrorMessage;
        receipt.Targets[0].MutationGateClosed = result.MutationGateClosed;
        Assert.Equal(AppleTestFlightJobStage.NeedsOperator, AppleTestFlightWorker.DeliveryOutcome(receipt, policy).Stage);
    }
}
