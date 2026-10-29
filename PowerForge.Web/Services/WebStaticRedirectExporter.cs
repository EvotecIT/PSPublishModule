using System.Text.Json;
using HtmlTinkerX;

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
        var spellings = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
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
                    throw new InvalidOperationException($"Static redirect destination is another generated redirect: {path}. Select its canonical destination instead.");
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
                    throw new InvalidOperationException($"Static redirect would overwrite existing content: {rule.From}. Remove the route collision, or rebuild clean when changing a generated redirect.");
            }
            plans[output] = (html, destination);
        }

        foreach (var plan in plans)
        {
            if (plans.ContainsKey(plan.Value.Destination))
                throw new InvalidOperationException("Static redirect chains and loops are not supported.");
            for (var parent = Path.GetDirectoryName(plan.Key);
                 parent is not null && parent != Path.TrimEndingDirectorySeparator(root);
                 parent = Path.GetDirectoryName(parent))
            {
                if (plans.ContainsKey(parent) || File.Exists(parent))
                    throw new InvalidOperationException($"Static redirect output requires a parent directory occupied by a file: {Path.GetRelativePath(root, parent)}.");
            }
        }
        // An incremental build may retain pages from removed rules. Refuse to publish stale
        // navigation rather than deleting content or guessing ownership from a marker alone.
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(root, "*", enumeration))
        {
            if (Path.GetFileName(file).StartsWith(".redirect-", StringComparison.Ordinal) && file.EndsWith(".tmp", StringComparison.Ordinal))
                throw new InvalidOperationException($"Interrupted static redirect write remains at {Path.GetRelativePath(root, file)}. Rebuild clean before publishing.");
            if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && !plans.ContainsKey(file) && IsGeneratedRedirect(file, staticOnly: true))
                throw new InvalidOperationException($"Stale static redirect remains at {Path.GetRelativePath(root, file)}. Rebuild clean for removed rules, or remove reserved redirect markup from an authored page.");
        }
        if (!dryRun)
        {
            foreach (var plan in plans)
            {
                if (File.Exists(plan.Key)) continue; // Preflight already proved the existing bytes identical.
                Directory.CreateDirectory(Path.GetDirectoryName(plan.Key)!);
                var temporaryPath = Path.Combine(Path.GetDirectoryName(plan.Key)!, ".redirect-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.WriteAllText(temporaryPath, plan.Value.Html);
                    // Publish only complete pages, and never replace a file created after preflight.
                    File.Move(temporaryPath, plan.Key);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
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
        var possibleMarker = prefix.Contains("static-redirect", StringComparison.Ordinal) ||
            (!staticOnly && prefix.Contains("api-docs-legacy-alias", StringComparison.Ordinal));
        if (!possibleMarker) return false;
        // Most pages need only the prefix read. Parse the complete reserved-marker candidate
        // so a long, valid destination URL cannot truncate its refresh or canonical tags.
        return WebRedirectPage.IsGenerated(HtmlParser.ParseWithAngleSharp(prefix + reader.ReadToEnd()), staticOnly);
    }

    private static string ResolvePage(string root, string route, Dictionary<string, Dictionary<string, string>> spellings)
    {
        // Restrict source routes to portable path segments, rather than decoding ambiguous filesystem paths.
        if (string.IsNullOrWhiteSpace(route) || !route.StartsWith('/') || route.StartsWith("//", StringComparison.Ordinal) ||
            route.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')) ||
            route.Split('/').Any(segment => segment is "." or ".." || segment.EndsWith('.') ||
                VisualStoryPortablePathValidator.IsReservedWindowsDeviceName(segment)) ||
            route.Contains("//", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported static page route: {route}");
        var relative = route.Trim('/');
        if (route.EndsWith('/') || !Path.HasExtension(relative))
            relative += "/index.html";
        else if (!relative.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Static redirects require HTML page routes: {route}");
        var path = Path.GetFullPath(Path.Combine(root, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        FileSystemPathSafety.RejectReparsePoints(path, root, "Static redirect page");
        // Parent directories may have different case semantics, including Windows directory
        // flags and Linux mount points. Keep spelling history separate for each exact parent.
        {
            var current = root;
            foreach (var segment in relative.TrimStart('/').Split('/'))
            {
                var child = Path.Combine(current, segment);
                if (!HasCaseSensitiveNames(current))
                {
                    if (!spellings.TryGetValue(current, out var namesSeen))
                        spellings[current] = namesSeen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (namesSeen.TryGetValue(segment, out var prior) && prior != segment)
                        throw new InvalidOperationException("Case-distinct static redirect paths cannot be represented safely in this output directory.");
                    if (Directory.Exists(current))
                    {
                        var names = Directory.EnumerateFileSystemEntries(current).Select(Path.GetFileName).ToArray();
                        if (!names.Contains(segment, StringComparer.Ordinal) && names.Contains(segment, StringComparer.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Static redirect path spelling differs from generated content: {route}");
                    }
                    namesSeen[segment] = segment;
                }
                current = child;
            }
        }
        return path;
    }

    private static bool HasCaseSensitiveNames(string directory)
    {
        var existing = directory;
        while (!Directory.Exists(existing)) existing = Path.GetDirectoryName(existing)!;
        if (WindowsDirectoryCaseSensitivity.TryGet(existing, out var sensitive)) return sensitive;
        // Empty directories supply no read-only evidence. Refuse ambiguous aliases there.
        if (!Directory.EnumerateFileSystemEntries(existing).Any(path => Path.GetFileName(path).Any(character =>
                char.ToUpperInvariant(character) != char.ToLowerInvariant(character)))) return false;
        return FrameworkCompatibility.GetPathStringComparison(existing) == StringComparison.Ordinal;
    }
}
