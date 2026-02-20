using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebVerifyPolicyTests
{
    [Fact]
    public void EvaluateOutcome_FailOnNavLint_UsesWarningCodeClassification()
    {
        var verify = new WebVerifyResult
        {
            Success = true,
            Errors = Array.Empty<string>(),
            Warnings = new[]
            {
                "[PFWEB.NAV.LINT] Navigation lint: expected nav surface 'docs' is missing from Navigation.Surfaces."
            }
        };

        var (success, failures) = WebVerifyPolicy.EvaluateOutcome(
            verify,
            failOnWarnings: false,
            failOnNavLint: true,
            failOnThemeContract: false);

        Assert.False(success);
        Assert.Contains(failures, failure => failure.Contains("fail-on-nav-lint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateOutcome_FailOnThemeContract_UsesWarningCodeClassification()
    {
        var verify = new WebVerifyResult
        {
            Success = true,
            Errors = Array.Empty<string>(),
            Warnings = new[]
            {
                "[PFWEB.THEME.CSS.CONTRACT] Theme CSS contract: feature 'apidocs' missing expected selectors."
            }
        };

        var (success, failures) = WebVerifyPolicy.EvaluateOutcome(
            verify,
            failOnWarnings: false,
            failOnNavLint: false,
            failOnThemeContract: true);

        Assert.False(success);
        Assert.Contains(failures, failure => failure.Contains("fail-on-theme-contract", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FilterWarnings_SupportsCodeSuppression()
    {
        var warnings = new[]
        {
            "[PFWEB.NAV.LINT] Navigation lint: expected nav surface 'docs' is missing.",
            "[PFWEB.THEME.CONTRACT] Theme contract: feature 'apidocs' requires nav surface 'apidocs'."
        };

        var filtered = WebVerifyPolicy.FilterWarnings(warnings, new[] { "PFWEB.NAV.LINT" });

        Assert.Single(filtered);
        Assert.Contains("Theme contract:", filtered[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateOutcome_FallsBackToLegacyMessageClassification()
    {
        var verify = new WebVerifyResult
        {
            Success = true,
            Errors = Array.Empty<string>(),
            Warnings = new[]
            {
                "Navigation lint: expected nav surface 'docs' is missing from Navigation.Surfaces."
            }
        };

        var (success, failures) = WebVerifyPolicy.EvaluateOutcome(
            verify,
            failOnWarnings: false,
            failOnNavLint: true,
            failOnThemeContract: false);

        Assert.False(success);
        Assert.Contains(failures, failure => failure.Contains("fail-on-nav-lint", StringComparison.OrdinalIgnoreCase));
    }
}
