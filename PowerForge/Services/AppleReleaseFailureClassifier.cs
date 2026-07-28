using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Converts Apple tool and App Store Connect failures into compact actionable diagnostics.
/// </summary>
internal static class AppleReleaseFailureClassifier
{
    private static readonly (Regex Pattern, string Category, string Code, string Action, bool Retryable)[] Rules =
    {
        Rule("provisioning profile|requires a provisioning profile|No profiles for", "signing", "APPLE_PROVISIONING", "Refresh or select the matching provisioning profile and verify the bundle id, team, capabilities, and signing certificate.", false),
        Rule("certificate.*expired|expired.*certificate|No signing certificate", "signing", "APPLE_CERTIFICATE", "Renew or install the required Apple signing certificate and re-run Doctor before rebuilding.", false),
        Rule("ITMS-[0-9]+|Asset validation failed|Invalid Bundle", "validation", "APPLE_ASSET_VALIDATION", "Open the retained Xcode distribution log, correct the reported bundle, entitlement, metadata, or binary validation issue, then increment the build before uploading again.", false),
        Rule("authentication|unauthorized|forbidden|401|403|API key", "authentication", "APPLE_AUTH", "Verify the App Store Connect key id, issuer id, private key, role, and repository secret wiring.", false),
        Rule("timed out|timeout|temporarily unavailable|5[0-9]{2}", "transient", "APPLE_TRANSIENT", "Keep the receipt and retry the smallest failed action after confirming Apple service health.", true),
        Rule("already contains build|bundle version.*must be higher|CFBundleVersion", "versioning", "APPLE_BUILD_NUMBER", "Choose a build number above local and remote history, update the version source, and create a new archive.", false),
        Rule("screenshot|APP_IPHONE|display type", "screenshots", "APPLE_SCREENSHOTS", "Regenerate or remap the affected screenshot set, review its approval manifest, and retry Screenshots only.", false),
        Rule("not ready|readiness|missing.*metadata|localization", "readiness", "APPLE_READINESS", "Run Doctor or Prepare, complete the reported App Store metadata/compliance fields, and retry the gated action.", false),
        Rule("notary|notarization|stapler|Developer ID|spctl", "notarization", "APPLE_NOTARIZATION", "Inspect the notarization log, correct signing, hardened runtime, entitlements, or package contents, then submit a newly signed artifact.", false)
    };

    internal static PowerForgeAppleReleaseDiagnostic[] Classify(
        string? message,
        string? processingState = null,
        string? standardOutput = null,
        string? standardError = null)
    {
        if (string.IsNullOrWhiteSpace(message) && !IsTerminalProcessingState(processingState))
            return Array.Empty<PowerForgeAppleReleaseDiagnostic>();

        var evidence = string.Join(
            Environment.NewLine,
            new[] { message, processingState, standardOutput, standardError }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(evidence))
            return Array.Empty<PowerForgeAppleReleaseDiagnostic>();

        var matches = Rules
            .Where(rule => rule.Pattern.IsMatch(evidence))
            .Select(rule => Create(rule.Category, rule.Code, message ?? processingState ?? "Apple release failed.", evidence, rule.Action, rule.Retryable))
            .GroupBy(static diagnostic => diagnostic.Code, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        if (matches.Length > 0)
            return matches;

        if (IsTerminalProcessingState(processingState))
        {
            return new[]
            {
                Create(
                    "processing",
                    "APPLE_PROCESSING",
                    message ?? $"Apple build processing ended in state '{processingState}'.",
                    evidence,
                    "Inspect the build-upload issues and Xcode distribution log, fix the reported cause, increment the build, and upload a new archive.",
                    false)
            };
        }

        return new[]
        {
            Create(
                "unknown",
                "APPLE_UNKNOWN",
                message ?? processingState ?? "Apple release failed.",
                evidence,
                "Inspect the retained receipt and tool output, run Doctor, and retry only the smallest failed action after identifying the cause.",
                false)
        };
    }

    private static bool IsTerminalProcessingState(string? value)
        => value is not null &&
           (value.Equals("INVALID", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("REJECTED", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("ERROR", StringComparison.OrdinalIgnoreCase));

    private static (Regex Pattern, string Category, string Code, string Action, bool Retryable) Rule(
        string pattern,
        string category,
        string code,
        string action,
        bool retryable)
        => (new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), category, code, action, retryable);

    private static PowerForgeAppleReleaseDiagnostic Create(
        string category,
        string code,
        string summary,
        string evidence,
        string action,
        bool retryable)
        => new()
        {
            Severity = "error",
            Category = category,
            Code = code,
            Summary = summary.Trim(),
            Evidence = Compact(evidence),
            Action = action,
            Retryable = retryable
        };

    private static string Compact(string value)
    {
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= 1200 ? normalized : normalized.Substring(0, 1200) + "…";
    }
}
