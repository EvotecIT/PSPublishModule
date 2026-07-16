using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

internal static class HomeAssistantReleasePolicy {
    private static readonly Dictionary<string, HomeAssistantVersionIncrement> ReleaseLabels =
        new(StringComparer.OrdinalIgnoreCase) {
            ["release:none"] = HomeAssistantVersionIncrement.None,
            ["release:patch"] = HomeAssistantVersionIncrement.Patch,
            ["release:minor"] = HomeAssistantVersionIncrement.Minor,
            ["release:major"] = HomeAssistantVersionIncrement.Major
        };

    internal static HomeAssistantVersionIncrement Resolve(
        IReadOnlyCollection<string> labels,
        IReadOnlyCollection<string> changedFiles,
        HomeAssistantVersionIncrement? explicitIncrement) {
        if (explicitIncrement.HasValue)
            return explicitIncrement.Value;

        var requested = labels
            .Where(label => ReleaseLabels.ContainsKey(label))
            .Select(label => ReleaseLabels[label])
            .Distinct()
            .ToArray();

        if (requested.Length > 1)
            throw new InvalidOperationException("The pull request has conflicting release labels. Use exactly one release:none, release:patch, release:minor, or release:major label.");

        if (requested.Length == 1)
            return requested[0];

        return changedFiles.Any(IsProductChange)
            ? HomeAssistantVersionIncrement.Patch
            : HomeAssistantVersionIncrement.None;
    }

    internal static string BuildMarker(int pullRequestNumber, string mergeCommitSha)
        => $"<!-- powerforge-homeassistant source-pr:{pullRequestNumber} merge:{mergeCommitSha} -->";

    internal static string BuildReleaseNotes(HomeAssistantPullRequest pullRequest, string marker)
        => $"{marker}{Environment.NewLine}{Environment.NewLine}Released from #{pullRequest.Number}: [{pullRequest.Title}]({pullRequest.HtmlUrl}).";

    private static bool IsProductChange(string path) {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var fileName = Path.GetFileName(normalized);

        if (normalized.StartsWith(".github/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("test/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(".agents/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(".codex/", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (fileName.StartsWith("README", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return true;
    }
}