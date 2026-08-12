using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PowerForge.Web.Cli;

/// <summary>Builds stable site-scoped rule ownership keys and recognizes earlier description formats.</summary>
internal static class CloudflareManagedRuleOwnership
{
    private static readonly Regex PathOperandPattern = new(
        "http\\.request\\.uri\\.path\\s+(?:eq|wildcard)\\s+\"((?:\\\\.|[^\"])*)\"|" +
        "starts_with\\(http\\.request\\.uri\\.path,\\s*\"((?:\\\\.|[^\"])*)\"\\)",
        RegexOptions.CultureInvariant);

    internal static string BuildOwnershipPrefix(string hostname, string? basePath)
    {
        var normalizedBasePath = CloudflareCachePolicyBuilder.NormalizeBasePath(basePath);
        return $"PowerForge [{hostname}{normalizedBasePath}]:";
    }

    internal static string BuildDescriptionPrefix(string policyName, string hostname, string? basePath) =>
        $"{BuildOwnershipPrefix(hostname, basePath)} {policyName}:";

    internal static bool IsLegacyRuleForSite(
        JsonObject rule,
        string policyName,
        string hostname,
        string? basePath)
    {
        var description = rule["description"]?.GetValue<string>() ?? string.Empty;
        var expression = rule["expression"]?.GetValue<string>() ?? string.Empty;
        var previousHostScopedSuffix = $" [{hostname}]:";
        var normalizedBasePath = CloudflareCachePolicyBuilder.NormalizeBasePath(basePath);
        if (normalizedBasePath == "/" &&
            description.StartsWith($"PowerForge {policyName}{previousHostScopedSuffix}", StringComparison.Ordinal))
            return true;

        var hasPreviousDescription = description.StartsWith("PowerForge ", StringComparison.Ordinal) &&
                                     description.Contains(previousHostScopedSuffix, StringComparison.Ordinal);
        var hasOldestDescription = description.StartsWith($"PowerForge {policyName}:", StringComparison.Ordinal);

        return (hasPreviousDescription || hasOldestDescription) &&
               expression.Contains($"http.host eq \"{hostname}\"", StringComparison.Ordinal) &&
               ExpressionMatchesBasePath(expression, basePath);
    }

    private static bool ExpressionMatchesBasePath(string expression, string? basePath)
    {
        var normalizedBasePath = CloudflareCachePolicyBuilder.NormalizeBasePath(basePath);
        var paths = ExtractPathOperands(expression);
        if (normalizedBasePath != "/")
        {
            var root = normalizedBasePath.TrimEnd('/');
            return paths.Count > 0 && paths.All(path =>
                path.Equals(root, StringComparison.Ordinal) ||
                path.StartsWith(normalizedBasePath, StringComparison.Ordinal));
        }

        if (paths.Count == 0 || paths.Any(path => path.StartsWith("/*", StringComparison.Ordinal)))
            return true;

        var topLevelSegments = paths
            .Select(path => path.TrimStart('/').Split('/', 2)[0])
            .Where(segment => segment.Length > 0 && !segment.Contains('*'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (topLevelSegments.Length != 1)
            return true;

        // Root discovery resources legitimately share this reserved directory.
        // Any other single-directory scope is conservatively treated as a subsite.
        return topLevelSegments[0].Equals(".well-known", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ExtractPathOperands(string expression)
    {
        var paths = new List<string>();
        foreach (Match match in PathOperandPattern.Matches(expression))
        {
            var encoded = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            paths.Add(encoded
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal));
        }
        return paths;
    }
}
