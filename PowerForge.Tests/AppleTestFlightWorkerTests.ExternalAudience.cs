namespace PowerForge.Tests;

public sealed partial class AppleTestFlightWorkerTests
{
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
