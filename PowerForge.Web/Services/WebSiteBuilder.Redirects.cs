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

