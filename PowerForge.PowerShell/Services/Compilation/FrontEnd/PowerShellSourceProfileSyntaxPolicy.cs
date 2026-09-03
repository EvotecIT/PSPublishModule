using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Owns conservative source-syntax compatibility checks for one selected semantic host profile.</summary>
internal static class PowerShellSourceProfileSyntaxPolicy
{
    internal static Token[] FindUnsupportedTokens(IEnumerable<Token> tokens, string semanticProfileId)
    {
        var profile = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId);
        if (profile.Family != PowerShellCompilationSemanticHostFamily.WindowsPowerShell51)
            return Array.Empty<Token>();

        return tokens
            .Where(static token => token.Kind == TokenKind.Number)
            .Where(token => !IsNumericLiteralSupported(token.Extent.Text, profile.Family))
            .ToArray();
    }

    internal static bool IsNumericLiteralSupported(string text, string semanticProfileId)
        => IsNumericLiteralSupported(
            text,
            PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId).Family);

    private static bool IsNumericLiteralSupported(
        string text,
        PowerShellCompilationSemanticHostFamily profileFamily)
    {
        if (profileFamily != PowerShellCompilationSemanticHostFamily.WindowsPowerShell51)
            return true;

        var value = text.Trim().TrimStart('+', '-').ToLowerInvariant();
        if (value.StartsWith("0b", StringComparison.Ordinal)) return false;
        foreach (var multiplier in new[] { "kb", "mb", "gb", "tb", "pb" })
        {
            if (!value.EndsWith(multiplier, StringComparison.Ordinal)) continue;
            value = value.Substring(0, value.Length - multiplier.Length);
            break;
        }
        return !new[] { "uy", "us", "ul", "y", "s", "u", "n" }
            .Any(suffix => value.EndsWith(suffix, StringComparison.Ordinal));
    }
}
