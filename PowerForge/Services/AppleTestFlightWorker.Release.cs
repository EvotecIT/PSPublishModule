#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    private void RetainPreviousSourcePlan()
    {
        var automation = ReadSpec().AppleApps!.Automation;
        RetainPreviousSourcePlan(_policy.ConsumerRoot, ContainedPath(_policy.ConsumerRoot, automation.PlanReceiptPath),
            _running!.Source!, Path.Combine(_directory, "previous-plans"));
    }

    internal static void RetainPreviousSourcePlan(string consumerRoot, string path, string source, string retainedDirectory)
    {
        AppleReleaseAppState.AssertUnlinked(path);
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidOperationException("Retained Apple plan exceeds its size bound.");
        var bytes = File.ReadAllBytes(path);
        var receipt = JsonSerializer.Deserialize<PowerForgeAppleReleaseReceipt>(bytes, Json)
                      ?? throw new InvalidOperationException("Retained Apple plan is invalid.");
        if (receipt.SourceCommit == source) return;
        if (!receipt.PlanOnly || !GitObjectId.IsFull(receipt.SourceCommit) ||
            receipt.PlanSha256 is null || !Regex.IsMatch(receipt.PlanSha256, "^[0-9a-fA-F]{64}$"))
            throw new InvalidOperationException("Only an ancestral source-bound preview plan may be retired before replanning.");
        using var process = System.Diagnostics.Process.Start(CreateProcess(Tool("git"), consumerRoot,
            new[] { "-c", "core.hooksPath=/dev/null", "-C", consumerRoot, "merge-base", "--is-ancestor", receipt.SourceCommit!, source }))!;
        if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new AppleTestFlightToolException("Could not verify retained plan ancestry."); }
        if (process.ExitCode != 0) throw new InvalidOperationException("Retained Apple plan source is not an ancestor of the new candidate.");
        AppleReleaseAppState.EnsureDirectory(retainedDirectory);
        var retained = Path.Combine(retainedDirectory, receipt.SourceCommit + "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() + ".json");
        AppleReleaseAppState.AtomicWriteBytes(retained, bytes);
        File.Delete(path);
    }

    private PowerForgeReleaseSpec ReadSpec()
    {
        var spec = JsonSerializer.Deserialize<PowerForgeReleaseSpec>(File.ReadAllText(ContainedPath(_policy.ConsumerRoot, _policy.ConfigPath)), Json)
                   ?? throw new InvalidOperationException("Release configuration is invalid.");
        var apple = spec.AppleApps ?? throw new InvalidOperationException("Apple release configuration is missing.");
        if (!apple.Automation.WriteReceipt || !apple.Automation.Resume || !apple.Automation.WaitForProcessing ||
            !apple.TestFlightBetaGroupIds.SequenceEqual(new[] { _policy.BetaGroupId }) ||
            apple.TestFlightBetaGroupNames.Any(value => value != _policy.BetaGroupName) || apple.TestFlightTesterEmails.Length != 0)
            throw new InvalidOperationException("Worker requires durable resumable receipts, processing waits, one enrolled Home group id, and no tester creation.");
        foreach (var target in _policy.Targets)
        {
            var app = apple.Apps.SingleOrDefault(value => value.Name == target.Name);
            if (app is null || app.BundleId != target.BundleId || app.AppStoreConnectAppId != target.AppId || app.Platform != target.Platform || app.TestFlightPolicy != AppleTestFlightPolicy.Internal)
                throw new InvalidOperationException("Configured target differs from the standing internal-beta policy.");
        }
        if (!_policy.PublisherPaths.Contains(apple.Automation.VersionSourcePath))
            throw new InvalidOperationException("Version source must be one of the explicitly enrolled publisher paths.");
        return spec;
    }

    private void Advance(AppleTestFlightJob job)
    {
        if (job.Stage is AppleTestFlightJobStage.CheckpointExecuting or AppleTestFlightJobStage.PublishingCheckpoint or AppleTestFlightJobStage.Releasing)
        {
            // A process exit before journal acknowledgement is an ambiguous mutation.
            // Immutable owner receipts are retained; never guess that it did not upload.
            SetStage(AppleTestFlightJobStage.NeedsOperator, "Worker stopped during mutation. Reconcile canonical receipts, source and remote delivery before resuming this job.");
            return;
        }
        if (job.Stage == AppleTestFlightJobStage.CheckpointReady)
        {
            PublishCheckpoint(job);
            return;
        }
        Git("fetch", "--quiet", "origin", "main");
        var remote = Git("rev-parse", "refs/remotes/origin/main");
        var head = Git("rev-parse", "HEAD");
        if (job.Stage is AppleTestFlightJobStage.ReleaseReady or AppleTestFlightJobStage.AppleProcessingPending)
        {
            if (job.Stage == AppleTestFlightJobStage.ReleaseReady && head == job.Source && remote != job.Source)
            {
                AssertClean();
                job.PlanSha256 = null;
                SetStage(AppleTestFlightJobStage.Queued, "Main changed before any delivery. Resolve and qualify the new candidate on the next pass.");
                return;
            }
            if (head != job.Source || remote != job.Source)
                throw new InvalidOperationException("Main moved after this source-bound release plan. Reconcile the existing delivery before rebinding; the strict operator cannot resume historical main.");
            AssertClean();
            ReadSpec();
            var plan = InvokeShip(plan: true);
            ValidatePlan(plan, job);
            if (plan.ShipPhase != PowerForgeAppleShipPhase.Release)
                throw new InvalidOperationException("Another operation consumed this build identity. Preserve the checkpoint and requalify a new candidate before reserving another build.");
            if (job.Stage == AppleTestFlightJobStage.AppleProcessingPending && (plan.Versioning?.BuildNumber != job.Build || plan.Versioning?.MarketingVersion != job.Version))
                throw new InvalidOperationException("Processing recovery attempted to allocate a different build.");
            job.PlanSha256 = plan.PlanSha256;
            _releasingFrom = job.Stage;
            SetStage(AppleTestFlightJobStage.Releasing);
            var result = InvokeShip(plan: false);
            job.Outcomes = result.Targets;
            if (IsReady(result, _policy)) SetStage(AppleTestFlightJobStage.ReadyInHome);
            else if (result.ShipPhase == PowerForgeAppleShipPhase.Release && (result.Success || result.Targets.Any(value => value.BuildProcessingState is "PROCESSING" or "PROCESSING_PENDING")) &&
                     result.Targets.All(value => string.IsNullOrEmpty(value.BuildProcessingState) || value.BuildProcessingState is "VALID" or "PROCESSING" or "PROCESSING_PENDING"))
                SetStage(AppleTestFlightJobStage.AppleProcessingPending, result.ErrorMessage);
            else SetStage(AppleTestFlightJobStage.NeedsOperator, SafeDiagnostic(result.ErrorMessage ?? "Both platform builds are not yet verified ready in Home; inspect the per-platform outcomes."));
            return;
        }
        AssertClean();
        if (head != remote) Git("merge", "--ff-only", "refs/remotes/origin/main");
        job.Source = remote;
        job.OriginalSource = remote;
        foreach (var number in job.IncludedPullRequests)
        {
            var pull = Api($"pulls/{number}");
            if (!pull.GetProperty("merged").GetBoolean() || Text(pull.GetProperty("base"), "ref") != "main" || Text(pull.GetProperty("base").GetProperty("repo"), "full_name") != _policy.Repository)
                throw new InvalidOperationException($"PR {number} is not merged into this repository's main.");
            Git("merge-base", "--is-ancestor", Text(pull, "merge_commit_sha"), remote);
        }
        if (!ChecksPass(remote)) { SetStage(AppleTestFlightJobStage.WaitingForChecks, "The exact latest main requires successful enrolled push qualification."); return; }
        RecordIncludedPullRequests(job, remote);
        ReadSpec();
        new AppleReleaseSourceTrustService().Capture(_policy.ConsumerRoot, ContainedPath(_policy.ConsumerRoot, _policy.ConfigPath));
        SetStage(AppleTestFlightJobStage.Planning);
        var approved = InvokeShip(plan: true);
        ValidatePlan(approved, job);
        job.PlanSha256 = approved.PlanSha256;
        job.Version = approved.Versioning!.MarketingVersion;
        job.Build = approved.Versioning.BuildNumber;
        if (approved.ShipPhase == PowerForgeAppleShipPhase.VersionCheckpoint)
        {
            RequireUnprotectedMain();
            SetStage(AppleTestFlightJobStage.CheckpointExecuting);
            var checkpoint = InvokeShip(plan: false);
            if (!checkpoint.Success || checkpoint.ShipPhase != PowerForgeAppleShipPhase.VersionCheckpoint)
                throw new InvalidOperationException(checkpoint.ErrorMessage ?? "Ship version checkpoint did not complete.");
            ValidatePublisherDiff(job);
            SetStage(AppleTestFlightJobStage.CheckpointReady);
            PublishCheckpoint(job);
        }
        else SetStage(AppleTestFlightJobStage.ReleaseReady);
    }

    internal static void ValidatePlan(PowerForgeAppleReleaseReceipt plan, AppleTestFlightJob job, AppleTestFlightWorkerPolicy? policy = null)
    {
        if (!plan.Success) throw new AppleTestFlightToolException("Ship preview did not succeed; retain this nonmutating stage and retry inventory observation.");
        if (!plan.PlanOnly || plan.Action != PowerForgeAppleReleaseAction.Ship || plan.SourceCommit != job.Source ||
            plan.PlanSha256?.Length != 64 || plan.Versioning is null || plan.Targets.Length == 0 ||
            plan.Targets.Any(value => value.ShipToAppStoreReview || (value.ShipToTestFlight && value.TestFlightPolicy != AppleTestFlightPolicy.Internal)))
            throw new InvalidOperationException("Ship plan exceeds the standing internal-beta policy or has stale source identity.");
        if (policy is not null && !plan.Targets.Where(value => value.ShipToTestFlight).Select(value => value.Name).Order().SequenceEqual(policy.Targets.Select(value => value.Name).Order()))
            throw new InvalidOperationException("Ship plan target set differs from enrolled targets.");
    }

    private void ValidatePlan(PowerForgeAppleReleaseReceipt plan, AppleTestFlightJob job) => ValidatePlan(plan, job, _policy);

    internal static bool IsReady(PowerForgeAppleReleaseReceipt receipt, AppleTestFlightWorkerPolicy policy) => receipt.Success && receipt.Versioning is not null && policy.Targets.All(target =>
        receipt.Targets.Any(value => value.Name == target.Name && value.Platform == target.Platform && value.AppId == target.AppId &&
            value.Version == receipt.Versioning.MarketingVersion && value.Build == receipt.Versioning.BuildNumber &&
            !string.IsNullOrEmpty(value.BuildId) && value.BuildProcessingState == "VALID" && value.TestFlightInternalState == "IN_BETA_TESTING" &&
            value.DistributedTestFlightBetaGroups.Any(group => group.Id == policy.BetaGroupId && group.Name == policy.BetaGroupName && group.IsInternalGroup == true)));

    private void AssertClean()
    {
        if (Git("status", "--porcelain=v1", "--untracked-files=all").Length != 0)
            throw new InvalidOperationException("Worker source has unexpected tracked or untracked changes. Preserve and inspect them.");
    }

    private void RequireUnprotectedMain()
    {
        if (Api("branches/main").GetProperty("protected").GetBoolean() || Api("rules/branches/main").GetArrayLength() != 0)
            throw new InvalidOperationException("Main branch policy requires a reviewed change. Publisher metadata push is held; no policy bypass is permitted.");
    }

    private void ValidatePublisherDiff(AppleTestFlightJob job)
    {
        var changed = Git("diff", "--name-only", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (changed.Length == 0 || Git("ls-files", "--others", "--exclude-standard").Length != 0)
            throw new InvalidOperationException("Publisher checkpoint must change only existing tracked version metadata.");
        foreach (var path in changed)
        {
            if (!_policy.PublisherPaths.Contains(path) || !PublisherVersionOnly(GitBlob(job.OriginalSource!, path), File.ReadAllText(ContainedPath(_policy.ConsumerRoot, path))))
                throw new InvalidOperationException($"Publisher checkpoint changed application behavior or project graph: {path}");
        }
    }

    internal static bool PublisherVersionOnly(string before, string after)
    {
        string Normalize(string value) => Regex.Replace(value,
            @"(?m)^(?<prefix>[ \t]*(?:MARKETING_VERSION|CURRENT_PROJECT_VERSION)[ \t]*(?::|=)[ \t]*)(?:[""']?\d+(?:\.\d+){0,2}[""']?)(?<suffix>[ \t]*;?[ \t]*(?:#.*)?)$", "${prefix}<publisher-version>${suffix}");
        return before != after && Normalize(before) == Normalize(after);
    }

    private void PublishCheckpoint(AppleTestFlightJob job)
    {
        RequireUnprotectedMain();
        Git("fetch", "--quiet", "origin", "main");
        if (Git("rev-parse", "refs/remotes/origin/main") != job.OriginalSource || Git("rev-parse", "HEAD") != job.OriginalSource)
            throw new InvalidOperationException("Main moved during the publisher checkpoint. Preserve the narrow diff for operator reconciliation.");
        ValidatePublisherDiff(job);
        SetStage(AppleTestFlightJobStage.PublishingCheckpoint);
        Git(new[] { "add", "--" }.Concat(_policy.PublisherPaths).ToArray());
        Git("-c", "commit.gpgSign=false", "commit", "-m", $"Set Apple beta version {job.Version} build {job.Build}");
        job.CheckpointCommit = Git("rev-parse", "HEAD");
        if (Git("rev-parse", "HEAD^") != job.OriginalSource)
            throw new InvalidOperationException("Publisher checkpoint parent changed before commit.");
        foreach (var path in Git("diff", "--name-only", "HEAD^", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (!_policy.PublisherPaths.Contains(path) || !PublisherVersionOnly(GitBlob(job.OriginalSource!, path), GitBlob(job.CheckpointCommit, path)))
                throw new InvalidOperationException("Committed publisher checkpoint changed behavior or project graph.");
        job.Source = job.CheckpointCommit;
        Save();
        Git("push", "origin", "HEAD:refs/heads/main");
        if (Git("ls-remote", "origin", "refs/heads/main").Split('\t')[0] != job.Source)
            throw new InvalidOperationException("Publisher checkpoint was not read back as the current remote main.");
        AssertClean();
        SetStage(AppleTestFlightJobStage.ReleaseReady, "Original qualification is retained only for this inspected publisher-only checkpoint; signed archives still use the exact checkpoint source.");
    }

    private string GitBlob(string source, string path) => Process("git", _policy.ConsumerRoot,
        new[] { "-c", "core.hooksPath=/dev/null", "-C", _policy.ConsumerRoot, "show", $"{source}:{path}" });
}
#endif
