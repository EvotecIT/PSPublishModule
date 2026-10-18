#if NET8_0_OR_GREATER
using System.Text.Json;

namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    internal static string[] BetaArguments(AppleTestFlightWorkerPolicy policy, AppleTestFlightJob job,
        PowerForgeAppleReleaseAction action, string[] targets, bool plan)
    {
        if (action is not (PowerForgeAppleReleaseAction.Status or PowerForgeAppleReleaseAction.TestFlight or PowerForgeAppleReleaseAction.SubmitTestFlightReview) ||
            !GitObjectId.IsFull(job.Source) || targets.Length == 0 || targets.Any(name => !policy.Targets.Any(value => value.Name == name)) ||
            action == PowerForgeAppleReleaseAction.SubmitTestFlightReview && !policy.SubmitBetaReviewAutomatically)
            throw new InvalidOperationException("Worker only invokes enrolled, source-bound beta operations.");
        var arguments = new List<string> { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(policy.OwnerRoot, "scripts/Invoke-PinnedPowerForge.ps1"),
            "-ExpectedCommit", policy.OwnerCommit, "-ConsumerRoot", policy.ConsumerRoot, "-ExpectedConsumerRepository", policy.Repository,
            "-ExpectedConsumerBranch", policy.DefaultBranch, "apple-release", action.ToString(), "--config", policy.ConfigPath,
            "--target", string.Join(",", targets), "--apple-source-commit", job.Source!, "--summary", "--output", "json" };
        if (plan) arguments.Add("--plan");
        else if (action != PowerForgeAppleReleaseAction.Status)
        {
            if (job.BetaPlanSha256?.Length != 64) throw new InvalidOperationException("Beta mutation requires an inspected plan hash.");
            arguments.AddRange(new[] { "--apple-expected-plan-sha256", job.BetaPlanSha256, "--confirm-apple-action" });
        }
        return arguments.ToArray();
    }

    private PowerForgeAppleReleaseReceipt InvokeBeta(PowerForgeAppleReleaseAction action, string[] targets, bool plan)
    {
        PowerForgeReleaseSpec? spec = null;
        CheckAppleChildEntry(() =>
        {
            AssertOwner();
            AssertClean();
            if (!plan && action != PowerForgeAppleReleaseAction.Status && !UpdateGate())
                throw new AppleTestFlightPreMutationRejectedException();
            spec = ReadSpec();
        }, mutation: !plan && action != PowerForgeAppleReleaseAction.Status);
        var automation = spec!.AppleApps!.Automation;
        if (plan) PreserveBetaPreview(ContainedPath(_policy.ConsumerRoot, automation.PlanReceiptPath));
        var started = DateTimeOffset.UtcNow;
        Process("pwsh", _policy.ConsumerRoot, BetaArguments(_policy, _running!, action, targets, plan), apple: true);
        PowerForgeAppleReleaseReceipt? receipt;
        if (plan)
        {
            var path = ContainedPath(_policy.ConsumerRoot, automation.PlanReceiptPath);
            receipt = File.Exists(path) && new FileInfo(path).Length <= 2 * 1024 * 1024
                ? JsonSerializer.Deserialize<PowerForgeAppleReleaseReceipt>(File.ReadAllText(path), Json) : null;
        }
        else receipt = ReadBetaHistory().FirstOrDefault(value => value.OperationPhase == "Completed" &&
            value.CheckedAt >= started && value.Action == action && value.SourceCommit == _running!.Source);
        if (receipt is null || receipt.CheckedAt < started || receipt.SourceCommit != _running!.Source || receipt.Action != action || receipt.PlanOnly != plan)
            throw new AppleTestFlightToolException("Beta command produced no fresh matching canonical receipt; retain its exact source and operation stage.");
        return receipt;
    }

    private PowerForgeAppleReleaseReceipt[] ReadBetaHistory()
    {
        var automation = ReadSpec().AppleApps!.Automation;
        return new AppleReleaseReceiptStore().ReadAll(new PowerForgeAppleReleasePlan
        {
            ProjectRoot = _policy.ConsumerRoot,
            ReceiptPath = ContainedPath(_policy.ConsumerRoot, automation.ReceiptPath),
            ReceiptHistoryPath = ContainedPath(_policy.ConsumerRoot, automation.ReceiptHistoryPath)
        });
    }

    private void PreserveBetaPreview(string path)
    {
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidOperationException("Retained preview exceeds its size bound.");
        var bytes = File.ReadAllBytes(path);
        var previous = JsonSerializer.Deserialize<PowerForgeAppleReleaseReceipt>(bytes, Json);
        if (previous?.PlanOnly != true || !GitObjectId.IsFull(previous.SourceCommit))
            throw new InvalidOperationException("Only a source-bound inactive preview may be replaced by the canonical beta planner.");
        var directory = Path.Combine(_directory, "previous-plans");
        AppleReleaseAppState.EnsureDirectory(directory);
        AppleReleaseAppState.AtomicWriteBytes(Path.Combine(directory,
            previous.SourceCommit + "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() + ".json"), bytes);
        // Keep the leaf in place. The canonical planner owns its replacement;
        // no worker deletion of plans, leases, receipts or history occurs here.
    }
}
#endif
