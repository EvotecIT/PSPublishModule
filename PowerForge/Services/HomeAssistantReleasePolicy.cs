using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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

    internal static string BuildReleaseMetadata(string marker, string releaseCommitSha, string? requiredAsset)
        => $"{marker}{Environment.NewLine}" +
           $"<!-- powerforge-homeassistant release-commit:{releaseCommitSha} -->{Environment.NewLine}" +
           $"<!-- powerforge-homeassistant required-asset:{Convert.ToBase64String(Encoding.UTF8.GetBytes(requiredAsset ?? string.Empty))} -->";

    internal static string? ReadReleaseCommit(string releaseBody) {
        if (string.IsNullOrWhiteSpace(releaseBody)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            releaseBody,
            @"<!--\s*powerforge-homeassistant\s+release-commit:(?<sha>[0-9a-f]{40,64})\s*-->",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["sha"].Value : null;
    }

    internal static string? ReadRequiredAsset(string releaseBody) {
        if (string.IsNullOrWhiteSpace(releaseBody))
            throw new InvalidOperationException("The GitHub release body does not record its required HACS asset.");
        var match = System.Text.RegularExpressions.Regex.Match(
            releaseBody,
            @"<!--\s*powerforge-homeassistant\s+required-asset:(?<value>[A-Za-z0-9+/=]*)\s*-->",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new InvalidOperationException("The GitHub release body does not record its required HACS asset.");
        var value = Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups["value"].Value));
        return string.IsNullOrEmpty(value) ? null : value;
    }

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
