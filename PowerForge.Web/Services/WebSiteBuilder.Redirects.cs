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
    private static void AddLegacyAmpRedirects(
        SiteSpec spec,
        List<RedirectSpec> redirects,
        IReadOnlyList<ContentItem> items)
    {
        if (spec is null || redirects is null || items is null)
            return;
        if (!spec.EnableLegacyAmpRedirects || items.Count == 0)
            return;

        var existingSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var redirect in redirects)
        {
            if (redirect is null || string.IsNullOrWhiteSpace(redirect.From))
                continue;
            existingSources.Add(NormalizeRouteForMatch(redirect.From));
        }

        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.OutputPath))
                continue;

            var target = NormalizeRouteForMatch(item.OutputPath);
            if (string.IsNullOrWhiteSpace(target))
                continue;

            foreach (var source in BuildLegacyAmpSources(target))
                TryAddAliasRedirect(source, target, existingSources, redirects);
        }
    }

    private static string[] BuildLegacyAmpSources(string canonicalRoute)
    {
        var normalized = NormalizeRouteForMatch(canonicalRoute);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();

        var trimmed = normalized.TrimEnd('/');
        var isRoot = string.IsNullOrWhiteSpace(trimmed);
        if (!isRoot && trimmed.EndsWith("/amp", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (isRoot)
        {
            values.Add("/amp");
            values.Add("/amp/");
        }
        else
        {
            values.Add(trimmed + "/amp");
            values.Add(trimmed + "/amp/");
        }

        return values
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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
        WriteApacheRedirects(Path.Combine(outputRoot, ".htaccess"), redirects);
        WriteNginxRedirects(Path.Combine(outputRoot, "nginx.redirects.conf"), redirects);
        WriteIisWebConfigRedirects(Path.Combine(outputRoot, "web.config"), redirects);
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

    private static void WriteApacheRedirects(string path, IReadOnlyList<RedirectSpec> redirects)
    {
        var lines = new List<string>
        {
            "# Generated by PowerForge.Web",
            "<IfModule mod_rewrite.c>",
            "RewriteEngine On"
        };

        var ruleCount = 0;
        foreach (var redirect in redirects)
        {
            if (!TryBuildRedirectRule(redirect, "$1", out var pattern, out var destination))
                continue;

            var flags = new List<string>
            {
                $"R={ResolveRedirectStatus(redirect)}",
                "L",
                redirect.PreserveQuery ? "QSA" : "QSD"
            };
            lines.Add($"RewriteRule {pattern} {destination} [{string.Join(",", flags)}]");
            ruleCount++;
        }

        lines.Add("</IfModule>");

        if (ruleCount > 0)
            WriteAllLinesIfChanged(path, lines);
    }

    private static void WriteIisWebConfigRedirects(string path, IReadOnlyList<RedirectSpec> redirects)
    {
        var rules = new XElement("rules", new XElement("clear"));
        var ruleCount = 0;

        foreach (var redirect in redirects)
        {
            if (!TryBuildRedirectRule(redirect, "{R:1}", out var pattern, out var destination))
                continue;

            var status = ResolveRedirectStatus(redirect);
            rules.Add(
                new XElement("rule",
                    new XAttribute("name", $"PFWEB Redirect {++ruleCount}"),
                    new XAttribute("stopProcessing", "true"),
                    new XElement("match",
                        new XAttribute("url", pattern)),
                    new XElement("action",
                        new XAttribute("type", "Redirect"),
                        new XAttribute("url", destination),
                        new XAttribute("appendQueryString", redirect.PreserveQuery ? "true" : "false"),
                        new XAttribute("redirectType", ResolveIisRedirectType(status)))));
        }

        if (ruleCount == 0)
            return;

        var document = new XDocument(
            new XElement("configuration",
                new XElement("system.webServer",
                    new XElement("rewrite", rules))));
        WriteAllTextIfChanged(path, document.ToString(SaveOptions.DisableFormatting));
    }

    private static void WriteNginxRedirects(string path, IReadOnlyList<RedirectSpec> redirects)
    {
        var lines = new List<string>
        {
            "# Generated by PowerForge.Web",
            "# Include this file inside your nginx server block."
        };

        var ruleCount = 0;
        foreach (var redirect in redirects)
        {
            if (!TryBuildNginxRedirectRule(redirect, out var locationMatcher, out var destination, out var exact))
                continue;

            var querySuffix = redirect.PreserveQuery ? "$is_args$args" : string.Empty;
            var status = ResolveRedirectStatus(redirect);
            lines.Add(exact
                ? $"location = {locationMatcher} {{ return {status} {destination}{querySuffix}; }}"
                : $"location ~ {locationMatcher} {{ return {status} {destination}{querySuffix}; }}");
            ruleCount++;
        }

        if (ruleCount > 0)
            WriteAllLinesIfChanged(path, lines);
    }

    private static bool TryBuildRedirectRule(RedirectSpec redirect, string wildcardReplacement, out string pattern, out string destination)
    {
        pattern = string.Empty;
        destination = string.Empty;
        if (redirect is null || string.IsNullOrWhiteSpace(redirect.From) || string.IsNullOrWhiteSpace(redirect.To))
            return false;

        pattern = BuildRedirectPattern(redirect.From, redirect.MatchType);
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var replacement = redirect.MatchType is RedirectMatchType.Prefix or RedirectMatchType.Wildcard or RedirectMatchType.Regex
            ? wildcardReplacement
            : string.Empty;
        destination = ReplacePathPlaceholder(redirect.To, replacement);
        destination = NormalizeRedirectDestination(destination);
        return !string.IsNullOrWhiteSpace(destination);
    }

    private static bool TryBuildNginxRedirectRule(RedirectSpec redirect, out string locationMatcher, out string destination, out bool exact)
    {
        locationMatcher = string.Empty;
        destination = string.Empty;
        exact = false;

        if (redirect is null || string.IsNullOrWhiteSpace(redirect.From) || string.IsNullOrWhiteSpace(redirect.To))
            return false;

        switch (redirect.MatchType)
        {
            case RedirectMatchType.Exact:
            {
                var route = NormalizeRouteForMatch(redirect.From);
                locationMatcher = string.IsNullOrWhiteSpace(route) ? "/" : route;
                destination = ReplacePathPlaceholder(redirect.To, string.Empty);
                exact = true;
                break;
            }
            case RedirectMatchType.Prefix:
            case RedirectMatchType.Wildcard:
            {
                locationMatcher = BuildNginxWildcardPattern(redirect.From);
                destination = ReplacePathPlaceholder(redirect.To, "$1");
                break;
            }
            case RedirectMatchType.Regex:
            {
                locationMatcher = NormalizeNginxRegexPattern(redirect.From);
                destination = ReplacePathPlaceholder(redirect.To, "$1");
                break;
            }
            default:
                return false;
        }

        destination = NormalizeRedirectDestination(destination);
        if (string.IsNullOrWhiteSpace(destination))
            return false;
        return !string.IsNullOrWhiteSpace(locationMatcher);
    }

    private static string BuildNginxWildcardPattern(string source)
    {
        var normalized = NormalizeRouteForMatch(source);
        if (string.IsNullOrWhiteSpace(normalized))
            return "^/(.*)$";

        var prefix = normalized;
        var starIndex = prefix.IndexOf('*');
        if (starIndex >= 0)
            prefix = prefix.Substring(0, starIndex);
        prefix = prefix.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(prefix) || prefix == "/")
            return "^/(.*)$";

        return $"^{Regex.Escape(prefix)}(?:/(.*))?$";
    }

    private static string NormalizeNginxRegexPattern(string source)
    {
        var regex = source?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(regex))
            return string.Empty;

        if (regex.StartsWith("^", StringComparison.Ordinal))
            return regex;
        if (regex.StartsWith("/", StringComparison.Ordinal))
            return "^" + regex;

        return "^/" + regex.TrimStart('/');
    }

    private static string BuildRedirectPattern(string source, RedirectMatchType matchType)
    {
        var normalized = NormalizeRouteForMatch(source);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        switch (matchType)
        {
            case RedirectMatchType.Prefix:
            case RedirectMatchType.Wildcard:
            {
                var prefix = normalized;
                var starIndex = prefix.IndexOf('*');
                if (starIndex >= 0)
                    prefix = prefix.Substring(0, starIndex);
                prefix = prefix.Trim('/').Trim();
                if (string.IsNullOrWhiteSpace(prefix))
                    return "^(.*)$";

                return $"^{Regex.Escape(prefix)}(?:/(.*))?$";
            }
            case RedirectMatchType.Regex:
            {
                var regex = source.Trim();
                if (regex.StartsWith("/", StringComparison.Ordinal))
                    regex = regex.TrimStart('/');
                if (!regex.StartsWith("^", StringComparison.Ordinal))
                    regex = "^" + regex;
                return regex;
            }
            case RedirectMatchType.Exact:
            default:
            {
                var exact = normalized.Trim('/');
                if (string.IsNullOrWhiteSpace(exact))
                    return "^$";
                return $"^{Regex.Escape(exact)}/?$";
            }
        }
    }

    private static string NormalizeRedirectDestination(string destination)
    {
        var trimmed = destination?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;
        if (IsExternalUrl(trimmed))
            return trimmed;
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed.TrimStart('/');
    }

    private static int ResolveRedirectStatus(RedirectSpec redirect)
    {
        if (redirect is null || redirect.Status <= 0)
            return 301;
        return redirect.Status;
    }

    private static string ResolveIisRedirectType(int status)
    {
        return status switch
        {
            301 or 308 => "Permanent",
            303 => "SeeOther",
            302 => "Found",
            _ => "Temporary"
        };
    }

}
