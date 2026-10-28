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
        // URL paths are case-sensitive, including when a build runs on a different host OS.
        var plans = new Dictionary<string, (string Html, string Destination)>(StringComparer.Ordinal);
        var spellings = FrameworkCompatibility.GetPathStringComparison(root) == StringComparison.Ordinal
            ? null : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in redirects)
        {
            if (rule.MatchType != RedirectMatchType.Exact || rule.Status is not (301 or 302 or 307 or 308))
                throw new InvalidOperationException($"GitHub Pages requires exact page redirects: {rule.From}");
            var output = ResolvePage(root, rule.From, spellings);
            var target = rule.To;
            string destination;
            if (target.StartsWith('/') && !target.StartsWith("//", StringComparison.Ordinal))
            {
                var path = target.Split('?', '#')[0];
                destination = ResolvePage(root, path, spellings);
                if (!File.Exists(destination))
                    throw new InvalidOperationException($"Static redirect canonical page was not generated: {path}");
                FileSystemPathSafety.RequireRegularFile(destination);
                if (IsGeneratedRedirect(destination))
                    throw new InvalidOperationException($"Static redirect destination is another generated redirect: {path}. Rebuild clean before exporting changed rules.");
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
            if (File.Exists(output))
            {
                FileSystemPathSafety.RequireRegularFile(output);
                if (File.ReadAllText(output) != html)
                    throw new InvalidOperationException($"Static redirect would overwrite existing content: {rule.From}. Rebuild clean before exporting changed rules.");
            }
            plans[output] = (html, destination);
        }

        foreach (var plan in plans)
        {
            if (plans.ContainsKey(plan.Value.Destination))
                throw new InvalidOperationException("Static redirect chains and loops are not supported.");
        }
        // An incremental build may retain pages from removed rules. Refuse to publish stale
        // navigation rather than deleting content or guessing ownership from a marker alone.
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(root, "*", enumeration).Where(path => path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
        {
            if (!plans.ContainsKey(file) && IsGeneratedRedirect(file, staticOnly: true))
                throw new InvalidOperationException($"Stale static redirect remains at {Path.GetRelativePath(root, file)}. Rebuild clean before exporting changed rules.");
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

    private static bool IsGeneratedRedirect(string file, bool staticOnly = false)
    {
        FileSystemPathSafety.RequireRegularFile(file);
        using var reader = File.OpenText(file);
        var buffer = new char[4096];
        var prefix = new string(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length));
        return prefix.Contains("data-pf=\"static-redirect\"", StringComparison.Ordinal) ||
            (!staticOnly && prefix.Contains("data-pf=\"api-docs-legacy-alias\"", StringComparison.Ordinal) &&
             prefix.Contains("http-equiv=\"refresh\"", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePage(string root, string route, Dictionary<string, string>? spellings)
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
        if (spellings is not null)
        {
            // Protect every directory component, not just the final filename: /API/a and /api/b
            // otherwise merge on a case-insensitive build host and publish the wrong URL spelling.
            var current = root;
            foreach (var segment in relative.TrimStart('/').Split('/'))
            {
                var child = Path.Combine(current, segment);
                if (spellings.TryGetValue(child, out var prior) && !prior.Equals(child, StringComparison.Ordinal))
                    throw new InvalidOperationException("Case-distinct static redirect paths require a case-sensitive output directory.");
                if (Directory.Exists(current))
                {
                    var names = Directory.EnumerateFileSystemEntries(current).Select(Path.GetFileName).ToArray();
                    if (!names.Contains(segment, StringComparer.Ordinal) && names.Contains(segment, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Static redirect path spelling differs from generated content: {route}");
                }
                spellings[child] = child;
                current = child;
            }
        }
        return path;
    }
}
