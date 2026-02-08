using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge.Web.Cli;

internal static class WebWarningBucketer
{
    internal static string BuildTopBucketsSummary(string[] warnings, int topN)
    {
        if (warnings is null || warnings.Length == 0)
            return "Warning buckets: (none)";

        var safeTopN = Math.Clamp(topN, 1, 50);
        var buckets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var warning in warnings)
        {
            if (string.IsNullOrWhiteSpace(warning))
                continue;

            var code = TryGetBracketCode(warning) ?? "UNCODED";
            buckets[code] = buckets.TryGetValue(code, out var count) ? count + 1 : 1;
        }

        if (buckets.Count == 0)
            return "Warning buckets: (none)";

        var ordered = buckets
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var top = ordered.Take(safeTopN).ToArray();
        var otherCount = ordered.Skip(safeTopN).Sum(kvp => kvp.Value);
        var parts = top.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList();
        if (otherCount > 0)
            parts.Add($"OTHER={otherCount}");

        return $"Warning buckets (top {safeTopN}): {string.Join(", ", parts)}";
    }

    private static string? TryGetBracketCode(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var trimmed = message.TrimStart();
        if (trimmed.Length < 3 || trimmed[0] != '[')
            return null;

        var end = trimmed.IndexOf(']');
        if (end <= 1)
            return null;

        var code = trimmed.Substring(1, end - 1).Trim();
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }
}

