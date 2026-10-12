using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleTestFlightWorkerTests
{
    [Fact]
    public void ReservationSourceCasingDoesNotChangeTheReservedBuildIdentity()
    {
        var root = Sandbox();
        try
        {
            var plan = Plan(new string('a', 40), "checkout");
            using (var lease = AppleReleaseAppState.Acquire(plan, root)) lease.Reserve(plan, "25");
            plan.SourceCommit = plan.SourceCommit!.ToUpperInvariant();
            using (var lease = AppleReleaseAppState.Acquire(plan, root)) lease.Reserve(plan, "25");
            Assert.Equal(0, AppleReleaseAppState.HighestReserved(plan, plan.SourceCommit, 25, root));
            Assert.Single(JsonSerializer.Deserialize<AppleReleaseAppState.Reservation[]>(File.ReadAllText(Directory.GetFiles(root, "reservations.json", SearchOption.AllDirectories).Single()))!);
        }
        finally { DeleteSandbox(root); }
    }

    [Fact]
    public void StateDirectoryIoFailureIsNotAProvenLeaseSharingConflict()
    {
        var root = Sandbox();
        try
        {
            File.WriteAllText(Path.Combine(root, "apps"), "state directory obstructed");
            var error = Assert.ThrowsAny<IOException>(() => AppleReleaseAppState.Acquire(Plan("source", "checkout"), root));
            Assert.IsNotType<AppleReleaseLeaseContentionException>(error);
        }
        finally { DeleteSandbox(root); }
    }

    [Theory]
    [InlineData((int)AppleDistributionRoute.AppStore)]
    [InlineData((int)AppleDistributionRoute.DirectNotarized)]
    public void MutationRequiresAStableBundleLeaseIdentityEvenWithAnExplicitAppId(int route)
    {
        var root = Sandbox();
        try
        {
            var plan = Plan("source", "checkout");
            plan.Apps[0].BundleId = null;
            plan.Apps[0].AppStoreConnectAppId = "123";
            plan.Apps[0].DistributionRoute = (AppleDistributionRoute)route;
            var error = Assert.Throws<InvalidOperationException>(() => AppleReleaseAppState.Acquire(plan, root));
            Assert.Contains("BundleId", error.Message);
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally { DeleteSandbox(root); }
    }

    [Fact]
    public void AClosedGateAfterMutationIsNeverAProvenEntryRejection()
    {
        var root = Sandbox();
        try
        {
            var plan = Plan("source", "checkout");
            using (AppleReleaseAppState.Acquire(plan, root)) { }
            File.WriteAllText(Path.Combine(root, "apps", AppleReleaseAppState.Hash(plan.Apps[0].BundleId!), "paused"), "operator pause");
            Assert.True(Assert.Throws<AppleReleaseGateClosedException>(() => AppleReleaseAppState.AssertMutationAllowed(plan, root, beforeMutation: true)).BeforeMutation);
            Assert.False(Assert.Throws<AppleReleaseGateClosedException>(() => AppleReleaseAppState.AssertMutationAllowed(plan, root)).BeforeMutation);
            Assert.False(AppleTestFlightWorker.IsPreMutationRejection(1, "{\"command\":\"apple-release\",\"success\":false,\"exitCode\":1,\"errorCode\":\"AppleMutationGateClosed\"}"));
            Assert.Equal(AppleTestFlightJobStage.NeedsOperator, AppleTestFlightWorker.FailureStage(AppleTestFlightJobStage.Releasing, new AppleReleaseGateClosedException("paused after archive", false)));
        }
        finally { DeleteSandbox(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumedHistoryStopsPaginationAndOlderFailedRerunsRemainEligible(bool upgradedJournal)
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AppleTestFlightWorkerState { LastScannedRequestRun = upgradedJournal ? 0 : 10001, LastRequestRun = 10001 };
        // More than100 full pages of durable history must not be fetched again.
        state.Requests = Enumerable.Range(2, 10000).Select(id => new AppleTestFlightRequestRecord { Key = "run:" + id, Operation = "Automatic: Off" }).ToList();
        state.Requests.Add(new() { Key = "run:1:attempt:1", Operation = "Rejected", Attempt = 1 });
        var pages = 0;
        JsonElement[] Page(int page)
        {
            pages++;
            return Enumerable.Range(9903, 100).Reverse().Select(id => RequestRun(id, 1, now.AddMinutes(-1))).ToArray();
        }
        var reads = new List<long>();
        var runs = AppleTestFlightWorker.ObserveRequestRuns(state, now.AddDays(-2), Page,
            id => { reads.Add(id); return RequestRun(id, 2, now); }, () => { });
        AppleTestFlightWorker.ConsumeRequestRuns(state, runs, now, run => new() { Key = "run:" + run.GetProperty("id").GetInt64(), Operation = run.GetProperty("id").GetInt64() == 1 ? "Build now" : "Automatic: Off" }, () => { });
        Assert.Equal(1, pages);
        Assert.Equal(new long[] { 1 }, reads);
        Assert.Single(state.Jobs);
        Assert.Equal(10002, state.LastScannedRequestRun);
        Assert.Empty(state.PendingRequestRuns);
        // The accepted old run is no longer polled, including another rerun.
        AppleTestFlightWorker.ObserveRequestRuns(state, now.AddDays(-2), Page, _ => throw new Exception("Accepted run polled again"), () => { });
    }

    [Fact]
    public void SavedPendingObservationSurvivesCursorAdvanceAndTransientReadFailure()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AppleTestFlightWorkerState();
        var runs = AppleTestFlightWorker.ObserveRequestRuns(state, now.AddDays(-2), _ => [RequestRun(2, 1, now, status: "queued"), RequestRun(1, 1, now.AddSeconds(-1))],
            _ => throw new Exception("New summaries already observed"), () => { });
        AppleTestFlightWorker.ConsumeRequestRuns(state, runs, now, _ => new() { Key = "run:1", Operation = "Automatic: Off" }, () => { });
        Assert.Equal(new long[] { 2 }, state.PendingRequestRuns);
        Assert.Throws<AppleTestFlightToolException>(() => AppleTestFlightWorker.ObserveRequestRuns(state, now.AddDays(-2), _ => [RequestRun(2, 1, now)],
            _ => throw new AppleTestFlightToolException("GitHub unavailable"), () => { }));
        Assert.Equal(new long[] { 2 }, state.PendingRequestRuns);
        runs = AppleTestFlightWorker.ObserveRequestRuns(state, now.AddDays(-2), _ => [RequestRun(2, 1, now)], id => RequestRun(id, 1, now), () => { });
        AppleTestFlightWorker.ConsumeRequestRuns(state, runs, now, _ => new() { Key = "run:2", Operation = "Build now" }, () => { });
        Assert.Empty(state.PendingRequestRuns);
        Assert.Single(state.Jobs);
    }

    [Fact]
    public void FailedAttemptPollingIsBoundedAndRotatesWithoutStarvingFreshOff()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AppleTestFlightWorkerState { LastScannedRequestRun = 25 };
        state.Requests = Enumerable.Range(1, 25).Select(id => new AppleTestFlightRequestRecord { Key = $"run:{id}:attempt:1", Operation = "Rejected" }).ToList();
        var readIds = new List<long>();
        JsonElement Read(long id) { readIds.Add(id); return RequestRun(id, 1, now.AddMinutes(-2), conclusion: "failure"); }
        var runs = AppleTestFlightWorker.ObserveRequestRuns(state, now.AddDays(-2), _ => [RequestRun(26, 1, now)], Read, () => { });
        AppleTestFlightWorker.ConsumeRequestRuns(state, runs, now, _ => new() { Key = "run:26", Operation = "Automatic: Off" }, () => { });
        Assert.Equal(AppleTestFlightAutomaticMode.Off, state.Mode);
        Assert.Equal(20, readIds.Count);
        readIds.Clear();
        AppleTestFlightWorker.ObserveRequestRuns(state, now.AddDays(-2), _ => [RequestRun(26, 1, now)], Read, () => { });
        Assert.Equal(20, readIds.Count);
        Assert.Contains(25, readIds);
        Assert.DoesNotContain(26, readIds);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateSuccessfulRerunCannotOverrideNewerOffAcrossIntakePasses(bool sameTimestamp)
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AppleTestFlightWorkerState { LastRequestRun = 25 };
        state.Requests = Enumerable.Range(1, 25).Select(id => new AppleTestFlightRequestRecord { Key = $"run:{id}:attempt:1", Operation = "Rejected" }).ToList();
        JsonElement Read(long id) => RequestRun(id, id == 25 ? 2 : 1, sameTimestamp ? now : now.AddSeconds(-1), conclusion: id == 25 ? "success" : "failure");
        AppleTestFlightRequestRecord Validate(JsonElement run) => new()
        {
            Key = "run:" + run.GetProperty("id").GetInt64(),
            RecordedAt = run.GetProperty("run_started_at").GetDateTimeOffset(),
            Operation = run.GetProperty("id").GetInt64() == 26 ? "Automatic: Off" : "Automatic: Every merged PR"
        };
        for (var pass = 0; pass < 2; pass++)
        {
            var runs = AppleTestFlightWorker.ObserveRequestRuns(state, now.AddDays(-2), _ => [RequestRun(26, 1, now)], Read, () => { });
            AppleTestFlightWorker.ConsumeRequestRuns(state, runs, now, Validate, () => { });
        }
        Assert.Contains(state.Requests, value => value.Key == "run:25" && value.Operation == "Automatic: Every merged PR");
        Assert.Equal(AppleTestFlightAutomaticMode.Off, state.Mode);
        Assert.Equal(now, state.ModeChangedAt);
        Assert.Equal(26, state.ModeChangedByRunId);
        AppleTestFlightWorker.ApplyRequest(state, new() { Key = "run:24", RecordedAt = now.AddSeconds(-2), Operation = "Build now" });
        Assert.Single(state.Jobs);
    }

    [Fact]
    public void OlderJournalModeTieBreakerAndSavedPendingIntentSurviveUpgrade()
    {
        var now = DateTimeOffset.UtcNow;
        // Missing new properties deserialize to defaults in an existing schema-1 journal.
        var state = JsonSerializer.Deserialize<AppleTestFlightWorkerState>(
            JsonSerializer.Serialize(new { LastRequestRun = 30, ModeChangedAt = now, Requests = new[] {
                new AppleTestFlightRequestRecord { Key = "run:30", Operation = "Automatic: Off", RecordedAt = now } } }), AppleTestFlightWorker.Json)!;
        var runs = AppleTestFlightWorker.ObserveRequestRuns(state, now.AddDays(-1), _ => [RequestRun(30, 1, now)],
            id => RequestRun(id, 1, now.AddSeconds(-1)), () => { },
            () => [RequestRun(29, 1, now.AddSeconds(-1), status: "queued")]);
        Assert.Contains(29, state.PendingRequestRuns);
        // Completion after upgrade is observed through the saved pending id.
        runs = AppleTestFlightWorker.ObserveRequestRuns(state, now.AddDays(-1), _ => [RequestRun(30, 1, now)],
            id => RequestRun(id, 1, now.AddSeconds(-1)), () => { });
        AppleTestFlightWorker.ConsumeRequestRuns(state, runs, now, _ => new() { Key = "run:29", Operation = "Build now", RecordedAt = now.AddSeconds(-1) }, () => { });
        Assert.Single(state.Jobs);
        Assert.Equal(30, state.LastScannedRequestRun);
        AppleTestFlightWorker.ApplyRequest(state, new() { Key = "run:28", Operation = "Automatic: Every merged PR", RecordedAt = now });
        Assert.Equal(AppleTestFlightAutomaticMode.Off, state.Mode);
        Assert.Equal(30, state.ModeChangedByRunId);
        AppleTestFlightWorker.ApplyRequest(state, new() { Key = "run:31", Operation = "Automatic: Selected PRs", RecordedAt = now });
        Assert.Equal(AppleTestFlightAutomaticMode.SelectedPRs, state.Mode);
    }

    [Fact]
    public void ClosedGateHoldsTheActualPublisherEntryBeforeAnyRepositoryCommand()
    {
        var root = Sandbox();
        try
        {
            var policy = new AppleTestFlightWorkerPolicy
            {
                Repository = "synthetic/app", ConsumerRoot = Directory.CreateDirectory(Path.Combine(root, "consumer")).FullName,
                OwnerRoot = FindRepository(), OwnerCommit = new string('a', 40), StateRoot = root, ScratchRoot = root,
                BaselineSourceCommit = new string('b', 40), AcceptRequestsAfter = DateTimeOffset.UtcNow.AddDays(-1),
                RequiredChecks = ["Synthetic check"], PublisherPaths = ["project.yml"], BetaGroupId = Guid.NewGuid().ToString(),
                Targets = [new() { Name = "App", BundleId = "com.example.publisher-held", AppId = "123" }]
            };
            // Inspect the existing private publisher boundary without creating an
            // operator enrollment or changing process-wide state-root settings.
            var worker = (AppleTestFlightWorker)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AppleTestFlightWorker));
            var job = new AppleTestFlightJob { Id = "held", Automatic = true, Stage = AppleTestFlightJobStage.CheckpointReady };
            var state = new AppleTestFlightWorkerState { Repository = policy.Repository, Jobs = [job], Mode = AppleTestFlightAutomaticMode.Off };
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var directory = Directory.CreateDirectory(Path.Combine(root, "queue")).FullName;
            foreach (var field in new[] { "_directory", "_journal", "_gate" })
                typeof(AppleTestFlightWorker).GetField(field, flags)!.SetValue(worker, field == "_directory" ? directory : Path.Combine(directory, field == "_journal" ? "queue.json" : "gate.json"));
            typeof(AppleTestFlightWorker).GetField("_policy", flags)!.SetValue(worker, policy);
            typeof(AppleTestFlightWorker).GetField("_state", flags)!.SetValue(worker, state);
            typeof(AppleTestFlightWorker).GetField("_running", flags)!.SetValue(worker, job);
            // No git checkout or network fixture exists: reaching RequireUnprotectedMain
            // or any commit/push command would fail rather than pass this boundary.
            typeof(AppleTestFlightWorker).GetMethod("PublishCheckpoint", flags)!.Invoke(worker, [job]);
            Assert.Equal(AppleTestFlightJobStage.CheckpointReady, job.Stage);
            Assert.Contains("held", job.Diagnostic);
            Assert.Empty(Directory.GetFileSystemEntries(policy.ConsumerRoot));
        }
        finally { DeleteSandbox(root); }
    }

}
