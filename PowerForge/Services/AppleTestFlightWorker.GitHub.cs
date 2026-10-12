#if NET8_0_OR_GREATER
using System.Text.Json;

namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    private void SyncRequests()
    {
        var runs = Pages($"actions/workflows/{Uri.EscapeDataString(Path.GetFileName(_policy.WorkflowPath))}/runs?event=workflow_dispatch&branch=main", "workflow_runs")
            .TakeWhile(value => value.GetProperty("created_at").GetDateTimeOffset() >= _policy.AcceptRequestsAfter && value.GetProperty("id").GetInt64() > _state.LastRequestRun)
            .OrderBy(value => value.GetProperty("id").GetInt64()).ToArray();
        // A later completed mode change cannot overtake an earlier unfinished request.
        foreach (var summary in runs)
        {
            var id = summary.GetProperty("id").GetInt64();
            if (id <= _state.LastRequestRun) continue;
            if (Text(summary, "status") != "completed") break;
            if (Text(summary, "conclusion") != "success") { _state.LastRequestRun = id; Save(); continue; }
            var run = Api($"actions/runs/{id}");
            if (Text(run, "event") != "workflow_dispatch" || Text(run, "head_branch") != "main" || Text(run, "path") != _policy.WorkflowPath ||
                Text(run.GetProperty("head_repository"), "full_name") != _policy.Repository ||
                !run.GetProperty("referenced_workflows").EnumerateArray().Any(value =>
                    Text(value, "path").StartsWith("EvotecIT/PSPublishModule/.github/workflows/powerforge-apple-testflight-request.yml@", StringComparison.Ordinal) &&
                    Text(value, "sha") == _policy.OwnerCommit))
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
                    Text(value, "sourceCommit") != Text(run, "head_sha"))
                    throw new InvalidOperationException("TestFlight request metadata differs from the authoritative workflow run.");
                var operation = Text(value, "operation");
                var numberText = Text(value, "pullRequest");
                var number = string.IsNullOrEmpty(numberText) ? 0 : int.Parse(numberText, System.Globalization.CultureInfo.InvariantCulture);
                if (number < 0 || (operation != "Build now" && number != 0)) throw new InvalidOperationException("Only Build now accepts a merged PR number.");
                ApplyRequest(_state, new AppleTestFlightRequestRecord
                {
                    Key = "run:" + id, Actor = actor, Operation = operation, PullRequest = number,
                    Url = Text(run, "html_url"), RecordedAt = run.GetProperty("created_at").GetDateTimeOffset()
                });
                if (operation != "Build now")
                    Api($"statuses/{Text(run, "head_sha")}", "--method", "POST", "-f", "context=powerforge/testflight-mode", "-f", "state=success",
                        "-f", "description=Mac observed automatic mode: " + _state.Mode, "-f", "target_url=" + Text(run, "html_url"));
                _state.LastRequestRun = id;
                Save();
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        }
        Save();
    }

    internal static void ApplyRequest(AppleTestFlightWorkerState state, AppleTestFlightRequestRecord request)
    {
        if (state.Requests.Any(value => value.Key == request.Key)) return;
        switch (request.Operation)
        {
            case "Build now":
                state.Jobs.Add(new AppleTestFlightJob { Id = request.Key.Replace(':', '-'), RequestKeys = new() { request.Key }, IncludedPullRequests = request.PullRequest > 0 ? new() { request.PullRequest } : new() });
                break;
            case "Automatic: Off": state.Mode = AppleTestFlightAutomaticMode.Off; state.ModeChangedAt = request.RecordedAt; break;
            case "Automatic: Selected PRs": state.Mode = AppleTestFlightAutomaticMode.SelectedPRs; state.ModeChangedAt = request.RecordedAt; break;
            case "Automatic: Every merged PR": state.Mode = AppleTestFlightAutomaticMode.EveryMergedPR; state.ModeChangedAt = request.RecordedAt; break;
            default: throw new InvalidOperationException("Unsupported TestFlight request operation.");
        }
        state.Requests.Add(request);
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

    private void PublishStatus(AppleTestFlightJob job)
    {
        if (job.Source is null) return;
        try
        {
            var state = job.Stage == AppleTestFlightJobStage.ReadyInHome ? "success" : job.Stage == AppleTestFlightJobStage.NeedsOperator ? "failure" : "pending";
            var description = $"{job.Stage}: {job.Version} ({job.Build}); PRs {string.Join(",", job.IncludedPullRequests)}";
            if (description.Length > 140) description = description.Substring(0, 140);
            Api($"statuses/{job.Source}", "--method", "POST", "-f", "context=powerforge/testflight", "-f", "state=" + state, "-f", "description=" + description,
                "-f", "target_url=" + (_state.Requests.First(value => job.RequestKeys.Contains(value.Key)).Url));
        }
        catch { job.Diagnostic = (job.Diagnostic ?? "") + " GitHub status publication failed; the local journal remains authoritative."; Save(); }
    }
}
#endif
