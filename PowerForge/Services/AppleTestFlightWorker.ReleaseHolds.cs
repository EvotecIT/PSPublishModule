#if NET8_0_OR_GREATER
using System.Text.Json;
namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    // A trigger opt-out is not a code exclusion: every later main contains the
    // merged change. Configured holds therefore cover manual requests as well.
    internal static bool ExcludesAutomaticTrigger(IEnumerable<string> labels, AppleTestFlightWorkerPolicy policy)
        => labels.Contains("testflight-skip", StringComparer.OrdinalIgnoreCase) &&
           !string.Equals(policy.ReleaseHoldLabel, "testflight-skip", StringComparison.OrdinalIgnoreCase);

    internal static bool CompareContainsMerge(string comparison) => comparison switch
    {
        "ahead" or "identical" => true,
        "behind" or "diverged" => false,
        _ => throw new InvalidOperationException("Could not establish ancestry for a release-hold PR; mutation remains held.")
    };

    private int[] FindReleaseHolds(string source)
        => FindReleaseHolds(_policy, source,
            Pages("issues?state=all&labels=" + Uri.EscapeDataString(_policy.ReleaseHoldLabel)),
            number => Api($"pulls/{number}"), (merge, candidate) => Text(Api($"compare/{merge}...{candidate}"), "status"));

    internal static int[] FindReleaseHolds(AppleTestFlightWorkerPolicy policy, string source, IEnumerable<JsonElement> issues,
        Func<int, JsonElement> readPull, Func<string, string, string> compare)
    {
        if (!GitObjectId.IsFull(source)) throw new InvalidOperationException("Release hold needs an exact source.");
        var held = new List<int>();
        // GitHub filters all labelled issues, including closed/merged PRs. Do
        // not restrict to enrollment time or last delivery: old code is included.
        foreach (var issue in issues)
        {
            if (!issue.TryGetProperty("pull_request", out _)) continue;
            var number = issue.GetProperty("number").GetInt32();
            var pull = readPull(number);
            if (!pull.GetProperty("merged").GetBoolean() || Text(pull.GetProperty("base"), "ref") != policy.DefaultBranch ||
                Text(pull.GetProperty("base").GetProperty("repo"), "full_name") != policy.Repository) continue;
            var merge = Text(pull, "merge_commit_sha");
            if (!GitObjectId.IsFull(merge)) throw new InvalidOperationException("Release-hold PR has no exact merge identity.");
            if (CompareContainsMerge(compare(merge, source))) held.Add(number);
        }
        return held.Distinct().Order().ToArray();
    }
}
#endif
