namespace PowerForge.Tests;

public sealed partial class AppleTestFlightWorkerTests
{
    [Theory]
    [InlineData("WAITING_FOR_REVIEW")]
    [InlineData("IN_REVIEW")]
    public void ExternalDelivery_UsesPendingSubmissionBeforeExternalStateConverges(string reviewState)
    {
        var policy = new AppleTestFlightWorkerPolicy { BetaGroupIsInternal = false, Targets = [new() { Name = "iOS" }] };
        var receipt = new PowerForgeAppleReleaseReceipt
        {
            Success = true, ShipPhase = PowerForgeAppleShipPhase.Release,
            Targets = [new() { Name = "iOS", BuildProcessingState = "VALID", TestFlightExternalState = "READY_FOR_BETA_SUBMISSION", TestFlightReviewState = reviewState }]
        };
        Assert.True(AppleTestFlightWorker.IsExternalReviewPending(receipt, policy));
        Assert.Equal(AppleTestFlightJobStage.AppleProcessingPending, AppleTestFlightWorker.DeliveryOutcome(receipt, policy).Stage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExternalDelivery_DoesNotHideSiblingDeliveryFailureBehindReview(bool perTargetError)
    {
        var policy = new AppleTestFlightWorkerPolicy
        {
            BetaGroupIsInternal = false, Targets = [new() { Name = "iOS" }, new() { Name = "Mac" }]
        };
        var receipt = new PowerForgeAppleReleaseReceipt
        {
            Success = false, ShipPhase = PowerForgeAppleShipPhase.Release, ErrorMessage = "Group assignment readback failed",
            Targets =
            [
                new() { Name = "iOS", BuildProcessingState = "VALID", TestFlightExternalState = "IN_BETA_REVIEW" },
                new() { Name = "Mac", BuildProcessingState = "VALID", ErrorMessage = perTargetError ? "Group assignment readback failed" : null }
            ]
        };
        var outcome = AppleTestFlightWorker.DeliveryOutcome(receipt, policy);
        Assert.Equal(AppleTestFlightJobStage.NeedsOperator, outcome.Stage);
        Assert.Contains("Group assignment readback failed", outcome.Diagnostic);
    }

    [Fact]
    public void ExternalDelivery_StillWaitsForProcessingTimeout()
    {
        var policy = new AppleTestFlightWorkerPolicy { BetaGroupIsInternal = false, Targets = [new() { Name = "iOS" }] };
        var receipt = new PowerForgeAppleReleaseReceipt
        {
            Success = false, ShipPhase = PowerForgeAppleShipPhase.Release, ErrorMessage = "Timed out waiting for processing",
            Targets = [new() { Name = "iOS", BuildProcessingState = "PROCESSING", ErrorMessage = "Timed out waiting for processing" }]
        };
        Assert.Equal(AppleTestFlightJobStage.AppleProcessingPending, AppleTestFlightWorker.DeliveryOutcome(receipt, policy).Stage);
    }

    [Theory]
    [InlineData("READY_FOR_BETA_SUBMISSION", (int)AppleTestFlightJobStage.NeedsOperator, false)]
    [InlineData("BETA_REJECTED", (int)AppleTestFlightJobStage.NeedsOperator, false)]
    [InlineData("MISSING_EXPORT_COMPLIANCE", (int)AppleTestFlightJobStage.NeedsOperator, false)]
    [InlineData("IN_BETA_REVIEW", (int)AppleTestFlightJobStage.AppleProcessingPending, true)]
    [InlineData("WAITING_FOR_BETA_REVIEW", (int)AppleTestFlightJobStage.AppleProcessingPending, true)]
    public void ExternalDelivery_DistinguishesOperatorActionFromAppleReview(string state, int expectedStage, bool reviewPending)
    {
        var policy = new AppleTestFlightWorkerPolicy { BetaGroupIsInternal = false, Targets = [new() { Name = "iOS" }] };
        var receipt = new PowerForgeAppleReleaseReceipt
        {
            Success = true, ShipPhase = PowerForgeAppleShipPhase.Release,
            Targets = [new() { Name = "iOS", BuildProcessingState = "VALID", TestFlightExternalState = state }]
        };
        var outcome = AppleTestFlightWorker.DeliveryOutcome(receipt, policy);
        Assert.Equal((AppleTestFlightJobStage)expectedStage, outcome.Stage);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Diagnostic));
        Assert.Equal(reviewPending, AppleTestFlightWorker.IsExternalReviewPending(receipt, policy));
    }

