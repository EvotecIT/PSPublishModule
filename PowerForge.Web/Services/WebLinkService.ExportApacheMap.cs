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

        return rules
            .Where(rule => IsApacheShortlinkMapRule(rule, options))
            .Select(rule => CreateApacheShortlinkMapEntry(rule, options))
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
            ResolveStatus(rule.Status, defaultStatus: 302));
    }

    private static bool IsApacheShortlinkMapRule(LinkRedirectRule rule, WebLinkApacheExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ShortlinkMapOutputPath) ||
            string.IsNullOrWhiteSpace(options.ShortlinkMapRuntimePath))
            return false;

        return string.Equals(rule.Source, "shortlink", StringComparison.Ordinal) &&
               rule.MatchType == LinkRedirectMatchType.Exact &&
               !string.IsNullOrWhiteSpace(rule.SourceHost) &&
               !string.Equals(rule.SourceHost.Trim(), "*", StringComparison.Ordinal) &&
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

    private sealed record ApacheShortlinkMapEntry(string Key, string Destination, string Host, int Status);
}
