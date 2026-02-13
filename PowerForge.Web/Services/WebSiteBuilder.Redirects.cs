using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PowerForge.Web;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static void AddVersioningAliasRedirects(SiteSpec spec, List<RedirectSpec> redirects)
    {
        if (spec is null || redirects is null)
            return;

        var versioning = spec.Versioning;
        if (versioning is null || !versioning.Enabled || !versioning.GenerateAliasRedirects)
            return;

        var versions = (versioning.Versions ?? Array.Empty<VersionSpec>())
            .Where(static version => version is not null && !string.IsNullOrWhiteSpace(version.Name))
            .GroupBy(version => version.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (versions.Length == 0)
            return;

        var existingSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var redirect in redirects)
        {
            if (redirect is null || string.IsNullOrWhiteSpace(redirect.From))
                continue;
            existingSources.Add(NormalizeRouteForMatch(redirect.From));
        }

        var latest = versions.FirstOrDefault(static version => version.Latest);
        if (latest is null)
            latest = versions.FirstOrDefault(static version => version.Default) ?? versions[0];
        var lts = versions.FirstOrDefault(static version => version.Lts);

        var latestAliasInput = string.IsNullOrWhiteSpace(versioning.LatestAliasPath)
            ? "latest"
            : versioning.LatestAliasPath;
        var latestAliasSource = ResolveVersionAliasSource(versioning.BasePath, latestAliasInput);
        TryAddAliasRedirect(latestAliasSource, ResolveVersionUrl(versioning.BasePath, latest), existingSources, redirects);

        if (lts is not null)
        {
            var ltsAliasInput = string.IsNullOrWhiteSpace(versioning.LtsAliasPath)
                ? "lts"
                : versioning.LtsAliasPath;
            var ltsAliasSource = ResolveVersionAliasSource(versioning.BasePath, ltsAliasInput);
            TryAddAliasRedirect(ltsAliasSource, ResolveVersionUrl(versioning.BasePath, lts), existingSources, redirects);
        }

        foreach (var version in versions)
        {
            if (version.Aliases is null || version.Aliases.Length == 0)
                continue;

            var target = ResolveVersionUrl(versioning.BasePath, version);
            foreach (var alias in version.Aliases)
            {
                var source = ResolveVersionAliasSource(versioning.BasePath, alias);
                TryAddAliasRedirect(source, target, existingSources, redirects);
            }
        }
    }

    private static string ResolveVersionAliasSource(string? basePath, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (IsExternalUrl(trimmed))
            return string.Empty;

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
            return NormalizeRouteForMatch(trimmed);

        var normalizedBasePath = NormalizeVersionBasePath(basePath);
        if (string.IsNullOrWhiteSpace(normalizedBasePath) || normalizedBasePath == "/")
            return NormalizeRouteForMatch("/" + trimmed.Trim('/') + "/");

        return NormalizeRouteForMatch($"{normalizedBasePath}/{trimmed.Trim('/')}/");
    }

    private static void TryAddAliasRedirect(
        string? source,
        string? target,
        HashSet<string> existingSources,
        List<RedirectSpec> redirects)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            return;

        var normalizedSource = NormalizeRouteForMatch(source);
        var normalizedTarget = NormalizeRouteForMatch(target);
        if (string.Equals(normalizedSource, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            return;

        if (!existingSources.Add(normalizedSource))
            return;

        redirects.Add(new RedirectSpec
        {
            From = normalizedSource,
            To = normalizedTarget,
            Status = 301,
            MatchType = RedirectMatchType.Exact,
            PreserveQuery = true
        });
    }

    private static void WriteRedirectOutputs(string outputRoot, IReadOnlyList<RedirectSpec> redirects)
    {
        if (redirects.Count == 0) return;

        WriteNetlifyRedirects(Path.Combine(outputRoot, "_redirects"), redirects);
        WriteAzureStaticWebAppConfig(Path.Combine(outputRoot, "staticwebapp.config.json"), redirects);
        WriteVercelRedirects(Path.Combine(outputRoot, "vercel.json"), redirects);
    }

    private static void WriteNetlifyRedirects(string path, IReadOnlyList<RedirectSpec> redirects)
    {
        var lines = new List<string>();
        foreach (var r in redirects)
        {
            if (r.MatchType == RedirectMatchType.Regex)
                continue;

            var from = NormalizeNetlifySource(r);
            var to = ReplacePathPlaceholder(r.To, ":splat");
            var status = r.Status <= 0 ? 301 : r.Status;
            lines.Add($"{from} {to} {status}");
        }

        if (lines.Count > 0)
            WriteAllLinesIfChanged(path, lines);
    }

    private static void WriteAzureStaticWebAppConfig(string path, IReadOnlyList<RedirectSpec> redirects)
    {
        var routes = new List<object>();
        foreach (var r in redirects)
        {
            if (r.MatchType == RedirectMatchType.Regex)
                continue;

            var route = NormalizeAzureRoute(r);
            routes.Add(new { route, redirect = r.To, statusCode = r.Status <= 0 ? 301 : r.Status });
        }

        var payload = new { routes };
        WriteAllTextIfChanged(path, JsonSerializer.Serialize(payload, WebJson.Options));
    }

    private static void WriteVercelRedirects(string path, IReadOnlyList<RedirectSpec> redirects)
    {
        var items = new List<object>();
        foreach (var r in redirects)
        {
            var source = NormalizeVercelSource(r);
            var destination = ReplacePathPlaceholder(r.To, ":path*");
            var permanent = r.Status == 301 || r.Status == 308 || r.Status == 0;
            items.Add(new { source, destination, permanent });
        }

        var payload = new { redirects = items };
        WriteAllTextIfChanged(path, JsonSerializer.Serialize(payload, WebJson.Options));
    }

    private static string NormalizeNetlifySource(RedirectSpec r)
    {
        var from = r.From;
        if (r.MatchType == RedirectMatchType.Prefix || r.MatchType == RedirectMatchType.Wildcard)
        {
            if (!from.Contains("*"))
                from = from.TrimEnd('/') + "/*";
        }
        return from;
    }

    private static string NormalizeAzureRoute(RedirectSpec r)
    {
        var route = r.From;
        if (r.MatchType == RedirectMatchType.Prefix || r.MatchType == RedirectMatchType.Wildcard)
        {
            if (!route.EndsWith("*", StringComparison.Ordinal))
                route = route.TrimEnd('/') + "/*";
        }
        return route;
    }

    private static string NormalizeVercelSource(RedirectSpec r)
    {
        var source = r.From;
        if (r.MatchType == RedirectMatchType.Prefix || r.MatchType == RedirectMatchType.Wildcard)
        {
            if (source.Contains("*"))
                source = source.Replace("*", ":path*");
            else
                source = source.TrimEnd('/') + "/:path*";
        }
        return source;
    }

    private static string ReplacePathPlaceholder(string path, string replacement)
    {
        return path.Replace("{path}", replacement, StringComparison.OrdinalIgnoreCase);
    }

}
