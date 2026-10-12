#if NET8_0_OR_GREATER
using System.Text.Json;

namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    private void SyncRequests()
    {
        var path = $"actions/workflows/{Uri.EscapeDataString(Path.GetFileName(_policy.WorkflowPath))}/runs?event=workflow_dispatch&branch=main";
        var runs = ObserveRequestRuns(_state, _policy.AcceptRequestsAfter,
            page => Api(path + $"&per_page=100&page={page}").GetProperty("workflow_runs").EnumerateArray().Select(value => value.Clone()).ToArray(),
            id => Api($"actions/runs/{id}"), Save,
            () => new[] { "queued", "in_progress", "waiting", "pending", "requested" }
                .SelectMany(status => Pages(path + "&status=" + status, "workflow_runs")).ToArray());
        ConsumeRequestRuns(_state, runs, DateTimeOffset.UtcNow, ReadRequest, Save);
        PublishPendingRequestStatuses();
    }

    private AppleTestFlightRequestRecord ReadRequest(JsonElement summary)
    {
        var id = summary.GetProperty("id").GetInt64();
        var run = Api($"actions/runs/{id}");
        if (run.GetProperty("run_attempt").GetInt32() != RequestAttempt(summary) || Text(run, "status") != "completed" || Text(run, "conclusion") != "success")
            throw new AppleTestFlightToolException("Request attempt changed during observation; retry its authoritative current state.");
        if (Text(run, "event") != "workflow_dispatch" || Text(run, "head_branch") != "main" || Text(run, "path") != _policy.WorkflowPath ||
            Text(run.GetProperty("head_repository"), "full_name") != _policy.Repository ||
            !run.GetProperty("referenced_workflows").EnumerateArray().Any(value =>
                Text(value, "path").StartsWith("EvotecIT/PSPublishModule/.github/workflows/powerforge-apple-testflight-request.yml@", StringComparison.Ordinal) &&
                string.Equals(Text(value, "sha"), _policy.OwnerCommit, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("TestFlight request did not use the enrolled default-branch workflow and pinned shared request owner.");
        var actor = Text(run.GetProperty("actor"), "login");
        var triggeringActor = Text(run.GetProperty("triggering_actor"), "login");
        RequireMaintainer(actor);
        RequireMaintainer(triggeringActor);
        var artifacts = Api($"actions/runs/{id}/artifacts").GetProperty("artifacts").EnumerateArray()
            .Where(value => Text(value, "name") == "powerforge-testflight-request" && !value.GetProperty("expired").GetBoolean()).ToArray();
        if (artifacts.Length != 1 || artifacts[0].GetProperty("size_in_bytes").GetInt64() > 65536)
            throw new InvalidOperationException("The request artifact is absent, ambiguous, expired, or exceeds its size limit.");
        var directory = Path.Combine(_policy.ScratchRoot, "request-" + id + "-" + Guid.NewGuid().ToString("N"));
        AppleReleaseAppState.EnsureDirectory(directory);
        try
        {
            Process("gh", _policy.OwnerRoot, new[] { "run", "download", id.ToString(), "--repo", _policy.Repository, "--name", "powerforge-testflight-request", "--dir", directory });
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            if (files.Length != 1 || Path.GetFileName(files[0]) != "request.json" || new FileInfo(files[0]).Length > 16384)
                throw new InvalidOperationException("TestFlight request artifact must contain one bounded request.json.");
            AppleReleaseAppState.AssertUnlinked(files[0]);
            using var request = JsonDocument.Parse(File.ReadAllText(files[0]));
            var value = request.RootElement;
            if (Text(value, "repository") != _policy.Repository || Text(value, "runId") != id.ToString() ||
                Text(value, "actor") != actor || Text(value, "sourceRef") != "refs/heads/main" ||
                !string.Equals(Text(value, "sourceCommit"), Text(run, "head_sha"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("TestFlight request metadata differs from the authoritative workflow run.");
            var operation = Text(value, "operation");
            var numberText = Text(value, "pullRequest");
            var number = string.IsNullOrEmpty(numberText) ? 0 : int.Parse(numberText, System.Globalization.CultureInfo.InvariantCulture);
            if (number < 0 || (operation != "Build now" && number != 0)) throw new InvalidOperationException("Only Build now accepts a merged PR number.");
            return new AppleTestFlightRequestRecord
            {
                Key = "run:" + id, Actor = actor, Operation = operation, PullRequest = number,
                Url = Text(run, "html_url"), RecordedAt = RequestStartedAt(summary), Attempt = RequestAttempt(summary),
                SourceCommit = Text(run, "head_sha"), StatusPending = operation != "Build now"
            };
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    internal static void ApplyRequest(AppleTestFlightWorkerState state, AppleTestFlightRequestRecord request)
    {
        if (state.Requests.Any(value => value.Key == request.Key)) return;
        switch (request.Operation)
        {
            case "Build now":
                state.Jobs.Add(new AppleTestFlightJob { Id = request.Key.Replace(':', '-'), RequestKeys = new() { request.Key }, IncludedPullRequests = request.PullRequest > 0 ? new() { request.PullRequest } : new() });
                break;
            case "Automatic: Off": ApplyMode(state, request, AppleTestFlightAutomaticMode.Off); break;
            case "Automatic: Selected PRs": ApplyMode(state, request, AppleTestFlightAutomaticMode.SelectedPRs); break;
            case "Automatic: Every merged PR": ApplyMode(state, request, AppleTestFlightAutomaticMode.EveryMergedPR); break;
            default: throw new InvalidOperationException("Unsupported TestFlight request operation.");
        }
        state.Requests.Add(request);
    }

    private static void ApplyMode(AppleTestFlightWorkerState state, AppleTestFlightRequestRecord request, AppleTestFlightAutomaticMode mode)
    {
        static long RunId(AppleTestFlightRequestRecord record) => record.Key.StartsWith("run:", StringComparison.Ordinal) &&
            long.TryParse(record.Key.Substring(4), out var id) ? id : 0;
        // Older journals have the intent time but no explicit tie breaker.
        if (state.ModeChangedByRunId == 0)
            state.ModeChangedByRunId = state.Requests.Where(value => value.Operation.StartsWith("Automatic:", StringComparison.Ordinal) &&
                value.RecordedAt == state.ModeChangedAt).Select(RunId).DefaultIfEmpty().Max();
        var runId = RunId(request);
        if (request.RecordedAt < state.ModeChangedAt ||
            (request.RecordedAt == state.ModeChangedAt && runId < state.ModeChangedByRunId)) return;
        state.Mode = mode;
        state.ModeChangedAt = request.RecordedAt;
        state.ModeChangedByRunId = runId;
    }

    private void RequireMaintainer(string actor)
    {
        var permission = Text(Api($"collaborators/{Uri.EscapeDataString(actor)}/permission"), "permission");
        if (permission is not ("admin" or "maintain" or "write")) throw new InvalidOperationException("TestFlight request actor does not have repository write permission.");
    }

    private void EnqueueMergedRequests()
    {
        if (_state.Mode == AppleTestFlightAutomaticMode.Off) return;
        foreach (var pull in Pages("pulls?state=closed&sort=updated&direction=desc"))
        {
            if (pull.GetProperty("updated_at").GetDateTimeOffset() < _policy.AcceptRequestsAfter) break;
            if (pull.GetProperty("merged_at").ValueKind == JsonValueKind.Null || Text(pull.GetProperty("base"), "ref") != "main") continue;
            var number = pull.GetProperty("number").GetInt32();
            var labels = pull.GetProperty("labels").EnumerateArray().Select(value => Text(value, "name")).ToArray();
            if (labels.Contains("testflight-skip")) continue;
            if (_state.Mode == AppleTestFlightAutomaticMode.EveryMergedPR && pull.GetProperty("merged_at").GetDateTimeOffset() < _state.ModeChangedAt) continue;
            if (_state.Mode == AppleTestFlightAutomaticMode.SelectedPRs && !labels.Contains("testflight")) continue;
            var key = $"pr:{number}:{Text(pull, "merge_commit_sha")}";
            if (_state.Requests.Any(value => value.Key == key)) continue;
            var detail = Api($"pulls/{number}");
            RequireMaintainer(Text(detail.GetProperty("merged_by"), "login"));
            if (_state.Mode == AppleTestFlightAutomaticMode.SelectedPRs)
            {
                var labeled = Pages($"issues/{number}/events").LastOrDefault(value => Text(value, "event") == "labeled" && Text(value.GetProperty("label"), "name") == "testflight");
                if (labeled.ValueKind == JsonValueKind.Undefined) continue;
                RequireMaintainer(Text(labeled.GetProperty("actor"), "login"));
            }
            var record = new AppleTestFlightRequestRecord { Key = key, Actor = Text(detail.GetProperty("merged_by"), "login"), Operation = "Merged PR", PullRequest = number, Url = Text(detail, "html_url"), RecordedAt = DateTimeOffset.UtcNow };
            EnqueueAutomaticRequest(_state, record);
            Save();
        }
    }

    private void ReconcileAutomaticSelection()
    {
        if (_state.Mode == AppleTestFlightAutomaticMode.Off) return;
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var pending = _state.Jobs.Where(value => value.Automatic && IsUnstarted(value.Stage))
            .SelectMany(value => value.RequestKeys).ToHashSet(StringComparer.Ordinal);
        foreach (var request in _state.Requests.Where(value => pending.Contains(value.Key) && value.PullRequest > 0))
        {
            var labels = Api($"pulls/{request.PullRequest}").GetProperty("labels").EnumerateArray().Select(value => Text(value, "name")).ToArray();
            if (labels.Contains("testflight-skip") || (_state.Mode == AppleTestFlightAutomaticMode.SelectedPRs && !labels.Contains("testflight")))
                excluded.Add(request.Key);
        }
        var cancelled = ExcludeUnstartedAutomaticRequests(_state, excluded);
        Save();
        foreach (var job in cancelled) PublishStatus(job);
    }

    internal static IReadOnlyList<AppleTestFlightJob> ExcludeUnstartedAutomaticRequests(AppleTestFlightWorkerState state, ISet<string> excludedKeys)
    {
        var cancelled = new List<AppleTestFlightJob>();
        foreach (var job in state.Jobs.Where(value => value.Automatic && IsUnstarted(value.Stage)).ToArray())
        {
            var excluded = job.RequestKeys.Where(excludedKeys.Contains).ToList();
            if (excluded.Count == 0) continue;
            var held = job;
            if (excluded.Count != job.RequestKeys.Count)
            {
                held = new AppleTestFlightJob { Id = job.Id + "-excluded-" + AppleReleaseAppState.Hash(string.Join("|", excluded)).Substring(0, 12),
                    Automatic = true, RequestKeys = excluded, Source = job.Source, OriginalSource = job.OriginalSource,
                    Version = job.Version, Build = job.Build,
                    IncludedPullRequests = state.Requests.Where(value => excluded.Contains(value.Key)).Select(value => value.PullRequest).Distinct().ToList() };
                state.Jobs.Add(held);
                job.RequestKeys.RemoveAll(excludedKeys.Contains);
                job.IncludedPullRequests.RemoveAll(held.IncludedPullRequests.Contains);
                job.Stage = AppleTestFlightJobStage.Queued;
                job.PlanSha256 = null;
            }
            held.Stage = AppleTestFlightJobStage.Excluded;
            held.Diagnostic = "Unstarted automatic trigger cancelled by current selection labels; request history is retained. Use Build now to request it again.";
            held.UpdatedAt = DateTimeOffset.UtcNow;
            held.StatusPending = true;
            cancelled.Add(held);
        }
        return cancelled;
    }

    internal static void EnqueueAutomaticRequest(AppleTestFlightWorkerState state, AppleTestFlightRequestRecord request)
    {
        if (state.Requests.Any(value => value.Key == request.Key)) return;
        var pending = state.Jobs.LastOrDefault(value => value.Automatic && value.Stage is AppleTestFlightJobStage.Queued or AppleTestFlightJobStage.WaitingForChecks or AppleTestFlightJobStage.Planning);
        if (pending is null)
        {
            pending = new AppleTestFlightJob { Id = "pr-" + request.PullRequest, Automatic = true };
            state.Jobs.Add(pending);
        }
        pending.RequestKeys.Add(request.Key);
        if (!pending.IncludedPullRequests.Contains(request.PullRequest)) pending.IncludedPullRequests.Add(request.PullRequest);
        state.Requests.Add(request);
    }

    private void RecordIncludedPullRequests(AppleTestFlightJob job, string source)
    {
        var previous = _state.Jobs.LastOrDefault(value => value.Stage == AppleTestFlightJobStage.ReadyInHome && value.Source is not null)?.Source ?? _policy.BaselineSourceCommit;
        Git("merge-base", "--is-ancestor", previous, source);
        var commits = Git("rev-list", "--first-parent", previous + ".." + source).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        foreach (var pull in Pages("pulls?state=closed&sort=updated&direction=desc"))
        {
            if (pull.GetProperty("updated_at").GetDateTimeOffset() < _policy.AcceptRequestsAfter) break;
            if (pull.GetProperty("merged_at").ValueKind != JsonValueKind.Null && commits.Contains(Text(pull, "merge_commit_sha")))
            {
                var number = pull.GetProperty("number").GetInt32();
                if (!job.IncludedPullRequests.Contains(number)) job.IncludedPullRequests.Add(number);
            }
        }
    }

    private bool ChecksPass(string source)
    {
        var run = Pages($"actions/workflows/{Uri.EscapeDataString(Path.GetFileName(_policy.QualificationWorkflowPath))}/runs?head_sha={source}&branch=main&event=push", "workflow_runs").FirstOrDefault();
        if (run.ValueKind == JsonValueKind.Undefined || Text(run, "head_sha") != source || Text(run, "head_branch") != "main" || Text(run, "event") != "push" ||
            Text(run, "path") != _policy.QualificationWorkflowPath || Text(run, "status") != "completed" || Text(run, "conclusion") != "success") return false;
        var jobs = Pages($"actions/runs/{run.GetProperty("id").GetInt64()}/jobs?filter=latest", "jobs").ToArray();
        return _policy.RequiredChecks.All(name => jobs.Any(value => Text(value, "name") == name && Text(value, "head_sha") == source && Text(value, "status") == "completed" && Text(value, "conclusion") == "success"));
    }

    internal static string StatusFor(AppleTestFlightJobStage stage) => stage switch
    {
        AppleTestFlightJobStage.ReadyInHome => "success",
        AppleTestFlightJobStage.NeedsOperator => "failure",
        AppleTestFlightJobStage.Excluded => "error",
        _ => "pending"
    };

    private void PublishPendingRequestStatuses()
    {
        foreach (var request in _state.Requests.Where(value => value.StatusPending && value.SourceCommit is not null))
        {
            try
            {
                Api($"statuses/{request.SourceCommit}", "--method", "POST", "-f", "context=powerforge/testflight-mode", "-f", "state=success",
                    "-f", "description=Mac observed automatic mode: " + _state.Mode, "-f", "target_url=" + request.Url);
                request.StatusPending = false;
                Save();
            }
            catch (AppleTestFlightToolException) { /* Optional acknowledgement is retried from the durable record. */ }
        }
    }

    private void PublishStatus(AppleTestFlightJob job)
    {
        if (job.Source is null) return;
        job.StatusPending = true;
        Save();
        try
        {
            var state = StatusFor(job.Stage);
            var description = $"{job.Stage}: {job.Version} ({job.Build}); PRs {string.Join(",", job.IncludedPullRequests)}";
            if (description.Length > 140) description = description.Substring(0, 140);
            Api($"statuses/{job.Source}", "--method", "POST", "-f", "context=powerforge/testflight", "-f", "state=" + state, "-f", "description=" + description,
                "-f", "target_url=" + (_state.Requests.FirstOrDefault(value => job.RequestKeys.Contains(value.Key))?.Url ?? ""));
            job.StatusPending = false;
            Save();
        }
        catch { job.Diagnostic = SafeDiagnostic((job.Diagnostic ?? "") + " GitHub status publication failed; the local journal remains authoritative."); Save(); }
    }
}
#endif
