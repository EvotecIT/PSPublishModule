using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static class WebVerifyPolicy
{
    internal static (bool Success, string[] PolicyFailures) EvaluateOutcome(
        WebVerifyResult verify,
        bool failOnWarnings,
        bool failOnNavLint,
        bool failOnThemeContract,
        string[]? suppressWarnings = null)
    {
        var failures = new List<string>();

        if (verify is null)
            return (true, Array.Empty<string>());

        var warnings = FilterWarnings(verify.Warnings, suppressWarnings);

        if (!verify.Success)
        {
            var firstError = verify.Errors.Length > 0
                ? verify.Errors[0]
                : "verify reported configuration errors.";
            failures.Add(firstError);
        }

        if (failOnWarnings && warnings.Length > 0)
            failures.Add($"fail-on-warnings enabled and verify produced {warnings.Length} warning(s).");

        if (failOnNavLint)
        {
            var navLintCount = warnings.Count(IsNavigationLintWarning);
            if (navLintCount > 0)
                failures.Add($"fail-on-nav-lint enabled and verify produced {navLintCount} navigation lint warning(s).");
        }

        if (failOnThemeContract)
        {
            var themeContractCount = warnings.Count(IsThemeContractWarning);
            if (themeContractCount > 0)
                failures.Add($"fail-on-theme-contract enabled and verify produced {themeContractCount} theme contract warning(s).");
        }

        var policyFailures = failures
            .Where(failure => !string.IsNullOrWhiteSpace(failure))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (policyFailures.Length == 0, policyFailures);
    }

    internal static string[] FilterWarnings(string[] warnings, string[]? suppressWarnings)
    {
        if (warnings is null || warnings.Length == 0)
            return Array.Empty<string>();

        var patterns = suppressWarnings is { Length: > 0 }
            ? suppressWarnings.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(static s => s.Trim()).ToArray()
            : Array.Empty<string>();
        if (patterns.Length == 0)
            return warnings;

        var filtered = new List<string>(warnings.Length);
        foreach (var warning in warnings)
        {
            if (string.IsNullOrWhiteSpace(warning))
                continue;
            if (ShouldSuppress(warning, patterns))
                continue;
            filtered.Add(warning);
        }
        return filtered.ToArray();
    }

    private static bool IsNavigationLintWarning(string warning) =>
        HasWarningCodePrefix(warning, "PFWEB.NAV.") ||
        (!string.IsNullOrWhiteSpace(warning) &&
         StripCodePrefix(warning).StartsWith("Navigation lint:", StringComparison.OrdinalIgnoreCase));

    private static bool IsThemeContractWarning(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
            return false;

        var code = TryGetCode(warning);
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (code.StartsWith("PFWEB.THEME.CONTRACT", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("PFWEB.THEME.CSS.CONTRACT", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var normalized = StripCodePrefix(warning);
        return normalized.StartsWith("Theme contract:", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Theme CSS contract:", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Theme '", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("theme manifest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSuppress(string warning, string[] patterns)
    {
        if (patterns.Length == 0) return false;

        var code = TryGetCode(warning);
        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var pattern = raw.Trim();
            if (pattern.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
            {
                var rx = pattern.Substring(3);
                if (string.IsNullOrWhiteSpace(rx))
                    continue;

                try
                {
                    if (Regex.IsMatch(warning, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                        return true;
                }
                catch
                {
                    // ignore invalid regex patterns
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(code) && string.Equals(code, pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            if (pattern.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                if (WildcardIsMatch(warning, pattern))
                    return true;
                continue;
            }

            if (warning.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string StripCodePrefix(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
            return string.Empty;

        var trimmed = warning.TrimStart();
        if (trimmed.Length < 3 || trimmed[0] != '[')
            return trimmed;

        var end = trimmed.IndexOf(']');
        if (end <= 0)
            return trimmed;

        // "[CODE] message" -> "message"
        return trimmed.Substring(end + 1).TrimStart();
    }

    private static string? TryGetCode(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
            return null;

        var trimmed = warning.TrimStart();
        if (trimmed.Length < 3 || trimmed[0] != '[')
            return null;

        var end = trimmed.IndexOf(']');
        if (end <= 1)
            return null;

        var code = trimmed.Substring(1, end - 1).Trim();
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }

    private static bool HasWarningCodePrefix(string warning, string codePrefix)
    {
        if (string.IsNullOrWhiteSpace(codePrefix))
            return false;

        var code = TryGetCode(warning);
        return !string.IsNullOrWhiteSpace(code) &&
               code.StartsWith(codePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool WildcardIsMatch(string input, string pattern)
    {
        // Translate wildcard to regex for consistent behavior across TFMs.
        try
        {
            var rx = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return Regex.IsMatch(input ?? string.Empty, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch
        {
            return false;
        }
    }
}
