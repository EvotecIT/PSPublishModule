using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Builds a site plan from configuration.</summary>
public static class WebSitePlanner
{
    /// <summary>Resolves a site plan from a site spec.</summary>
    /// <param name="spec">Site configuration.</param>
    /// <param name="configPath">Path to the site config file.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>Resolved site plan.</returns>
    public static WebSitePlan Plan(SiteSpec spec, string configPath, JsonSerializerOptions? options = null)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(configPath)) throw new ArgumentException("Config path is required.", nameof(configPath));

        var fullConfigPath = Path.GetFullPath(configPath);
        var root = Path.GetDirectoryName(fullConfigPath) ?? Directory.GetCurrentDirectory();

        var plan = new WebSitePlan
        {
            Name = spec.Name,
            BaseUrl = spec.BaseUrl,
            ConfigPath = fullConfigPath,
            RootPath = root,
            ContentRoot = ResolvePath(root, spec.ContentRoot),
            ContentRoots = ResolvePaths(root, spec.ContentRoots),
            ProjectsRoot = ResolvePath(root, spec.ProjectsRoot),
            ThemesRoot = ResolvePath(root, spec.ThemesRoot),
            SharedRoot = ResolvePath(root, spec.SharedRoot),
            RouteOverrideCount = spec.RouteOverrides?.Length ?? 0,
            RedirectCount = spec.Redirects?.Length ?? 0
        };

        plan.Collections = BuildCollectionPlans(spec, root);
        plan.Projects = BuildProjectPlans(spec, plan, options);

        return plan;
    }

    private static WebCollectionPlan[] BuildCollectionPlans(SiteSpec spec, string root)
    {
        if (spec.Collections is null || spec.Collections.Length == 0)
            return Array.Empty<WebCollectionPlan>();

        var list = new List<WebCollectionPlan>(spec.Collections.Length);
        foreach (var c in spec.Collections)
        {
            if (c is null) continue;
            var inputPath = ResolvePath(root, c.Input);
            var fileCount = CountMarkdownFiles(inputPath, c.Include, c.Exclude);
            list.Add(new WebCollectionPlan
            {
                Name = c.Name,
                InputPath = inputPath ?? string.Empty,
                OutputPath = c.Output,
                FileCount = fileCount
            });
        }

        return list.ToArray();
    }

    private static WebProjectPlan[] BuildProjectPlans(SiteSpec spec, WebSitePlan plan, JsonSerializerOptions? options)
    {
        var projectsRoot = plan.ProjectsRoot;
        if (string.IsNullOrWhiteSpace(projectsRoot) || !Directory.Exists(projectsRoot))
            return Array.Empty<WebProjectPlan>();

        var list = new List<WebProjectPlan>();
        foreach (var dir in Directory.GetDirectories(projectsRoot))
        {
            var projectFile = Path.Combine(dir, "project.json");
            if (!File.Exists(projectFile))
                continue;

            ProjectSpec? project = null;
            try
            {
                project = WebSiteSpecLoader.LoadProjectWithPath(projectFile, options).Spec;
            }
            catch
            {
                // Ignore invalid project specs during plan; they will surface in build/verify later.
            }

            var contentPath = Path.Combine(dir, "content");
            var fileCount = CountMarkdownFiles(contentPath, Array.Empty<string>(), Array.Empty<string>());

            list.Add(new WebProjectPlan
            {
                Name = project?.Name ?? Path.GetFileName(dir),
                Slug = project?.Slug ?? Path.GetFileName(dir),
                RootPath = dir,
                ContentPath = Directory.Exists(contentPath) ? contentPath : null,
                ContentFileCount = fileCount
            });
        }

        return list.ToArray();
    }

    private static string? ResolvePath(string root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(root, path));
    }

    private static string[] ResolvePaths(string root, string[]? paths)
    {
        if (paths is null || paths.Length == 0)
            return Array.Empty<string>();

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolvePath(root, path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
    }

    private static int CountMarkdownFiles(string? path, string[]? includePatterns, string[]? excludePatterns)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;

        if (path.Contains('*'))
            return CountMarkdownFilesWithWildcard(path, includePatterns ?? Array.Empty<string>(), excludePatterns ?? Array.Empty<string>());

        if (!Directory.Exists(path))
            return 0;

        var files = Directory.EnumerateFiles(path, "*.md", SearchOption.AllDirectories);
        return FilterByPatterns(path, files, includePatterns ?? Array.Empty<string>(), excludePatterns ?? Array.Empty<string>()).Count();
    }

    private static int CountMarkdownFilesWithWildcard(string path, string[] includePatterns, string[] excludePatterns)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = normalized.Split('*');
        if (parts.Length != 2)
            return 0;

        var basePath = parts[0].TrimEnd(Path.DirectorySeparatorChar);
        var tail = parts[1].TrimStart(Path.DirectorySeparatorChar);
        if (!Directory.Exists(basePath))
            return 0;

        var total = 0;
        foreach (var dir in Directory.GetDirectories(basePath))
        {
            var candidate = string.IsNullOrEmpty(tail) ? dir : Path.Combine(dir, tail);
            if (!Directory.Exists(candidate))
                continue;
            var files = Directory.EnumerateFiles(candidate, "*.md", SearchOption.AllDirectories);
            total += FilterByPatterns(candidate, files, includePatterns, excludePatterns).Count();
        }

        return total;
    }

    private static IEnumerable<string> FilterByPatterns(string basePath, IEnumerable<string> files, string[] includePatterns, string[] excludePatterns)
    {
        var includes = NormalizePatterns(includePatterns);
        var excludes = NormalizePatterns(excludePatterns);

        foreach (var file in files)
        {
            if (excludes.Length > 0 && MatchesAny(excludes, basePath, file))
                continue;
            if (includes.Length == 0 || MatchesAny(includes, basePath, file))
                yield return file;
        }
    }

    private static string[] NormalizePatterns(string[] patterns)
    {
        return patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Replace('\\', '/').Trim())
            .ToArray();
    }

    private static bool MatchesAny(string[] patterns, string basePath, string file)
    {
        foreach (var pattern in patterns)
        {
            if (Path.IsPathRooted(pattern))
            {
                if (GlobMatch(pattern.Replace('\\', '/'), file.Replace('\\', '/')))
                    return true;
                continue;
            }

            var relative = Path.GetRelativePath(basePath, file).Replace('\\', '/');
            if (GlobMatch(pattern, relative))
                return true;
        }
        return false;
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(value, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
