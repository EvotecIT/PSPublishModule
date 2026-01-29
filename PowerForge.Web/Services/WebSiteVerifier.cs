using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Web;

/// <summary>Verifies site content and routing integrity.</summary>
public static class WebSiteVerifier
{
    /// <summary>Validates the site spec against discovered content.</summary>
    /// <param name="spec">Site configuration.</param>
    /// <param name="plan">Resolved site plan.</param>
    /// <returns>Verification result.</returns>
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

            var leafBundleRoots = BuildLeafBundleRoots(files);
            foreach (var file in files)
            {
                if (IsUnderAnyRoot(file, leafBundleRoots) && !IsLeafBundleIndex(file))
                    continue;

                var markdown = File.ReadAllText(file);
                var (matter, body) = FrontMatterParser.Parse(markdown);
                var title = matter?.Title ?? FrontMatterParser.ExtractTitleFromMarkdown(body) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title))
                {
                    errors.Add($"Missing title in: {file}");
                }

                var collectionRoot = ResolveCollectionRootForFile(plan.RootPath, collection.Input, file);
                var relativePath = ResolveRelativePath(collectionRoot, file);
                var relativeDir = NormalizePath(Path.GetDirectoryName(relativePath) ?? string.Empty);
                var isSectionIndex = IsSectionIndex(file);
                var isBundleIndex = IsLeafBundleIndex(file);
                var slugPath = ResolveSlugPath(relativePath, relativeDir, matter?.Slug);
                if (isSectionIndex || isBundleIndex)
                    slugPath = ApplySlugOverride(relativeDir, matter?.Slug);
                if (string.IsNullOrWhiteSpace(slugPath))
                {
                    errors.Add($"Missing slug in: {file}");
                    continue;
                }

                var projectSlug = ResolveProjectSlug(plan, file);
                var baseOutput = ReplaceProjectPlaceholder(collection.Output, projectSlug);
                var route = BuildRoute(baseOutput, slugPath, spec.TrailingSlash);
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

    private static HashSet<string> BuildLeafBundleRoots(IReadOnlyList<string> markdownFiles)
    {
        if (markdownFiles.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var byDir = markdownFiles
            .GroupBy(f => Path.GetDirectoryName(f) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in byDir)
        {
            var dir = kvp.Key;
            var files = kvp.Value;
            var hasIndex = files.Any(IsLeafBundleIndex);
            if (!hasIndex) continue;
            if (files.Any(IsSectionIndex)) continue;

            var hasOtherMarkdown = files.Any(f =>
            {
                var name = Path.GetFileName(f);
                if (name.Equals("index.md", StringComparison.OrdinalIgnoreCase)) return false;
                if (name.Equals("_index.md", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            });

            if (!hasOtherMarkdown)
                roots.Add(dir);
        }

        return roots;
    }

    private static bool IsLeafBundleIndex(string filePath)
        => Path.GetFileName(filePath).Equals("index.md", StringComparison.OrdinalIgnoreCase);

    private static bool IsSectionIndex(string filePath)
        => Path.GetFileName(filePath).Equals("_index.md", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderAnyRoot(string filePath, HashSet<string> roots)
    {
        if (roots.Count == 0) return false;
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (filePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                filePath.Equals(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string ResolveRelativePath(string? collectionRoot, string filePath)
    {
        if (string.IsNullOrWhiteSpace(collectionRoot))
            return Path.GetFileName(filePath);
        return Path.GetRelativePath(collectionRoot, filePath).Replace('\\', '/');
    }

    private static string ResolveSlugPath(string relativePath, string relativeDir, string? slugOverride)
    {
        var withoutExtension = NormalizePath(Path.ChangeExtension(relativePath, null) ?? string.Empty);
        return ApplySlugOverride(withoutExtension, slugOverride);
    }

    private static string ApplySlugOverride(string basePath, string? slugOverride)
    {
        if (string.IsNullOrWhiteSpace(slugOverride))
            return basePath;

        var normalized = NormalizePath(slugOverride);
        if (normalized.Contains('/'))
            return normalized;

        if (string.IsNullOrWhiteSpace(basePath))
            return normalized;

        var idx = basePath.LastIndexOf('/');
        if (idx < 0)
            return normalized;

        var parent = basePath.Substring(0, idx);
        if (string.IsNullOrWhiteSpace(parent))
            return normalized;

        return parent + "/" + normalized;
    }

    private static string? ResolveCollectionRootForFile(string rootPath, string input, string filePath)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var full = Path.IsPathRooted(input) ? input : Path.Combine(rootPath, input);
        if (!full.Contains('*'))
            return Path.GetFullPath(full);

        var normalized = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = normalized.Split('*');
        if (parts.Length != 2)
            return Path.GetFullPath(full);

        var basePath = parts[0].TrimEnd(Path.DirectorySeparatorChar);
        var tail = parts[1].TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(tail))
            return Path.GetFullPath(full);

        if (!filePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(full);

        var relative = Path.GetRelativePath(basePath, filePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return Path.GetFullPath(full);

        var wildcardSegment = segments[0];
        var candidate = Path.Combine(basePath, wildcardSegment, tail);
        return Path.GetFullPath(candidate);
    }

    private static string ReplaceProjectPlaceholder(string output, string? projectSlug)
    {
        if (string.IsNullOrWhiteSpace(output))
            return output;
        if (string.IsNullOrWhiteSpace(projectSlug))
            return output.Replace("{project}", string.Empty, StringComparison.OrdinalIgnoreCase);
        return output.Replace("{project}", projectSlug, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveProjectSlug(WebSitePlan plan, string filePath)
    {
        foreach (var project in plan.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.ContentPath))
                continue;

            if (filePath.StartsWith(project.ContentPath, StringComparison.OrdinalIgnoreCase))
                return project.Slug;
        }

        return null;
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
