using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

internal static class PrivateModuleVersionPolicySupport
{
    internal static IReadOnlyDictionary<string, string> BuildRequiredVersions(
        IEnumerable<string> names,
        string? requiredVersion,
        string? versionPolicy)
    {
        if (!string.IsNullOrWhiteSpace(requiredVersion) && !string.IsNullOrWhiteSpace(versionPolicy))
            throw new InvalidOperationException("RequiredVersion cannot be combined with VersionPolicy.");

        var required = string.IsNullOrWhiteSpace(requiredVersion)
            ? ParseVersionPolicy(versionPolicy).RequiredVersion
            : requiredVersion!.Trim();
        return BuildVersionDictionary(names, required);
    }

    internal static IReadOnlyDictionary<string, string> BuildMinimumVersions(IEnumerable<string> names, string? versionPolicy)
        => BuildVersionDictionary(names, ParseVersionPolicy(versionPolicy).MinimumVersion);

    internal static IReadOnlyDictionary<string, bool> BuildMinimumVersionInclusivity(IEnumerable<string> names, string? versionPolicy)
    {
        var constraint = ParseVersionPolicy(versionPolicy);
        return BuildInclusivityDictionary(names, constraint.MinimumVersion, constraint.MinimumVersionInclusive);
    }

    internal static IReadOnlyDictionary<string, string> BuildMaximumVersions(IEnumerable<string> names, string? versionPolicy)
        => BuildVersionDictionary(names, ParseVersionPolicy(versionPolicy).MaximumVersion);

    internal static IReadOnlyDictionary<string, bool> BuildMaximumVersionInclusivity(IEnumerable<string> names, string? versionPolicy)
    {
        var constraint = ParseVersionPolicy(versionPolicy);
        return BuildInclusivityDictionary(names, constraint.MaximumVersion, constraint.MaximumVersionInclusive);
    }

    private static IReadOnlyDictionary<string, string> BuildVersionDictionary(IEnumerable<string> names, string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return NormalizeNames(names)
            .ToDictionary(static name => name, _ => version!.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, bool> BuildInclusivityDictionary(
        IEnumerable<string> names,
        string? boundary,
        bool inclusive)
    {
        if (string.IsNullOrWhiteSpace(boundary))
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        return NormalizeNames(names)
            .ToDictionary(static name => name, _ => inclusive, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> NormalizeNames(IEnumerable<string> names)
        => (names ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static ModuleStateVersionConstraint ParseVersionPolicy(string? versionPolicy)
    {
        var policy = versionPolicy?.Trim();
        if (policy is null || policy.Length == 0 || string.Equals(policy, "*", StringComparison.Ordinal))
            return ModuleStateVersionConstraint.Empty;

        string? requiredVersion = null;
        string? minimumVersion = null;
        var minimumVersionInclusive = true;
        string? maximumVersion = null;
        var maximumVersionInclusive = true;
        foreach (var token in policy.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith(">=", StringComparison.Ordinal))
            {
                minimumVersion = token.Substring(2).Trim();
                minimumVersionInclusive = true;
            }
            else if (token.StartsWith(">", StringComparison.Ordinal))
            {
                minimumVersion = token.Substring(1).Trim();
                minimumVersionInclusive = false;
            }
            else if (token.StartsWith("<=", StringComparison.Ordinal))
            {
                maximumVersion = token.Substring(2).Trim();
                maximumVersionInclusive = true;
            }
            else if (token.StartsWith("<", StringComparison.Ordinal))
            {
                maximumVersion = token.Substring(1).Trim();
                maximumVersionInclusive = false;
            }
            else if (token.StartsWith("=", StringComparison.Ordinal))
            {
                requiredVersion = token.Substring(1).Trim();
            }
            else
            {
                requiredVersion = token.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(requiredVersion) &&
            (!string.IsNullOrWhiteSpace(minimumVersion) || !string.IsNullOrWhiteSpace(maximumVersion)))
        {
            throw new InvalidOperationException("VersionPolicy cannot combine exact and range constraints.");
        }

        return new ModuleStateVersionConstraint(
            requiredVersion,
            minimumVersion,
            minimumVersionInclusive,
            maximumVersion,
            maximumVersionInclusive);
    }
}
