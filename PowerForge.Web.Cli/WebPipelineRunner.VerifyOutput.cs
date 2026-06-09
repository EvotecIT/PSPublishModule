using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static string BuildVerifyFailureSummary(
        WebVerifyResult verify,
        string[] filteredWarnings,
        string[] policyFailures,
        string? baselinePath,
        int baselineKeyCount,
        string[] newWarnings,
        int warningPreviewCount,
        int errorPreviewCount)
    {
        var safeWarningPreview = Math.Clamp(warningPreviewCount, 0, 50);
        var safeErrorPreview = Math.Clamp(errorPreviewCount, 0, 50);

        var parts = new List<string>
        {
            $"Verify failed ({verify.Errors.Length} errors, {filteredWarnings.Length} warnings)"
        };

        if (policyFailures.Length > 0)
            parts.Add($"policy: {TruncateForLog(policyFailures[0], 220)}");

        if (!string.IsNullOrWhiteSpace(baselinePath))
            parts.Add($"baseline {baselinePath} ({baselineKeyCount} keys)");

        if (newWarnings.Length > 0)
            parts.Add($"new-warnings {newWarnings.Length}");

        if (safeErrorPreview > 0 && verify.Errors.Length > 0)
        {
            var sampleErrors = verify.Errors
                .Where(static e => !string.IsNullOrWhiteSpace(e))
                .Take(safeErrorPreview)
                .Select(e => TruncateForLog(e, 220))
                .ToArray();
            if (sampleErrors.Length > 0)
            {
                var remaining = verify.Errors.Length - sampleErrors.Length;
                var preview = string.Join(" | ", sampleErrors);
                if (remaining > 0) preview += $" | +{remaining} more";
                parts.Add($"errors: {preview}");
            }
        }

        var warningSource = newWarnings.Length > 0 ? newWarnings : filteredWarnings;
        if (safeWarningPreview > 0 && warningSource.Length > 0)
        {
            var sampleWarnings = warningSource
                .Where(static w => !string.IsNullOrWhiteSpace(w))
                .Take(safeWarningPreview)
                .Select(w => TruncateForLog(w, 220))
                .ToArray();
            if (sampleWarnings.Length > 0)
            {
                var remaining = warningSource.Length - sampleWarnings.Length;
                var preview = string.Join(" | ", sampleWarnings);
                if (remaining > 0) preview += $" | +{remaining} more";
                parts.Add($"warnings: {preview}");
            }
        }

        return string.Join(", ", parts);
    }
}

