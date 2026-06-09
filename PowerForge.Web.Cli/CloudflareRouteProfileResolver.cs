using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal sealed class CloudflareSiteRouteProfile
{
    public string SiteConfigPath { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string[] VerifyPaths { get; init; } = Array.Empty<string>();
    public string[] PurgePaths { get; init; } = Array.Empty<string>();
}

internal static class CloudflareRouteProfileResolver
{
    private const int MaxMenuDepth = 32;

    private static readonly string[] PurgeOnlyPaths =
    {
        "/404.html",
        "/llms.txt",
        "/llms-full.txt",
        "/llms.json"
    };

    internal static CloudflareSiteRouteProfile Load(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Config path is required.", nameof(configPath));

        var resolvedConfig = Path.GetFullPath(configPath);
        if (!File.Exists(resolvedConfig))
            throw new FileNotFoundException($"site config file was not found: {resolvedConfig}", resolvedConfig);

        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(resolvedConfig, WebCliJson.Options);
        var baseUrl = (spec.BaseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("site config is missing BaseUrl.");

        var verifyPaths = BuildVerifyPaths(spec);
        var purgePaths = BuildPurgePaths(verifyPaths);

        return new CloudflareSiteRouteProfile
        {
            SiteConfigPath = specPath,
            BaseUrl = baseUrl,
            VerifyPaths = verifyPaths,
            PurgePaths = purgePaths
        };
    }

    private static string[] BuildVerifyPaths(SiteSpec spec)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPath("/", ordered, seen);

        var features = (spec.Features ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (features.Contains("docs"))
            AddPath("/docs/", ordered, seen);
        if (features.Contains("apiDocs"))
            AddPath("/api/", ordered, seen);
        if (features.Contains("blog"))
            AddPath("/blog/", ordered, seen);
        if (features.Contains("search"))
            AddPath("/search/", ordered, seen);

        foreach (var surface in spec.Navigation?.Surfaces ?? Array.Empty<NavigationSurfaceSpec>())
            AddFromUrl(surface.Path, ordered, seen);

        foreach (var menu in spec.Navigation?.Menus ?? Array.Empty<MenuSpec>())
            AddMenuItems(menu.Items, ordered, seen);

        AddMenuItems(spec.Navigation?.Actions ?? Array.Empty<MenuItemSpec>(), ordered, seen);
        AddPath("/sitemap.xml", ordered, seen);

        // `/404/` is valid for purge warmups but should not be part of cache-hit verification.
        ordered.RemoveAll(path => path.Equals("/404/", StringComparison.OrdinalIgnoreCase));
        return ordered.ToArray();
    }

    private static string[] BuildPurgePaths(IEnumerable<string> verifyPaths)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in verifyPaths ?? Array.Empty<string>())
            AddPath(path, ordered, seen);

        foreach (var path in PurgeOnlyPaths)
            AddPath(path, ordered, seen);

        return ordered.ToArray();
    }

    private static void AddMenuItems(IEnumerable<MenuItemSpec> items, List<string> ordered, HashSet<string> seen, int depth = 0)
    {
        if (depth > MaxMenuDepth)
            return;

        foreach (var item in items ?? Array.Empty<MenuItemSpec>())
        {
            AddFromUrl(item.Url, ordered, seen);
            AddMenuItems(item.Items, ordered, seen, depth + 1);

            foreach (var section in item.Sections ?? Array.Empty<MenuSectionSpec>())
            {
                AddMenuItems(section.Items, ordered, seen, depth + 1);
                foreach (var column in section.Columns ?? Array.Empty<MenuColumnSpec>())
                    AddMenuItems(column.Items, ordered, seen, depth + 1);
            }
        }
    }

    private static void AddFromUrl(string? urlOrPath, List<string> ordered, HashSet<string> seen)
    {
        if (TryNormalizeInternalPath(urlOrPath, out var normalized))
            AddPath(normalized, ordered, seen);
    }

    private static void AddPath(string? path, List<string> ordered, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var normalized = path.Trim();
        if (seen.Add(normalized))
            ordered.Add(normalized);
    }

    private static bool TryNormalizeInternalPath(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var value = raw.Trim();
        if (value.StartsWith("#", StringComparison.Ordinal) ||
            value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("//", StringComparison.Ordinal))
            return false;

        if (Uri.TryCreate(value, UriKind.Absolute, out _))
            return false;

        var delimiterIndex = value.IndexOfAny(new[] { '?', '#' });
        if (delimiterIndex >= 0)
            value = value[..delimiterIndex];

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!value.StartsWith("/", StringComparison.Ordinal))
            value = "/" + value;

        value = value.Replace('\\', '/');
        while (value.Contains("//", StringComparison.Ordinal))
            value = value.Replace("//", "/", StringComparison.Ordinal);

        if (value.Contains("..", StringComparison.Ordinal))
            return false;

        if (value.Contains('*', StringComparison.Ordinal) ||
            value.Contains('{', StringComparison.Ordinal) ||
            value.Contains('}', StringComparison.Ordinal))
            return false;

        if (value.Equals("/", StringComparison.Ordinal))
        {
            normalized = value;
            return true;
        }

        if (value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            normalized = value;
            return true;
        }

        if (!value.EndsWith("/", StringComparison.Ordinal))
            value += "/";

        normalized = value;
        return true;
    }
}
