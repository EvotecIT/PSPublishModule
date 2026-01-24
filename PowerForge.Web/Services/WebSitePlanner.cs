using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web;

public static class WebSitePlanner
{
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
            var fileCount = CountMarkdownFiles(inputPath);
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
            var fileCount = CountMarkdownFiles(contentPath);

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

    private static int CountMarkdownFiles(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;

        if (path.Contains('*'))
            return CountMarkdownFilesWithWildcard(path);

        if (!Directory.Exists(path))
            return 0;

        return Directory.EnumerateFiles(path, "*.md", SearchOption.AllDirectories).Count();
    }

    private static int CountMarkdownFilesWithWildcard(string path)
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
            total += Directory.EnumerateFiles(candidate, "*.md", SearchOption.AllDirectories).Count();
        }

        return total;
    }
}
