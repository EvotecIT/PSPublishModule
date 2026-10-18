namespace PowerForge.Tests;

public sealed partial class AppleTestFlightReadinessTests
{
    [Fact]
    public void WorkerBetaLifecycle_UsesCanonicalPlansAndExactBuildsThroughReviewAndGroupReadback()
    {
        var spec = Spec();
        spec.AppleApps!.Apps = [App("Sample iOS", ApplePlatform.iOS), App("Sample Mac", ApplePlatform.macOS)];
        var policy = new AppleTestFlightWorkerPolicy
        {
            SubmitBetaReviewAutomatically = true, BetaGroupIsInternal = false,
            BetaGroupId = "external-group", BetaGroupName = "Existing testers",
            Targets = spec.AppleApps.Apps.Select(value => new AppleTestFlightWorkerTarget
            { Name = value.Name!, BundleId = value.BundleId!, AppId = value.AppStoreConnectAppId!, Platform = value.Platform }).ToArray()
        };
        var job = new AppleTestFlightJob { Source = new string('a', 40), Version = "1.0.0", Build = "9" };
        var reviews = new Dictionary<ApplePlatform, string>();
        var assignments = new HashSet<ApplePlatform>();
        var submitted = new List<ApplePlatform>();
        var service = Service(request =>
        {
            var state = State(request, "VALID", false);
            var platform = state.Platforms.Single();
            platform.MatchedBuild!.Id = "build-" + platform.Platform;
            platform.BetaDetail = new()
            {
                InternalBuildState = "IN_BETA_TESTING",
                ExternalBuildState = assignments.Contains(platform.Platform) ? "IN_BETA_TESTING" :
                    reviews.TryGetValue(platform.Platform, out var review) ? review == "APPROVED" ? "BETA_APPROVED" : "WAITING_FOR_BETA_REVIEW" : "READY_FOR_BETA_SUBMISSION"
            };
            if (reviews.TryGetValue(platform.Platform, out var reviewState))
                platform.BetaReviewSubmission = new() { Id = "review-" + platform.Platform, BetaReviewState = reviewState };
            return state;
        }, request =>
        {
            Assert.Equal(job.Version, request.VersionString);
            Assert.Equal(job.Build, request.BuildNumber);
            submitted.Add(request.Platform);
            reviews[request.Platform] = "WAITING_FOR_REVIEW";
            return new() { Build = new() { Id = "build-" + request.Platform, ProcessingState = "VALID", Expired = false },
                Submission = new() { Id = "review-" + request.Platform, BetaReviewState = "WAITING_FOR_REVIEW" } };
        }, request =>
        {
            Assert.Equal(new[] { policy.BetaGroupId }, request.BetaGroupIds);
            Assert.Empty(request.Testers);
            assignments.Add(request.Platform);
            return new() { Build = new() { Id = "build-" + request.Platform, ProcessingState = "VALID", Expired = false },
                VerifiedBetaGroups = [new() { Id = policy.BetaGroupId, Name = policy.BetaGroupName, IsInternalGroup = false }] };
        });

        PowerForgeReleaseResult Execute(PowerForgeAppleReleaseAction action, bool plan, string[] targets, string? hash = null)
            => service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(_root, "release.json"), AppleAction = action, PlanOnly = plan,
                Targets = targets, AppleSourceCommit = job.Source, AppleActionConfirmed = !plan,
                AppleExpectedPlanSha256 = hash
            });
        var names = policy.Targets.Select(value => value.Name).ToArray();
        var status = Execute(PowerForgeAppleReleaseAction.Status, false, names);
        Assert.True(status.Success, status.ErrorMessage);
        foreach (var value in status.AppleReceipt!.Targets) job.BuildIds[value.Name] = value.BuildId!;
        AppleTestFlightWorker.ValidateExistingBetaReceipt(status.AppleReceipt, job, policy, names, requireValid: true);
        var next = AppleTestFlightWorker.NextBetaAction(status.AppleReceipt, policy);
        Assert.Equal(PowerForgeAppleReleaseAction.SubmitTestFlightReview, next.Action);
        var preview = Execute(next.Action!.Value, true, next.Targets);
        Assert.True(preview.Success, preview.ErrorMessage);
        AppleTestFlightWorker.ValidateBetaPlan(preview.AppleReceipt!, job, policy, next.Action.Value, next.Targets);
        var reviewResult = Execute(next.Action.Value, false, next.Targets, preview.AppleReceipt!.PlanSha256);
        Assert.True(reviewResult.Success, reviewResult.ErrorMessage);
        Assert.Equal(2, submitted.Count);
        Assert.Null(AppleTestFlightWorker.NextBetaAction(reviewResult.AppleReceipt!, policy).Action);
        Assert.Empty(assignments);

        foreach (var platform in submitted) reviews[platform] = "APPROVED";
        var approved = Execute(PowerForgeAppleReleaseAction.Status, false, names);
        next = AppleTestFlightWorker.NextBetaAction(approved.AppleReceipt!, policy);
        Assert.Equal(PowerForgeAppleReleaseAction.TestFlight, next.Action);
        preview = Execute(next.Action!.Value, true, next.Targets);
        AppleTestFlightWorker.ValidateBetaPlan(preview.AppleReceipt!, job, policy, next.Action.Value, next.Targets);
        var delivery = Execute(next.Action.Value, false, next.Targets, preview.AppleReceipt!.PlanSha256);
        Assert.True(delivery.Success, delivery.ErrorMessage);
        AppleTestFlightWorker.ValidateExistingBetaReceipt(delivery.AppleReceipt!, job, policy, names, requireValid: true);
        Assert.True(AppleTestFlightWorker.IsExistingBetaReady(delivery.AppleReceipt!, job, policy));
        Assert.Equal(2, submitted.Count);
        Assert.Equal(2, assignments.Count);
        Assert.False(delivery.AppleAppPlan!.PrepareDistribution);
        Assert.False(delivery.AppleAppPlan.SubmitForReview);
        Assert.False(delivery.AppleAppPlan.ReleaseApprovedVersion);
    }
}
