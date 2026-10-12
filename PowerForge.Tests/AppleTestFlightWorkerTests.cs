using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleTestFlightWorkerTests
{
    [Fact]
    public void AppLease_SerializesIndependentCheckouts_AndRetainsFailedDeliveryReservation()
    {
        var root = Sandbox();
        var first = Plan("source-a", "checkout-a");
        var second = Plan("source-b", "checkout-b");
        try
        {
            using (var lease = AppleReleaseAppState.Acquire(first, root))
            {
                lease.Reserve(first, "25");
                Assert.Throws<InvalidOperationException>(() => AppleReleaseAppState.Acquire(second, root));
            }
            // A restarted process must still advance past build 25, even when the
            // remote inventory reports only 22 and no successful upload receipt exists.
            Assert.Equal(25, AppleReleaseAppState.HighestReserved(second, second.SourceCommit, 22, root));
            Assert.Equal(0, AppleReleaseAppState.HighestReserved(first, first.SourceCommit, 25, root));
            using var resumed = AppleReleaseAppState.Acquire(first, root);
            resumed.Reserve(first, "25");
            Assert.Throws<InvalidOperationException>(() => resumed.Reserve(second, "25"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManualDottedBuildIdentity_IsReservedWithoutChangingIntegerVersionAllocation()
    {
        var root = Sandbox();
        try
        {
            var plan = Plan("source", "checkout");
            using var lease = AppleReleaseAppState.Acquire(plan, root);
            lease.Reserve(plan, "25.1.2");
            lease.Reserve(plan, "25.1.2");
            Assert.Throws<InvalidOperationException>(() => lease.Reserve(Plan("another-source", "another-checkout"), "25.1.2"));
            Assert.Equal(25, AppleReleaseAppState.HighestReserved(plan, "new-source", 22, root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AppLease_IsObservedByAnotherDotNetProcess()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/local/bin/pwsh")) return;
        var root = Sandbox();
        try
        {
            using (AppleReleaseAppState.Acquire(Plan("source", "checkout"), root))
            {
                var path = Directory.GetFiles(root, "operation.lock", SearchOption.AllDirectories).Single();
                using var process = new Process { StartInfo = AppleTestFlightWorker.CreateProcess("/usr/local/bin/pwsh", root,
                    new[] { "-NoProfile", "-NonInteractive", "-Command", "try { $s=[IO.File]::Open('" + path.Replace("'", "''") + "','Open','ReadWrite','None'); $s.Dispose(); exit 9 } catch [IO.IOException] { exit 0 }" }) };
                process.Start();
                Assert.True(process.WaitForExit(20000));
                Assert.Equal(0, process.ExitCode);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ModeJournal_IsIdempotent_AndBuildNowWorksWhileOff()
    {
        var state = new AppleTestFlightWorkerState();
        var selected = new AppleTestFlightRequestRecord { Key = "run:1", Operation = "Automatic: Selected PRs", RecordedAt = DateTimeOffset.UtcNow };
        AppleTestFlightWorker.ApplyRequest(state, selected);
        AppleTestFlightWorker.ApplyRequest(state, selected);
        AppleTestFlightWorker.ApplyRequest(state, new() { Key = "run:2", Operation = "Automatic: Off" });
        AppleTestFlightWorker.ApplyRequest(state, new() { Key = "run:3", Operation = "Build now", PullRequest = 351 });
        Assert.Equal(AppleTestFlightAutomaticMode.Off, state.Mode);
        Assert.Equal(3, state.Requests.Count);
        Assert.False(state.Jobs.Single().Automatic);
        Assert.Equal(new[] { 351 }, state.Jobs.Single().IncludedPullRequests);
        var root = Sandbox();
        try
        {
            var journal = Path.Combine(root, "queue.json");
            AppleReleaseAppState.AtomicWrite(journal, state);
            var resumed = JsonSerializer.Deserialize<AppleTestFlightWorkerState>(File.ReadAllText(journal), AppleTestFlightWorker.Json)!;
            AppleTestFlightWorker.ApplyRequest(resumed, selected);
            Assert.Equal(3, resumed.Requests.Count);
            Assert.Single(resumed.Jobs);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AdjacentAutomaticRequests_SharePendingCandidate_AndAmbiguityHoldsLaterRequests()
    {
        var state = new AppleTestFlightWorkerState { Mode = AppleTestFlightAutomaticMode.EveryMergedPR };
        AppleTestFlightWorker.EnqueueAutomaticRequest(state, new() { Key = "pr:1:a", PullRequest = 1 });
        AppleTestFlightWorker.EnqueueAutomaticRequest(state, new() { Key = "pr:2:b", PullRequest = 2 });
        AppleTestFlightWorker.EnqueueAutomaticRequest(state, new() { Key = "pr:2:b", PullRequest = 2 });
        var job = Assert.Single(state.Jobs);
        Assert.Equal(new[] { 1, 2 }, job.IncludedPullRequests);
        Assert.Equal(2, job.RequestKeys.Count);
        job.Stage = AppleTestFlightJobStage.Releasing;
        AppleTestFlightWorker.EnqueueAutomaticRequest(state, new() { Key = "pr:3:c", PullRequest = 3 });
        Assert.Equal(2, state.Jobs.Count);
        Assert.Same(job, AppleTestFlightWorker.SelectRunnableJob(state));
        job.Stage = AppleTestFlightJobStage.NeedsOperator;
        Assert.Null(AppleTestFlightWorker.SelectRunnableJob(state));
        job.Stage = AppleTestFlightJobStage.Excluded;
        Assert.Same(state.Jobs[1], AppleTestFlightWorker.SelectRunnableJob(state));
    }

    [Fact]
    public void OffOrLostHeartbeat_HoldsTheNextMutationStage()
    {
        var root = Sandbox();
        try
        {
            var gate = Path.Combine(root, "gate.json");
            var now = DateTimeOffset.UtcNow;
            AppleReleaseAppState.AtomicWrite(gate, new { Allowed = true, ExpiresAt = now.AddMinutes(1) });
            Assert.True(AppleReleaseAppState.GateAllowsMutation(gate, now));
            AppleReleaseAppState.AtomicWrite(gate, new { Allowed = false, ExpiresAt = now.AddMinutes(1) });
            Assert.False(AppleReleaseAppState.GateAllowsMutation(gate, now));
            AppleReleaseAppState.AtomicWrite(gate, new { Allowed = true, ExpiresAt = now.AddSeconds(-1) });
            Assert.False(AppleReleaseAppState.GateAllowsMutation(gate, now));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Build now", "351", "refs/heads/main", true)]
    [InlineData("Automatic: Selected PRs", "", "refs/heads/main", true)]
    [InlineData("Build now", "351", "refs/heads/untrusted", false)]
    [InlineData("Automatic: Off", "351", "refs/heads/main", false)]
    [InlineData("Build now", "1; arbitrary command", "refs/heads/main", false)]
    public void RequestWorkflow_ProducesValidatedArtifact_WithoutExecutingInputs(string operation, string number, string sourceRef, bool accepted)
    {
        if (!File.Exists("/usr/bin/python3")) return;
        var repository = FindRepository();
        var workflow = File.ReadAllLines(Path.Combine(repository, ".github/workflows/powerforge-apple-testflight-request.yml"));
        var script = string.Join('\n', workflow.SkipWhile(value => value != "        run: |").Skip(1).TakeWhile(value => value.StartsWith("          ", StringComparison.Ordinal)).Select(value => value.Substring(10)));
        var python = script.Substring(script.IndexOf('\n') + 1);
        python = python.Substring(0, python.LastIndexOf("\nPY", StringComparison.Ordinal));
        var root = Sandbox();
        try
        {
            var start = AppleTestFlightWorker.CreateProcess("/usr/bin/python3", root, new[] { "-c", python });
            foreach (var entry in new Dictionary<string, string> {
                ["REQUEST_OPERATION"] = operation, ["REQUEST_PULL_REQUEST"] = number, ["REQUEST_REPOSITORY"] = "owner/app",
                ["REQUEST_RUN_ID"] = "1234", ["REQUEST_ACTOR"] = "maintainer", ["REQUEST_REF"] = sourceRef,
                ["REQUEST_SHA"] = new string('a', 40), ["REQUEST_EVENT"] = "workflow_dispatch", ["GITHUB_STEP_SUMMARY"] = Path.Combine(root, "summary") })
                start.Environment[entry.Key] = entry.Value;
            using var process = new Process { StartInfo = start };
            process.Start();
            process.WaitForExit();
            Assert.Equal(accepted, process.ExitCode == 0);
            Assert.Equal(accepted, File.Exists(Path.Combine(root, "request.json")));
            if (accepted)
            {
                using var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "request.json")));
                Assert.Equal(operation, artifact.RootElement.GetProperty("operation").GetString());
                Assert.Equal(number, artifact.RootElement.GetProperty("pullRequest").GetString());
                Assert.Equal("1234", artifact.RootElement.GetProperty("runId").GetString());
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("MARKETING_VERSION: \"2.2\"\nCURRENT_PROJECT_VERSION: \"28\"", "MARKETING_VERSION: \"2.2\"\nCURRENT_PROJECT_VERSION: \"29\"", true)]
    [InlineData("CURRENT_PROJECT_VERSION = 28;\nPRODUCT_BUNDLE_IDENTIFIER = app;", "CURRENT_PROJECT_VERSION = 29;\nPRODUCT_BUNDLE_IDENTIFIER = app;", true)]
    [InlineData("CURRENT_PROJECT_VERSION = 28;\nSDKROOT = iphoneos;", "CURRENT_PROJECT_VERSION = 29;\nSDKROOT = macosx;", false)]
    [InlineData("CURRENT_PROJECT_VERSION: \"28\"", "CURRENT_PROJECT_VERSION: \"29\"\nscript: arbitrary", false)]
    public void PublisherCheckpoint_OnlyVersionFieldsMayChange(string before, string after, bool allowed)
        => Assert.Equal(allowed, AppleTestFlightWorker.PublisherVersionOnly(before, after));

    [Fact]
    public void ReadyRequiresBothValidPlatforms_AndActualInternalGroupDistribution()
    {
        var policy = new AppleTestFlightWorkerPolicy { BetaGroupId = "home-id", BetaGroupName = "Home", Targets = new[]
        {
            new AppleTestFlightWorkerTarget { Name = "iOS", AppId = "app", Platform = ApplePlatform.iOS },
            new AppleTestFlightWorkerTarget { Name = "Mac", AppId = "app", Platform = ApplePlatform.macOS }
        } };
        var receipt = new PowerForgeAppleReleaseReceipt { Success = true, Versioning = new() { MarketingVersion = "2.2", BuildNumber = "29" }, Targets = policy.Targets.Select(target => new PowerForgeAppleReleaseTargetReceipt
        {
            Name = target.Name, AppId = target.AppId, Platform = target.Platform, BuildId = target.Name,
            Version = "2.2", Build = "29",
            BuildProcessingState = "VALID", TestFlightInternalState = "IN_BETA_TESTING", TestFlightBetaGroupsConfigured = true,
            DistributedTestFlightBetaGroups = new[] { new AppStoreConnectBetaGroupInfo { Id = "home-id", Name = "Home", IsInternalGroup = true } }
        }).ToArray() };
        Assert.True(AppleTestFlightWorker.IsReady(receipt, policy));
        receipt.Targets[1].BuildProcessingState = "PROCESSING";
        Assert.False(AppleTestFlightWorker.IsReady(receipt, policy));
        receipt.Targets[1].BuildProcessingState = "VALID";
        receipt.Targets[1].DistributedTestFlightBetaGroups = Array.Empty<AppStoreConnectBetaGroupInfo>();
        Assert.False(AppleTestFlightWorker.IsReady(receipt, policy));
    }

    [Fact]
    public void ProcessBoundary_UsesLiteralArguments_AndExcludesInjectionEnvironment()
    {
        var marker = "$(touch /tmp/worker-should-never-run); `id`\n'quoted'";
        var start = AppleTestFlightWorker.CreateProcess("/usr/bin/printf", Path.GetTempPath(), new[] { "%s", marker });
        Assert.Equal(new[] { "%s", marker }, start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.False(start.Environment.ContainsKey("APP_STORE_CONNECT_PRIVATE_KEY_PATH"));
        Assert.False(start.Environment.ContainsKey("DOTNET_STARTUP_HOOKS"));
        Assert.False(start.Environment.ContainsKey("DYLD_INSERT_LIBRARIES"));
        if (OperatingSystem.IsWindows()) return;
        using var process = new Process { StartInfo = start };
        process.Start();
        Assert.Equal(marker, process.StandardOutput.ReadToEnd());
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void StandingPolicy_RejectsStoreRouteAndStaleSource()
    {
        var job = new AppleTestFlightJob { Source = "expected" };
        var plan = new PowerForgeAppleReleaseReceipt { Success = true, PlanOnly = true, Action = PowerForgeAppleReleaseAction.Ship,
            SourceCommit = "expected", PlanSha256 = new string('a', 64), Versioning = new(), Targets = new[] { new PowerForgeAppleReleaseTargetReceipt { ShipToTestFlight = true, TestFlightPolicy = AppleTestFlightPolicy.Internal } } };
        AppleTestFlightWorker.ValidatePlan(plan, job);
        plan.Targets[0].ShipToAppStoreReview = true;
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.ValidatePlan(plan, job));
        plan.Targets[0].ShipToAppStoreReview = false;
        plan.SourceCommit = "other";
        Assert.Throws<InvalidOperationException>(() => AppleTestFlightWorker.ValidatePlan(plan, job));
    }

    private static PowerForgeAppleReleasePlan Plan(string source, string checkout) => new()
    { ProjectRoot = checkout, SourceCommit = source, Apps = new[] { new PowerForgeAppleAppReleaseTargetPlan { BundleId = "com.example.worker", Name = "App" } } };

    private static string Sandbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.TestFlight.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln"))) return directory.FullName;
        throw new InvalidOperationException("Could not locate repository workflow fixture.");
    }
}
