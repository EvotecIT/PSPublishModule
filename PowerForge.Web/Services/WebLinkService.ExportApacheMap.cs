using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Web;

/// <summary>Indexed Apache map export operations for exact shortlinks.</summary>
public static partial class WebLinkService
{
    private const string ApacheShortlinkMapName = "powerforge_shortlinks";

    private static ApacheShortlinkMapEntry[] BuildApacheShortlinkMapEntries(
        IReadOnlyList<LinkRedirectRule> rules,
        WebLinkApacheExportOptions options)
    {
        ValidateApacheShortlinkMapOptions(options);
        if (string.IsNullOrWhiteSpace(options.ShortlinkMapOutputPath))
            return Array.Empty<ApacheShortlinkMapEntry>();

        var entries = new List<ApacheShortlinkMapEntry>();
        var precedingOrdinaryRules = new List<LinkRedirectRule>();
        foreach (var rule in rules)
        {
            if (!IsApacheShortlinkMapRule(rule, options) ||
                precedingOrdinaryRules.Any(preceding => ApacheRulesMayOverlap(preceding, rule)))
            {
                precedingOrdinaryRules.Add(rule);
                continue;
            }

            entries.Add(CreateApacheShortlinkMapEntry(rule, options));
        }

        return entries
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static ApacheShortlinkMapEntry CreateApacheShortlinkMapEntry(
        LinkRedirectRule rule,
        WebLinkApacheExportOptions options)
    {
        var destination = NormalizeApacheDestination(rule.TargetUrl, rule.SourceHost, options.LanguageRootHosts);
        if (!IsSafeApacheRewriteSubstitution(destination))
            throw new InvalidOperationException($"Apache redirect target contains characters that must be URL-encoded before export: {rule.Id ?? rule.SourcePath}");
        return new ApacheShortlinkMapEntry(
            BuildApacheShortlinkMapKey(rule),
            destination,
            rule.SourceHost!.Trim(),
            ResolveStatus(rule.Status, defaultStatus: 302),
            rule);
    }

    private static bool IsApacheShortlinkMapRule(LinkRedirectRule rule, WebLinkApacheExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ShortlinkMapOutputPath) ||
            string.IsNullOrWhiteSpace(options.ShortlinkMapRuntimePath))
            return false;

        var host = rule.SourceHost?.Trim() ?? string.Empty;
        var path = NormalizeSourcePath(rule.SourcePath).Trim('/');
        var mapSafe = host.Length > 0 && path.Length > 0 &&
                      !host.Any(static value => char.IsControl(value) || char.IsWhiteSpace(value) || value == ':') &&
                      !path.Any(static value => char.IsControl(value) || char.IsWhiteSpace(value) || value == ':');

