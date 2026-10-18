namespace PowerForge.Tests;

public sealed partial class AppleTestFlightWorkerTests
{
    private static (AppleTestFlightWorkerPolicy Policy, AppleTestFlightJob Job, PowerForgeAppleReleaseReceipt Receipt) AutomaticBeta()
    {
        var policy = new AppleTestFlightWorkerPolicy
        {
            SubmitBetaReviewAutomatically = true, BetaGroupIsInternal = false, ReleaseHoldLabel = "testflight-skip",
            BetaGroupId = "existing-external", BetaGroupName = "Existing testers",
            Targets = [new() { Name = "Phone", BundleId = "com.example.phone", AppId = "app", Platform = ApplePlatform.iOS },
                       new() { Name = "Mac", BundleId = "com.example.mac", AppId = "app", Platform = ApplePlatform.macOS }]
        };
        var job = new AppleTestFlightJob { Source = new string('a', 40), Version = "2.3", Build = "31",
            BuildIds = new() { ["Phone"] = "phone31", ["Mac"] = "mac31" } };
        var receipt = new PowerForgeAppleReleaseReceipt
        {
            SourceCommit = job.Source, Action = PowerForgeAppleReleaseAction.Status, Success = true,
            Targets = policy.Targets.Select(target => new PowerForgeAppleReleaseTargetReceipt
            {
                Name = target.Name, BundleId = target.BundleId, AppId = target.AppId, Platform = target.Platform,
                TestFlightPolicy = AppleTestFlightPolicy.External, Version = job.Version, Build = job.Build,
                BuildId = job.BuildIds[target.Name], BuildProcessingState = "VALID", BuildExpired = false,
                TestFlightExternalState = "READY_FOR_BETA_SUBMISSION"
            }).ToArray()
        };
        return (policy, job, receipt);
    }

    [Fact]
    public void AutomaticBeta_SubmitsOnlyTheSiblingStillNeedingReview_AndDoesNotRepeatPendingReview()
    {
        var (policy, job, receipt) = AutomaticBeta();
        receipt.Targets[0].TestFlightReviewState = "WAITING_FOR_REVIEW";
        // Stale external state is subordinate to an actual pending submission.
        var action = AppleTestFlightWorker.NextBetaAction(receipt, policy);
        Assert.Equal(PowerForgeAppleReleaseAction.SubmitTestFlightReview, action.Action);
        Assert.Equal(new[] { "Mac" }, action.Targets);
        receipt.Targets[1].TestFlightExternalState = "IN_BETA_REVIEW";
        Assert.Null(AppleTestFlightWorker.NextBetaAction(receipt, policy).Action);
        AppleTestFlightWorker.ValidateExistingBetaReceipt(receipt, job, policy, ["Phone", "Mac"], requireValid: true);
    }

