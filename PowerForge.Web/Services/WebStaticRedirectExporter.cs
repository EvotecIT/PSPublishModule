using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Materializes exact redirects after all site generators have written their canonical pages.</summary>
internal static class WebStaticRedirectExporter
{
    internal static int Export(string siteRoot, bool dryRun)
    {
        var root = FileSystemPathSafety.ResolveParentDirectoryAliases(siteRoot);
        var metadata = Path.Combine(root, "_powerforge", "redirects.json");
        FileSystemPathSafety.RejectReparsePoints(metadata, root, "Static redirect metadata");
        FileSystemPathSafety.RequireRegularFile(metadata);
        using var document = JsonDocument.Parse(File.ReadAllText(metadata));
        var redirects = document.RootElement.GetProperty("redirects").Deserialize<RedirectSpec[]>(WebJson.Options)
            ?? Array.Empty<RedirectSpec>();
        var plans = new Dictionary<string, (string Html, string Destination)>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in redirects)
        {
            if (rule.MatchType != RedirectMatchType.Exact || rule.Status is not (301 or 302 or 307 or 308))
                throw new InvalidOperationException($"GitHub Pages requires exact page redirects: {rule.From}");
            var output = ResolvePage(root, rule.From);
            var target = rule.To;
            string destination;
            if (target.StartsWith('/') && !target.StartsWith("//", StringComparison.Ordinal))
            {
                var path = target.Split('?', '#')[0];
                destination = ResolvePage(root, path);
                if (!File.Exists(destination))
                    throw new InvalidOperationException($"Static redirect canonical page was not generated: {path}");
                FileSystemPathSafety.RequireRegularFile(destination);
            }
            else if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
                     uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo))
            {
                destination = target;
            }
            else
                throw new InvalidOperationException($"Unsupported static redirect destination: {target}");

            var html = WebRedirectPage.Render(target, "static-redirect", preserveQueryAndFragment: true, rule.PreserveQuery);
            if (plans.TryGetValue(output, out var prior) && prior.Html != html)
                throw new InvalidOperationException($"Conflicting static redirects: {rule.From}");
            if (File.Exists(output) && File.ReadAllText(output) != html)
                throw new InvalidOperationException($"Static redirect would overwrite existing content: {rule.From}");
            plans[output] = (html, destination);
        }

        foreach (var plan in plans)
        {
            if (plans.ContainsKey(plan.Value.Destination))
                throw new InvalidOperationException("Static redirect chains and loops are not supported.");
        }
        if (!dryRun)
        {
            foreach (var plan in plans)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plan.Key)!);
                File.WriteAllText(plan.Key, plan.Value.Html);
            }
        }
        return plans.Count;
    }

    private static string ResolvePage(string root, string route)
    {
        // Restrict source routes to portable path segments, rather than decoding ambiguous filesystem paths.
        if (string.IsNullOrWhiteSpace(route) || !route.StartsWith('/') || route.StartsWith("//", StringComparison.Ordinal) ||
            route.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')) ||
            route.Split('/').Any(segment => segment is "." or ".." || segment.EndsWith('.')) ||
            route.Contains("//", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported static page route: {route}");
        var relative = route.Trim('/');
        if (!relative.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            if (Path.HasExtension(relative))
                throw new InvalidOperationException($"Static redirects require HTML page routes: {route}");
            relative += "/index.html";
        }
        var path = Path.GetFullPath(Path.Combine(root, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        FileSystemPathSafety.RejectReparsePoints(path, root, "Static redirect page");
        return path;
    }
}
