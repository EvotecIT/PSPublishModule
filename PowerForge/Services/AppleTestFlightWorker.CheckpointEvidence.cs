#if NET8_0_OR_GREATER
using System.Text.Json;

namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    /// <summary>Read-only admission of a completed, uncommitted publisher checkpoint after a worker restart.</summary>
    internal string[] PublisherCheckpointEvidence()
    {
        _state = LoadState();
        var job = SelectRunnableJob(_state);
        if (job?.Stage != AppleTestFlightJobStage.CheckpointReady) return Array.Empty<string>();
        // The pinned entry verifies owner source before compiling this reader.
        // Keep this local evidence operation read-only and independent of GitHub availability.
        AssertOrigin(_policy.ConsumerRoot, _policy.Repository);
        if (Git("symbolic-ref", "--short", "HEAD") != _policy.DefaultBranch || Git("for-each-ref", "--format=%(refname)", "refs/replace").Length != 0)
            throw new InvalidOperationException("Checkpoint source must be main without replacement refs.");
        if (Git("rev-parse", "HEAD") != job.OriginalSource || job.Source != job.OriginalSource ||
            Git("rev-parse", $"refs/remotes/origin/{_policy.DefaultBranch}") != job.OriginalSource ||
            Git("diff", "--cached", "--name-only").Length != 0)
            throw new InvalidOperationException("Restart admission requires the original current main and an unstaged completed publisher checkpoint.");
        var apple = ReadSpec().AppleApps!;
        var automation = apple.Automation;
        var projectRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ContainedPath(_policy.ConsumerRoot, _policy.ConfigPath))!, apple.ProjectRoot ?? "."));
        var sourcePath = Path.GetFullPath(Path.Combine(projectRoot, automation.VersionSourcePath ?? throw new InvalidOperationException("Checkpoint version source is missing.")));
        if (!_policy.PublisherPaths.Select(path => ContainedPath(_policy.ConsumerRoot, path)).Contains(sourcePath))
            throw new InvalidOperationException("Checkpoint version source is outside the enrolled publisher paths.");
        var version = new AppleReleaseVersionSourceService().Read(sourcePath);
        if (version.MarketingVersion != job.Version || version.BuildNumber != job.Build)
            throw new InvalidOperationException("Publisher checkpoint bytes differ from the completed version/build identity.");
        var plan = ReadCheckpointReceipt(automation.PlanReceiptPath);
        var completed = ReadCheckpointReceipt(automation.ReceiptPath);
        ValidateCheckpointReceipts(job, plan, completed, _policy);
        var local = new PowerForgeReleaseService(new NullLogger()).ValidateLocalAppleEvidence(
            ContainedPath(_policy.ConsumerRoot, _policy.ConfigPath), job.Source!);
        if (!local.Success) throw new InvalidOperationException("Checkpoint journal integrity or local evidence failed: " + local.ErrorMessage);
        ValidatePublisherDiff(job);
        return Git("diff", "--name-only", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => ContainedPath(_policy.ConsumerRoot, path)).ToArray();
    }

    private PowerForgeAppleReleaseReceipt ReadCheckpointReceipt(string path)
    {
        var full = ContainedPath(_policy.ConsumerRoot, path);
        if (new FileInfo(full).Length > 32 * 1024 * 1024) throw new InvalidOperationException("Checkpoint receipt exceeds its size bound.");
        return JsonSerializer.Deserialize<PowerForgeAppleReleaseReceipt>(File.ReadAllText(full), Json)
            ?? throw new InvalidOperationException("Publisher checkpoint receipt is invalid.");
    }

    internal static void ValidateCheckpointReceipts(AppleTestFlightJob job, PowerForgeAppleReleaseReceipt plan,
        PowerForgeAppleReleaseReceipt completed, AppleTestFlightWorkerPolicy policy)
    {
        ValidatePlan(plan, job, policy);
        if (job.Stage != AppleTestFlightJobStage.CheckpointReady || plan.ShipPhase != PowerForgeAppleShipPhase.VersionCheckpoint ||
            plan.PlanSha256 != job.PlanSha256 || PowerForgeReleaseService.ComputeApplePlanSha256(plan) != job.PlanSha256 || !completed.Success || completed.PlanOnly ||
            completed.OperationPhase != "Completed" || completed.Action != PowerForgeAppleReleaseAction.Ship || completed.ShipPhase != PowerForgeAppleShipPhase.VersionCheckpoint ||
            completed.SourceCommit != job.OriginalSource || completed.PlanSha256 != job.PlanSha256 ||
            completed.Versioning?.BuildNumber != job.Build || completed.Versioning?.MarketingVersion != job.Version ||
            completed.Targets.Length != plan.Targets.Length || completed.Targets.Any(value => value.ShipToAppStoreReview) ||
            !completed.Targets.Select(value => value.Name).Order().SequenceEqual(plan.Targets.Select(value => value.Name).Order()))
            throw new InvalidOperationException("Restart admission requires the exact successful source, plan, target, and version checkpoint receipts.");
    }
}
#endif
