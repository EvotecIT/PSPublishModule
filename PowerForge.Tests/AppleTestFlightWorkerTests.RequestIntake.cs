using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleTestFlightWorkerTests
{
    [Theory]
    [InlineData("workflow")]
    [InlineData("actor")]
    [InlineData("artifact")]
    [InlineData("metadata")]
    [InlineData("json")]
    [InlineData("number")]
    [InlineData("operation")]
    public void RejectedRequestIsDurableAndCannotBlockLaterOff(string defect)
    {
        var root = Sandbox();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var state = new AppleTestFlightWorkerState { Mode = AppleTestFlightAutomaticMode.EveryMergedPR };
            var path = Path.Combine(root, "queue.json");
            var reads = 0;
            AppleTestFlightRequestRecord Read(JsonElement run)
            {
                reads++;
                if (run.GetProperty("id").GetInt64() == 1)
                {
                    if (defect == "json") throw new JsonException("malformed artifact");
                    if (defect == "number") throw new FormatException("invalid PR");
                    if (defect != "operation") throw new InvalidOperationException("invalid " + defect);
                    return new() { Key = "run:1", Operation = "Unsupported" };
                }
                return new() { Key = "run:2", Operation = "Automatic: Off", RecordedAt = now };
            }
            var runs = new[] { RequestRun(1, 1, now.AddMinutes(-2)), RequestRun(2, 1, now.AddMinutes(-1)) };
            AppleTestFlightWorker.ConsumeRequestRuns(state, runs, now, Read, () => AppleReleaseAppState.AtomicWrite(path, state));
            state = JsonSerializer.Deserialize<AppleTestFlightWorkerState>(File.ReadAllText(path), AppleTestFlightWorker.Json)!;
            Assert.Equal(AppleTestFlightAutomaticMode.Off, state.Mode);
            Assert.Empty(state.Jobs);
            Assert.Equal("Rejected", state.Requests[0].Operation);
            Assert.NotEmpty(state.Requests[0].Diagnostic!);
            AppleTestFlightWorker.ConsumeRequestRuns(state, runs, now, Read, () => throw new Exception("Unexpected replay"));
            Assert.Equal(2, reads);
        }
        finally { DeleteSandbox(root); }
    }

    [Fact]
    public void FailedOrStuckAttemptsPermitOneSuccessfulRerunAndPreserveIntentOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AppleTestFlightWorkerState();
        var initial = new[] { RequestRun(1, 1, now.AddMinutes(-20), conclusion: "failure"),
            RequestRun(2, 1, now.AddMinutes(-15), status: "in_progress"), RequestRun(3, 1, now.AddMinutes(-1)) };
        AppleTestFlightRequestRecord Read(JsonElement run) => new() { Key = "run:" + run.GetProperty("id").GetInt64(),
            Operation = run.GetProperty("id").GetInt64() == 3 ? "Automatic: Off" : "Build now" };
        AppleTestFlightWorker.ConsumeRequestRuns(state, initial, now, Read, () => { });
        Assert.Equal(2, state.Requests.Count(value => value.Operation == "Rejected"));
        Assert.Empty(state.Jobs);
        // Run ids stay unchanged across reruns; the attempt start orders this new intent after Off.
        AppleTestFlightWorker.ConsumeRequestRuns(state, new[] { RequestRun(1, 2, now) }, now, Read, () => { });
        AppleTestFlightWorker.ConsumeRequestRuns(state, new[] { RequestRun(1, 3, now.AddMinutes(1)) }, now.AddMinutes(1), Read, () => { });
        Assert.Single(state.Jobs);
        Assert.Equal(AppleTestFlightAutomaticMode.Off, state.Mode);
        Assert.Single(state.Requests, value => value.Key == "run:1");
    }

    [Fact]
    public void IncompleteRequestIsBoundedButTransientObservationFailureRemainsRetryable()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new AppleTestFlightWorkerState();
        var runs = new[] { RequestRun(1, 1, now, status: "queued"), RequestRun(2, 1, now.AddSeconds(1)) };
        AppleTestFlightWorker.ConsumeRequestRuns(state, runs, now, _ => throw new Exception("Must preserve order"), () => { });
        Assert.Empty(state.Requests);
        Assert.Throws<AppleTestFlightToolException>(() => AppleTestFlightWorker.ConsumeRequestRuns(state,
            new[] { RequestRun(1, 1, now) }, now, _ => throw new AppleTestFlightToolException("network unavailable"), () => { }));
        Assert.Empty(state.Requests);
        Assert.Equal(0, state.LastRequestRun);
    }

    [Theory]
    [InlineData((int)AppleTestFlightJobStage.Excluded, "error")]
    [InlineData((int)AppleTestFlightJobStage.ReadyInHome, "success")]
    [InlineData((int)AppleTestFlightJobStage.NeedsOperator, "failure")]
    [InlineData((int)AppleTestFlightJobStage.WaitingForChecks, "pending")]
    public void TerminalStatusMatchesDeliveryOutcome(int stage, string expected) =>
        Assert.Equal(expected, AppleTestFlightWorker.StatusFor((AppleTestFlightJobStage)stage));

    [Fact]
    public void SplitCancellationRetainsSourceAndSchedulesTerminalStatusRetry()
    {
        var state = new AppleTestFlightWorkerState();
        AppleTestFlightWorker.EnqueueAutomaticRequest(state, new() { Key = "pr:1:a", PullRequest = 1 });
        AppleTestFlightWorker.EnqueueAutomaticRequest(state, new() { Key = "pr:2:b", PullRequest = 2 });
        state.Jobs[0].Source = new string('a', 40);
        var cancelled = Assert.Single(AppleTestFlightWorker.ExcludeUnstartedAutomaticRequests(state, new HashSet<string> { "pr:1:a" }));
        Assert.Equal(state.Jobs[0].Source, cancelled.Source);
        Assert.True(cancelled.StatusPending);
        Assert.Equal("error", AppleTestFlightWorker.StatusFor(cancelled.Stage));
    }

    private static JsonElement RequestRun(long id, int attempt, DateTimeOffset started, string status = "completed", string conclusion = "success") =>
        JsonSerializer.SerializeToElement(new { id, run_attempt = attempt, run_started_at = started, created_at = started.AddHours(-1), status, conclusion });

    private static void DeleteSandbox(string path) { try { Directory.Delete(path, true); } catch { /* Preserve primary assertion failures. */ } }
}
