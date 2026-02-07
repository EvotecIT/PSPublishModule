using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static class WebVerifyPolicy
{
    internal static (bool Success, string[] PolicyFailures) EvaluateOutcome(
        WebVerifyResult verify,
        bool failOnWarnings,
        bool failOnNavLint,
        bool failOnThemeContract)
    {
        var failures = new List<string>();

        if (verify is null)
            return (true, Array.Empty<string>());

        if (!verify.Success)
        {
            var firstError = verify.Errors.Length > 0
                ? verify.Errors[0]
                : "verify reported configuration errors.";
            failures.Add(firstError);
        }

        if (failOnWarnings && verify.Warnings.Length > 0)
            failures.Add($"fail-on-warnings enabled and verify produced {verify.Warnings.Length} warning(s).");

        if (failOnNavLint)
        {
            var navLintCount = verify.Warnings.Count(IsNavigationLintWarning);
            if (navLintCount > 0)
                failures.Add($"fail-on-nav-lint enabled and verify produced {navLintCount} navigation lint warning(s).");
        }

        if (failOnThemeContract)
        {
            var themeContractCount = verify.Warnings.Count(IsThemeContractWarning);
            if (themeContractCount > 0)
                failures.Add($"fail-on-theme-contract enabled and verify produced {themeContractCount} theme contract warning(s).");
        }

        var policyFailures = failures
            .Where(failure => !string.IsNullOrWhiteSpace(failure))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (policyFailures.Length == 0, policyFailures);
    }

    private static bool IsNavigationLintWarning(string warning) =>
        !string.IsNullOrWhiteSpace(warning) &&
        warning.StartsWith("Navigation lint:", StringComparison.OrdinalIgnoreCase);

    private static bool IsThemeContractWarning(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
            return false;

        return warning.StartsWith("Theme contract:", StringComparison.OrdinalIgnoreCase) ||
               warning.StartsWith("Theme '", StringComparison.OrdinalIgnoreCase) ||
               warning.Contains("theme manifest", StringComparison.OrdinalIgnoreCase);
    }
}