    [Theory]
    [InlineData("BETA_REJECTED")]
    [InlineData("MISSING_EXPORT_COMPLIANCE")]
    public void AutomaticBeta_NeverHidesHumanActionBehindSiblingReview(string state)
    {
        var (policy, _, receipt) = AutomaticBeta();
        receipt.Targets[0].TestFlightExternalState = "IN_BETA_REVIEW";
        receipt.Targets[1].TestFlightExternalState = state;
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.NextBetaAction(receipt, policy));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void AutomaticBeta_RequiresKnownNonexpiryEvenWhenProcessingIsValid(bool? expired)
    {
        var (policy, _, receipt) = AutomaticBeta();
        receipt.Targets[1].BuildExpired = expired;
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.NextBetaAction(receipt, policy));
    }

    [Fact]
    public void AutomaticBeta_WaitsForProcessing_ButInvalidSiblingRequiresOperator()
    {
        var (policy, _, receipt) = AutomaticBeta();
        receipt.Targets[0].TestFlightExternalState = "BETA_APPROVED";
        receipt.Targets[1].BuildProcessingState = "PROCESSING";
        Assert.Null(AppleTestFlightWorker.NextBetaAction(receipt, policy).Action);
        receipt.Targets[1].BuildProcessingState = "INVALID";
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.NextBetaAction(receipt, policy));
    }

    [Fact]
    public void AutomaticBeta_OptInDoesNotBroadenExistingPoliciesOrPublicReview()
    {
        var (policy, job, receipt) = AutomaticBeta();
        policy.SubmitBetaReviewAutomatically = false;
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.NextBetaAction(receipt, policy));
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.BetaArguments(policy, job,
            PowerForgeAppleReleaseAction.SubmitTestFlightReview, ["Phone"], plan: false));
        foreach (var action in new[] { PowerForgeAppleReleaseAction.Ship, PowerForgeAppleReleaseAction.Version,
            PowerForgeAppleReleaseAction.Archive, PowerForgeAppleReleaseAction.Upload, PowerForgeAppleReleaseAction.SubmitAppReview, PowerForgeAppleReleaseAction.Release })
            Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.BetaArguments(policy, job, action, ["Phone"], plan: true));
    }

    [Fact]
    public void AutomaticBeta_CanonicalCommandRetainsFullUploadedSourceAndReviewedHash()
    {
        var (policy, job, _) = AutomaticBeta();
        policy.DefaultBranch = "master";
        job.BetaPlanSha256 = new string('b', 64);
        var arguments = AppleTestFlightWorker.BetaArguments(policy, job, PowerForgeAppleReleaseAction.TestFlight, ["Phone", "Mac"], plan: false);
        Assert.Equal(job.Source, arguments[Array.IndexOf(arguments, "--apple-source-commit") + 1]);
        Assert.Equal(job.BetaPlanSha256, arguments[Array.IndexOf(arguments, "--apple-expected-plan-sha256") + 1]);
        Assert.Equal("master", arguments[Array.IndexOf(arguments, "-ExpectedConsumerBranch") + 1]);
        Assert.Contains("--confirm-apple-action", arguments);
        Assert.DoesNotContain("--apple-adopt-existing-build", arguments);
        Assert.DoesNotContain("--apple-app-store-target", arguments);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("build")]
    [InlineData("id")]
    [InlineData("platform")]
    [InlineData("bundle")]
    [InlineData("upload")]
    [InlineData("versioning")]
    [InlineData("targets")]
    public void AutomaticBeta_RejectsChangedCandidateBeforeConfirmation(string changed)
    {
        var (policy, job, receipt) = AutomaticBeta();
        receipt.Action = PowerForgeAppleReleaseAction.SubmitTestFlightReview;
        receipt.PlanOnly = true;
        receipt.PlanSha256 = new string('b', 64);
        switch (changed)
        {
            case "source": receipt.SourceCommit = new string('c', 40); break;
            case "build": receipt.Targets[0].Build = "32"; break;
            case "id": receipt.Targets[0].BuildId = "different"; break;
            case "platform": receipt.Targets[0].Platform = ApplePlatform.macOS; break;
            case "bundle": receipt.Targets[0].BundleId = "another.bundle"; break;
            case "upload": receipt.Targets[0].UploadPerformed = true; break;
            case "versioning": receipt.Versioning = new() { BuildNumber = "32" }; break;
            case "targets": receipt.Targets = [receipt.Targets[0]]; break;
        }
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.ValidateBetaPlan(receipt, job, policy,
            PowerForgeAppleReleaseAction.SubmitTestFlightReview, ["Phone", "Mac"]));
    }

    [Fact]
    public void AutomaticBeta_ReadinessRequiresAllPlatformsAndActualOriginalGroupReadback()
    {
        var (policy, job, receipt) = AutomaticBeta();
        foreach (var target in receipt.Targets)
        {
            target.TestFlightExternalState = "IN_BETA_TESTING";
            // Public review information is observation, not a public mutation.
            target.AppReviewSubmissionId = "public-submission";
        }
        Assert.Equal(PowerForgeAppleReleaseAction.TestFlight, AppleTestFlightWorker.NextBetaAction(receipt, policy).Action);
        Assert.False(AppleTestFlightWorker.IsExistingBetaReady(receipt, job, policy));
        foreach (var target in receipt.Targets)
            target.DistributedTestFlightBetaGroups = [new() { Id = policy.BetaGroupId, Name = policy.BetaGroupName, IsInternalGroup = false }];
        Assert.True(AppleTestFlightWorker.IsExistingBetaReady(receipt, job, policy));
        receipt.Targets[1].DistributedTestFlightBetaGroups[0].Id = "another-group";
        Assert.False(AppleTestFlightWorker.IsExistingBetaReady(receipt, job, policy));
    }

    [Theory]
    [InlineData((int)AppleTestFlightJobStage.SubmittingBetaReview)]
    [InlineData((int)AppleTestFlightJobStage.DistributingTestFlight)]
    public void AutomaticBeta_DefinitivePremutationRejectionRetriesExistingBuild_AmbiguousEntryHolds(int stage)
    {
        Assert.Equal(AppleTestFlightJobStage.AppleProcessingPending,
            AppleTestFlightWorker.FailureStage((AppleTestFlightJobStage)stage, new AppleTestFlightPreMutationRejectedException()));
        Assert.Equal(AppleTestFlightJobStage.NeedsOperator,
            AppleTestFlightWorker.FailureStage((AppleTestFlightJobStage)stage, new AppleTestFlightToolException("connection lost after entry")));
    }

    [Fact]
    public void ConfiguredSkipLabelRetainsIntentUntilCleared_LegacySkipRemainsCompatible()
    {
        var policy = new AppleTestFlightWorkerPolicy { ReleaseHoldLabel = "testflight-skip" };
        Assert.False(AppleTestFlightWorker.ExcludesAutomaticTrigger(["testflight-skip"], policy));
        Assert.False(AppleTestFlightWorker.ExcludesAutomaticTrigger([], policy));
        Assert.True(AppleTestFlightWorker.ExcludesAutomaticTrigger(["testflight-skip"], new()));
        Assert.True(AppleTestFlightWorker.CompareContainsMerge("ahead"));
        Assert.True(AppleTestFlightWorker.CompareContainsMerge("identical"));
        Assert.False(AppleTestFlightWorker.CompareContainsMerge("behind"));
        Assert.False(AppleTestFlightWorker.CompareContainsMerge("diverged"));
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.CompareContainsMerge("unknown"));
    }

    [Fact]
    public void ReleaseHoldCoversOlderMergedCode_NotOnlyTheCurrentTrigger_AndClearingResumes()
    {
        var policy = new AppleTestFlightWorkerPolicy { Repository = "owner/app", DefaultBranch = "main", ReleaseHoldLabel = "testflight-skip" };
        var source = new string('b', 40);
        var merge = new string('a', 40);
        var issue = System.Text.Json.JsonSerializer.SerializeToElement(new { number = 1, pull_request = new { } });
        var pull = System.Text.Json.JsonSerializer.SerializeToElement(new { merged = true, merge_commit_sha = merge,
            @base = new { @ref = "main", repo = new { full_name = "owner/app" } } });
        Assert.Equal(new[] { 1 }, AppleTestFlightWorker.FindReleaseHolds(policy, source, [issue], _ => pull, (older, candidate) =>
        {
            Assert.Equal(merge, older);
            Assert.Equal(source, candidate);
            return "ahead";
        }));
        Assert.Empty(AppleTestFlightWorker.FindReleaseHolds(policy, source, [], _ => throw new Exception("no labelled PR"), (_, _) => "ahead"));
        Assert.Empty(AppleTestFlightWorker.FindReleaseHolds(policy, source, [issue], _ => pull, (_, _) => "behind"));
        Assert.Throws<AppleTestFlightToolException>(() => AppleTestFlightWorker.FindReleaseHolds(policy, source, [issue],
            _ => pull, (_, _) => throw new AppleTestFlightToolException("GitHub unavailable")));
    }
}