    [Fact]
    public void ExternalDelivery_ReportsHumanActionEvenWhenAnotherPlatformIsInReview()
    {
        var policy = new AppleTestFlightWorkerPolicy
        {
            BetaGroupIsInternal = false, Targets = [new() { Name = "iOS" }, new() { Name = "Mac" }]
        };
        var receipt = new PowerForgeAppleReleaseReceipt
        {
            Success = true, ShipPhase = PowerForgeAppleShipPhase.Release,
            Targets =
            [
                new() { Name = "iOS", BuildProcessingState = "VALID", TestFlightExternalState = "IN_BETA_REVIEW" },
                new() { Name = "Mac", BuildProcessingState = "VALID", TestFlightExternalState = "READY_FOR_BETA_SUBMISSION" }
            ]
        };
        Assert.True(AppleTestFlightWorker.IsExternalReviewPending(receipt, policy));
        var outcome = AppleTestFlightWorker.DeliveryOutcome(receipt, policy);
        Assert.Equal(AppleTestFlightJobStage.NeedsOperator, outcome.Stage);
        Assert.Contains("operator action", outcome.Diagnostic);
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("FAILED")]
    public void ExternalDelivery_DoesNotHideProcessingFailureBehindAnotherPlatformsReview(string processingState)
    {
        var policy = new AppleTestFlightWorkerPolicy
        {
            BetaGroupIsInternal = false, Targets = [new() { Name = "iOS" }, new() { Name = "Mac" }]
        };
        var receipt = new PowerForgeAppleReleaseReceipt
        {
            Success = false, ShipPhase = PowerForgeAppleShipPhase.Release,
            Targets =
            [
                new() { Name = "iOS", BuildProcessingState = "VALID", TestFlightExternalState = "IN_BETA_REVIEW" },
                new() { Name = "Mac", BuildProcessingState = processingState, ErrorMessage = "Apple rejected the uploaded Mac binary" }
            ]
        };
        var outcome = AppleTestFlightWorker.DeliveryOutcome(receipt, policy);
        Assert.Equal(AppleTestFlightJobStage.NeedsOperator, outcome.Stage);
        Assert.Contains("Mac: Apple rejected the uploaded Mac binary", outcome.Diagnostic);
    }

    [Fact]
    public void ExternalPlan_RequiresMatchingLocalAudienceAndStillRejectsAppStoreReview()
    {
        var policy = new AppleTestFlightWorkerPolicy
        {
            BetaGroupIsInternal = false,
            Targets = [new() { Name = "iOS" }]
        };
        var job = new AppleTestFlightJob { Source = "expected" };
        var plan = new PowerForgeAppleReleaseReceipt
        {
            Success = true, PlanOnly = true, Action = PowerForgeAppleReleaseAction.Ship,
            SourceCommit = "expected", PlanSha256 = new string('a', 64), Versioning = new(),
            Targets = [new() { Name = "iOS", ShipToTestFlight = true, TestFlightPolicy = AppleTestFlightPolicy.External }]
        };
        AppleTestFlightWorker.ValidatePlan(plan, job, policy);
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.ValidatePlan(plan, job));
        policy.BetaGroupIsInternal = true;
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.ValidatePlan(plan, job, policy));
        policy.BetaGroupIsInternal = false;
        plan.Targets[0].ShipToAppStoreReview = true;
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.ValidatePlan(plan, job, policy));
    }

    [Theory]
    [InlineData((int)PowerForgeAppleShipPhase.VersionCheckpoint, "2.3", "30")]
    [InlineData((int)PowerForgeAppleShipPhase.Release, "2.4", "30")]
    [InlineData((int)PowerForgeAppleShipPhase.Release, "2.3", "31")]
    public void ExternalReview_DoesNotPermitRecoveryToRebindTheCheckpoint(int phase, string version, string build)
    {
        var job = new AppleTestFlightJob { Stage = AppleTestFlightJobStage.AppleProcessingPending, Version = "2.3", Build = "30" };
        var plan = new PowerForgeAppleReleaseReceipt
        {
            ShipPhase = (PowerForgeAppleShipPhase)phase,
            Versioning = new() { MarketingVersion = version, BuildNumber = build },
            Targets = [new() { Name = "iOS", BuildProcessingState = "VALID", TestFlightExternalState = "IN_BETA_REVIEW" }]
        };
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.ValidateReleaseRecoveryPlan(plan, job));
    }

    [Fact]
    public void DeliveredExternalJob_DoesNotBlockTheNextSelectedBuild()
    {
        var completed = new AppleTestFlightJob { Id = "completed", Stage = AppleTestFlightJobStage.ReadyInTestFlight };
        var next = new AppleTestFlightJob { Id = "next", Stage = AppleTestFlightJobStage.Queued };
        var state = new AppleTestFlightWorkerState { Jobs = [completed, next] };
        Assert.Same(next, AppleTestFlightWorker.SelectRunnableJob(state));
        Assert.Same(next, AppleTestFlightWorker.SelectManualBehindHeldIntent(state));
        Assert.Equal("success", AppleTestFlightWorker.StatusFor(completed.Stage));
    }
}
