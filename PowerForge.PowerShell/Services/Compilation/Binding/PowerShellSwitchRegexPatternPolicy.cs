using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Keeps runtime-free regex-switch patterns inside one deterministic, target-stable error boundary.
/// </summary>
internal static class PowerShellSwitchRegexPatternPolicy
{
    internal static bool TryValidateLiteral(PowerShellBoundExpression expression, out string diagnostic)
    {
        if (expression is not PowerShellBoundLiteralExpression { Value: string pattern })
        {
            diagnostic = "Scalar regex switch requires compile-time literal String patterns so target runtimes cannot expose different invalid-pattern error identities.";
            return false;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            diagnostic = string.Empty;
            return true;
        }
        catch (ArgumentException)
        {
            diagnostic = "Scalar regex switch pattern is not a valid regular expression in the bounded runtime-free contract.";
            return false;
        }
    }
}