        return mapSafe &&
               string.Equals(rule.Source, "shortlink", StringComparison.Ordinal) &&
               rule.MatchType == LinkRedirectMatchType.Exact &&
               !string.Equals(host, "*", StringComparison.Ordinal) &&
               string.IsNullOrWhiteSpace(rule.SourceQuery) &&
               string.IsNullOrWhiteSpace(rule.SourceQueryParameter) &&
               !rule.PreserveQuery &&
               rule.Status is 301 or 302 or 307 or 308;
    }

    private static void ValidateApacheShortlinkMapOptions(WebLinkApacheExportOptions options)
    {
        var hasOutput = !string.IsNullOrWhiteSpace(options.ShortlinkMapOutputPath);
        var hasRuntime = !string.IsNullOrWhiteSpace(options.ShortlinkMapRuntimePath);
        if (hasOutput != hasRuntime)
            throw new InvalidOperationException("Apache shortlink-map export requires both output and runtime paths.");
        if (!hasRuntime)
            return;

        var configurationOutputPath = Path.GetFullPath(options.OutputPath);
        var mapOutputPath = Path.GetFullPath(options.ShortlinkMapOutputPath!);
        // A build directory can live on a case-insensitive volume regardless of
        // the host OS. Conservatively reject case-only differences everywhere.
        if (string.Equals(configurationOutputPath, mapOutputPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Apache configuration and shortlink-map outputs must use different paths.");

        var runtimePath = options.ShortlinkMapRuntimePath!.Trim();
        if (!Path.IsPathRooted(runtimePath) || runtimePath.Any(static value => char.IsControl(value) || char.IsWhiteSpace(value) || value is '"' or '\'' or '$' or '{' or '}'))
            throw new InvalidOperationException("Apache shortlink-map runtime path must be an absolute path without shell or configuration metacharacters.");
    }

    private static string BuildApacheShortlinkMapKey(LinkRedirectRule rule)
    {
        var host = rule.SourceHost!.Trim().ToLowerInvariant();
        var path = NormalizeSourcePath(rule.SourcePath).Trim('/');
        if (host.Any(static value => char.IsControl(value) || char.IsWhiteSpace(value) || value == ':') ||
            path.Length == 0 || path.Any(static value => char.IsControl(value) || char.IsWhiteSpace(value) || value == ':'))
            throw new InvalidOperationException($"Apache indexed shortlink path is not map-safe: {rule.Id ?? rule.SourcePath}");
        return $"{ResolveStatus(rule.Status, defaultStatus: 302)}:{host}:{path}";
    }

    private static bool ApacheRulesMayOverlap(LinkRedirectRule preceding, LinkRedirectRule shortlink)
    {
        if (!ApacheHostsMayOverlap(preceding.SourceHost, shortlink.SourceHost))
            return false;

        var shortlinkPath = NormalizeSourcePath(shortlink.SourcePath).Trim('/');
        if (preceding.MatchType == LinkRedirectMatchType.Regex)
            return true;

        var precedingPath = NormalizeSourcePath(preceding.SourcePath).Trim('/');
        if (preceding.MatchType == LinkRedirectMatchType.Prefix)
        {
            var starIndex = precedingPath.IndexOf('*');
            if (starIndex >= 0)
                precedingPath = precedingPath.Substring(0, starIndex).TrimEnd('/');
            return precedingPath.Length == 0 ||
                   shortlinkPath.Equals(precedingPath, StringComparison.OrdinalIgnoreCase) ||
                   shortlinkPath.StartsWith(precedingPath + "/", StringComparison.OrdinalIgnoreCase);
        }

        return shortlinkPath.Equals(precedingPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ApacheHostsMayOverlap(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || left.Trim() == "*" ||
            string.IsNullOrWhiteSpace(right) || right.Trim() == "*")
            return true;

        static string NormalizeHost(string value) => value.Trim().StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? value.Trim().Substring(4)
            : value.Trim();
        return NormalizeHost(left).Equals(NormalizeHost(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string? WriteApacheShortlinkMap(
        IReadOnlyList<ApacheShortlinkMapEntry> entries,
        WebLinkApacheExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ShortlinkMapOutputPath))
            return null;

        var outputPath = Path.GetFullPath(options.ShortlinkMapOutputPath!);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(outputPath, append: false, Utf8NoBom);
        foreach (var entry in entries)
            writer.WriteLine($"{entry.Key} {entry.Destination}");
        return outputPath;
    }

    private static void AppendApacheShortlinkMapRules(
        List<string> lines,
        IReadOnlyList<ApacheShortlinkMapEntry> entries,
        WebLinkApacheExportOptions options)
    {
        var runtimePath = options.ShortlinkMapRuntimePath!.Trim();
        lines.Add($"RewriteMap {ApacheShortlinkMapName} \"dbm:{runtimePath}\"");
        lines.Add(string.Empty);

        foreach (var group in entries
                     .GroupBy(static entry => new { entry.Host, entry.Status })
                     .OrderBy(static group => group.Key.Host, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static group => group.Key.Status))
        {
            AppendHostCondition(lines, group.Key.Host);
            WebApacheRewriteSafety.AppendOperationalPathCondition(lines);
            lines.Add($"RewriteCond ${{{ApacheShortlinkMapName}:{group.Key.Status}:{group.Key.Host.ToLowerInvariant()}:$1|NOT_FOUND}} !^NOT_FOUND$");
            lines.Add($"RewriteRule ^/?(.+?)/?$ ${{{ApacheShortlinkMapName}:{group.Key.Status}:{group.Key.Host.ToLowerInvariant()}:$1}} [R={group.Key.Status},L,NE,QSD]");
            lines.Add(string.Empty);
        }
    }

    private sealed record ApacheShortlinkMapEntry(
        string Key,
        string Destination,
        string Host,
        int Status,
        LinkRedirectRule Rule);
}
