using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Web;

public static class WebSiteVerifier
{
    public static WebVerifyResult Verify(SiteSpec spec, WebSitePlan plan)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var errors = new List<string>();
        var warnings = new List<string>();

        if (spec.Collections is null || spec.Collections.Length == 0)
        {
            warnings.Add("No collections defined.");
            return new WebVerifyResult { Success = true, Warnings = warnings.ToArray(), Errors = Array.Empty<string>() };
        }

        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var collection in spec.Collections)
        {
            if (collection is null) continue;
            var files = EnumerateCollectionFiles(plan.RootPath, collection.Input).ToArray();
            if (files.Length == 0)
            {
                warnings.Add($"Collection '{collection.Name}' has no files.");
                continue;
            }

            foreach (var file in files)
            {
                var markdown = File.ReadAllText(file);
                var (matter, body) = FrontMatterParser.Parse(markdown);
                var title = matter?.Title ?? FrontMatterParser.ExtractTitleFromMarkdown(body) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title))
                {
                    errors.Add($"Missing title in: {file}");
                }

                var slug = matter?.Slug ?? Slugify(Path.GetFileNameWithoutExtension(file));
                if (string.IsNullOrWhiteSpace(slug))
                {
                    errors.Add($"Missing slug in: {file}");
                    continue;
                }

                var route = BuildRoute(collection.Output, slug, spec.TrailingSlash);
                if (routes.TryGetValue(route, out var existing))
                {
                    errors.Add($"Duplicate route '{route}' from '{file}' and '{existing}'.");
                }
                else
                {
                    routes[route] = file;
                }
            }
        }

        return new WebVerifyResult
        {
            Success = errors.Count == 0,
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    private static string BuildRoute(string baseOutput, string slug, TrailingSlashMode slashMode)
    {
        var basePath = NormalizePath(baseOutput);
        var slugPath = NormalizePath(slug);
        string combined;

        if (string.IsNullOrWhiteSpace(slugPath) || slugPath == "index")
        {
            combined = basePath;
        }
        else if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
        {
            combined = slugPath;
        }
        else
        {
            combined = $"{basePath}/{slugPath}";
        }

        combined = "/" + combined.Trim('/');
        return EnsureTrailingSlash(combined, slashMode);
    }

    private static string EnsureTrailingSlash(string path, TrailingSlashMode mode)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        if (mode == TrailingSlashMode.Never)
            return path.EndsWith("/") && path.Length > 1 ? path.TrimEnd('/') : path;

        if (mode == TrailingSlashMode.Always && !path.EndsWith("/"))
            return path + "/";

        return path;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var trimmed = path.Trim();
        if (trimmed == "/") return "/";
        return trimmed.Trim('/');
    }

    private static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var lower = input.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') sb.Append('-');
        }
        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    private static IEnumerable<string> EnumerateCollectionFiles(string rootPath, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();

        var full = Path.IsPathRooted(input) ? input : Path.Combine(rootPath, input);
        if (full.Contains('*'))
            return EnumerateCollectionFilesWithWildcard(full);

        if (!Directory.Exists(full))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(full, "*.md", SearchOption.AllDirectories);
    }

    private static IEnumerable<string> EnumerateCollectionFilesWithWildcard(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = normalized.Split('*');
        if (parts.Length != 2)
            return Array.Empty<string>();

        var basePath = parts[0].TrimEnd(Path.DirectorySeparatorChar);
        var tail = parts[1].TrimStart(Path.DirectorySeparatorChar);
        if (!Directory.Exists(basePath))
            return Array.Empty<string>();

        var results = new List<string>();
        foreach (var dir in Directory.GetDirectories(basePath))
        {
            var candidate = string.IsNullOrEmpty(tail) ? dir : Path.Combine(dir, tail);
            if (!Directory.Exists(candidate))
                continue;
            results.AddRange(Directory.EnumerateFiles(candidate, "*.md", SearchOption.AllDirectories));
        }

        return results;
    }
}
