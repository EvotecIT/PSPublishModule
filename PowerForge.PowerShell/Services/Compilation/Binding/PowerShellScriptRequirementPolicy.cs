using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Evaluates the file-level requirements that a named semantic profile can satisfy at compile time.</summary>
internal static class PowerShellScriptRequirementPolicy
{
    private static readonly Regex EditionPattern = new(
        @"(?<![\w-])-PSEdition(?:\s+|:\s*)(?<edition>""[^""]*""|'[^']*'|[^\s#]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Qualifies simple installed module names checked by the authored Hybrid script load.</summary>
    internal static bool UsesNativeModuleRequirements(ParsedSourceDocument document, PowerShellCompilationCapability capabilities)
        => capabilities == PowerShellCompilationCapabilities.HybridModule &&
           document.SyntaxRoot.ScriptRequirements is { RequiredModules.Count: > 0 } requirements &&
           requirements.RequiredModules.All(static module => !string.IsNullOrWhiteSpace(module.Name) &&
               module.Name.All(static character => char.IsLetterOrDigit(character) || character is '.' or '_'));

    internal static string? GetFailure(ParsedSourceDocument document, string semanticProfileId,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
    {
        if (document.SyntaxRoot.ScriptRequirements is not { } requirements)
            return null;

        var nativeModules = UsesNativeModuleRequirements(document, capabilities);
        var profile = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId);
        var directives = GetDirectiveTokens(document.Tokens).ToArray();
        var unsupportedSwitch = directives.SelectMany(static token => GetNativeDirectiveOptions(token.Text))
            .FirstOrDefault(option =>
                !option.Equals("Version", StringComparison.OrdinalIgnoreCase) &&
                !option.Equals("PSEdition", StringComparison.OrdinalIgnoreCase) &&
                !(nativeModules && option.Equals("Modules", StringComparison.OrdinalIgnoreCase)));
        if (unsupportedSwitch is not null)
            return $"Source #requires -{unsupportedSwitch} cannot be satisfied by a runtime-free typed artifact.";

        if (requirements.RequiredModules.Count > 0 && !nativeModules || requirements.RequiredAssemblies.Count > 0 ||
            !string.IsNullOrWhiteSpace(requirements.RequiredApplicationId) || requirements.IsElevationRequired)
            return "Source #requires declares a module, assembly, host, or elevation dependency that cannot be satisfied by a runtime-free typed artifact.";

        if (requirements.RequiredPSVersion is { } requiredVersion)
        {
            var profileVersion = GetMinimumVersion(profile);
            if (requiredVersion > profileVersion)
                return $"Source #requires needs PowerShell {requiredVersion} or newer, but semantic profile '{profile.ProfileId}' represents {profileVersion}.";
        }

        var requiredEditions = GetRequiredEditions(requirements, directives);
        if (requiredEditions.Length > 0 &&
            !requiredEditions.Any(edition => edition.Equals(profile.PowerShellEdition, StringComparison.OrdinalIgnoreCase)))
            return $"Source #requires accepts PowerShell edition '{string.Join("' or '", requiredEditions)}', but semantic profile '{profile.ProfileId}' represents '{profile.PowerShellEdition}'.";

        if (!nativeModules && requirements.RequiredPSVersion is null && requiredEditions.Length == 0)
            return "Source #requires does not contain a compile-time requirement supported by the selected semantic profile.";

        return null;
    }

    /// <summary>Checks requirements against the PowerShell SDK embedded in a retained executable.</summary>
    internal static string? GetPackagedEntryFailure(ScriptBlockAst ast, IEnumerable<Token> tokens)
    {
        if (ast.ScriptRequirements is not { } requirements)
            return null;

        var directives = GetDirectiveTokens(tokens).ToArray();
        var unsupported = GetUnsupportedPackagedRequirement(requirements) ??
            directives.SelectMany(static token => GetNativeDirectiveOptions(token.Text))
                .FirstOrDefault(static option =>
                    "PSSnapin".StartsWith(option, StringComparison.OrdinalIgnoreCase) ||
                    !("Version".StartsWith(option, StringComparison.OrdinalIgnoreCase) ||
                      "PSEdition".StartsWith(option, StringComparison.OrdinalIgnoreCase)));
        if (unsupported is not null)
            return $"#requires -{unsupported} needs a dependency or execution contract that packaged entries do not yet support.";

        var version = Version.Parse(PowerShellCompilationGeneratedPackageCatalog.GetVersion("Microsoft.PowerShell.SDK"));
        if (requirements.RequiredPSVersion is { } minimum && !IsPackagedVersionCompatible(minimum, version))
            return $"#requires specifies PowerShell {minimum}, which the packaged PowerShell {version} host does not accept.";

        var editions = GetRequiredEditions(requirements, directives);
        if (editions.Length > 0 && !editions.Contains("Core", StringComparer.OrdinalIgnoreCase))
            return $"#requires accepts PowerShell edition '{string.Join("' or '", editions)}', but this package carries PowerShell Core.";

        return null;
    }

    private static string? GetUnsupportedPackagedRequirement(ScriptRequirements requirements)
    {
        if (requirements.RequiredModules.Count > 0) return "Modules";
        if (requirements.RequiredAssemblies.Count > 0) return "Assembly";
        if (requirements.IsElevationRequired) return "RunAsAdministrator";
        return string.IsNullOrEmpty(requirements.RequiredApplicationId) ? null : "ShellId";
    }

    private static IEnumerable<string> GetNativeDirectiveOptions(string directive)
    {
        var statement = Parser.ParseInput(directive.Substring(1), out _, out _);
        return statement.FindAll(static node => node is CommandParameterAst, searchNestedScriptBlocks: false)
            .Cast<CommandParameterAst>().Select(static parameter => parameter.ParameterName);
    }

    // PowerShell 7 checks historical major/minor combinations and ignores patch and revision.
    private static bool IsPackagedVersionCompatible(Version requirement, Version runtime)
        => requirement.Major switch
        {
            1 or 2 or 3 or 4 => requirement.Minor == 0,
            5 => requirement.Minor is 0 or 1,
            6 => requirement.Minor is >= 0 and <= 2,
            7 => requirement.Minor >= 0 && requirement.Minor <= runtime.Minor,
            _ => false
        };

    private static IEnumerable<Token> GetDirectiveTokens(IEnumerable<Token> tokens)
    {
        const string prefix = "#requires";
        return tokens.Where(static token =>
            token.Kind == TokenKind.Comment &&
            token.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            (token.Text.Length == prefix.Length || char.IsWhiteSpace(token.Text[prefix.Length])));
    }

    private static string[] GetRequiredEditions(ScriptRequirements requirements, IEnumerable<Token> directives)
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

        return directives
            .SelectMany(static directive => EditionPattern.Matches(directive.Text).Cast<Match>())
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
