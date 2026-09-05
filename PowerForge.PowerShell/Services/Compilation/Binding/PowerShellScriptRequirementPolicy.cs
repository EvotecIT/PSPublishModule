using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Evaluates the file-level requirements that a named semantic profile can satisfy at compile time.</summary>
internal static class PowerShellScriptRequirementPolicy
{
    private static readonly Regex DirectivePattern = new(
        @"(?im)^\s*#requires\b(?<body>[^\r\n]*)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SwitchPattern = new(
        @"(?<![\w-])-(?<name>[A-Za-z][A-Za-z0-9]*)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EditionPattern = new(
        @"(?<![\w-])-PSEdition(?:\s+|:\s*)(?<edition>""[^""]*""|'[^']*'|[^\s#]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static string? GetFailure(ParsedSourceDocument document, string semanticProfileId)
    {
        if (document.SyntaxRoot.ScriptRequirements is not { } requirements)
            return null;

        var profile = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId);
        var unsupportedSwitch = DirectivePattern.Matches(document.SyntaxRoot.Extent.Text)
            .Cast<Match>()
            .SelectMany(static directive => SwitchPattern.Matches(directive.Groups["body"].Value).Cast<Match>())
            .Select(static option => option.Groups["name"].Value)
            .FirstOrDefault(static option =>
                !option.Equals("Version", StringComparison.OrdinalIgnoreCase) &&
                !option.Equals("PSEdition", StringComparison.OrdinalIgnoreCase));
        if (unsupportedSwitch is not null)
            return $"Source #requires -{unsupportedSwitch} cannot be satisfied by a runtime-free typed artifact.";

        if (requirements.RequiredModules.Count > 0 || requirements.RequiredAssemblies.Count > 0 ||
            !string.IsNullOrWhiteSpace(requirements.RequiredApplicationId) || requirements.IsElevationRequired)
            return "Source #requires declares a module, assembly, host, or elevation dependency that cannot be satisfied by a runtime-free typed artifact.";

        if (requirements.RequiredPSVersion is { } requiredVersion)
        {
            var profileVersion = GetMinimumVersion(profile);
            if (requiredVersion > profileVersion)
                return $"Source #requires needs PowerShell {requiredVersion} or newer, but semantic profile '{profile.ProfileId}' represents {profileVersion}.";
        }

        var requiredEditions = GetRequiredEditions(requirements, document.SyntaxRoot.Extent.Text);
        if (requiredEditions.Length > 0 &&
            !requiredEditions.Any(edition => edition.Equals(profile.PowerShellEdition, StringComparison.OrdinalIgnoreCase)))
            return $"Source #requires accepts PowerShell edition '{string.Join("' or '", requiredEditions)}', but semantic profile '{profile.ProfileId}' represents '{profile.PowerShellEdition}'.";

        if (requirements.RequiredPSVersion is null && requiredEditions.Length == 0)
            return "Source #requires does not contain a compile-time requirement supported by the selected semantic profile.";

        return null;
    }

    private static string[] GetRequiredEditions(ScriptRequirements requirements, string sourceText)
    {
        var property = typeof(ScriptRequirements).GetProperty("RequiredPSEditions");
        if (property?.GetValue(requirements) is System.Collections.IEnumerable editions)
        {
            return editions.Cast<object>()
                .Select(static edition => edition.ToString()?.Trim() ?? string.Empty)
                .Where(static edition => edition.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return DirectivePattern.Matches(sourceText)
            .Cast<Match>()
            .SelectMany(static directive => EditionPattern.Matches(directive.Groups["body"].Value).Cast<Match>())
            .Select(static match => match.Groups["edition"].Value.Trim().Trim('\'', '"'))
            .Where(static edition => edition.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Version GetMinimumVersion(PowerShellCompilationSemanticOracleProfile profile)
    {
        var separator = profile.VersionRange.IndexOf(',');
        var minimum = separator > 1 ? profile.VersionRange.Substring(1, separator - 1).Trim() : string.Empty;
        return Version.TryParse(minimum, out var version)
            ? version
            : throw new InvalidOperationException($"Semantic profile '{profile.ProfileId}' has invalid version range '{profile.VersionRange}'.");
    }
}
