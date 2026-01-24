using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static class WebSiteBuilder
{
    public static WebBuildResult Build(SiteSpec spec, WebSitePlan plan, string outputPath, JsonSerializerOptions? options = null)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

        var outDir = Path.GetFullPath(outputPath.Trim().Trim('"'));
        Directory.CreateDirectory(outDir);

        var metaDir = Path.Combine(outDir, "_powerforge");
        Directory.CreateDirectory(metaDir);

        var jsonOptions = WebJson.Options;
        var planPath = Path.Combine(metaDir, "site-plan.json");
        var specPath = Path.Combine(metaDir, "site-spec.json");
        var redirectsPath = Path.Combine(metaDir, "redirects.json");

        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, jsonOptions));
        File.WriteAllText(specPath, JsonSerializer.Serialize(spec, jsonOptions));

        var redirects = new List<RedirectSpec>();
        if (spec.RouteOverrides is { Length: > 0 }) redirects.AddRange(spec.RouteOverrides);
        if (spec.Redirects is { Length: > 0 }) redirects.AddRange(spec.Redirects);

        var projectSpecs = LoadProjectSpecs(plan.ProjectsRoot, options ?? WebJson.Options).ToList();
        foreach (var project in projectSpecs)
        {
            if (project.Redirects is { Length: > 0 })
                redirects.AddRange(project.Redirects);
        }

        var data = LoadData(spec, plan.RootPath);
        var projectMap = projectSpecs
            .Where(p => !string.IsNullOrWhiteSpace(p.Slug))
            .ToDictionary(p => p.Slug, StringComparer.OrdinalIgnoreCase);
        var projectContentMap = projectSpecs
            .Where(p => p.Content is not null && !string.IsNullOrWhiteSpace(p.Slug))
            .ToDictionary(p => p.Slug, p => p.Content!, StringComparer.OrdinalIgnoreCase);
        var items = BuildContentItems(spec, plan, redirects, data, projectMap, projectContentMap);
        foreach (var item in items)
        {
            WriteContentItem(outDir, spec, plan.RootPath, item, data, projectMap);
        }

        CopyThemeAssets(spec, plan.RootPath, outDir);
        WriteSearchIndex(outDir, items);

        var redirectsPayload = new
        {
            routeOverrides = spec.RouteOverrides,
            redirects = redirects
        };
        File.WriteAllText(redirectsPath, JsonSerializer.Serialize(redirectsPayload, jsonOptions));
        WriteRedirectOutputs(outDir, redirects);

        return new WebBuildResult
        {
            OutputPath = outDir,
            PlanPath = planPath,
            SpecPath = specPath,
            RedirectsPath = redirectsPath,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    private static void CopyThemeAssets(SiteSpec spec, string rootPath, string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(spec.DefaultTheme))
            return;

        var themeRoot = ResolveThemeRoot(spec, rootPath);
        if (string.IsNullOrWhiteSpace(themeRoot) || !Directory.Exists(themeRoot))
            return;

        var loader = new ThemeLoader();
        var manifest = loader.Load(themeRoot);
        var assetsDir = manifest?.AssetsPath ?? "assets";
        if (string.IsNullOrWhiteSpace(assetsDir))
            return;

        var source = Path.Combine(themeRoot, assetsDir);
        if (!Directory.Exists(source))
            return;

        var outputThemesRoot = "themes";
        if (!string.IsNullOrWhiteSpace(spec.ThemesRoot) && !Path.IsPathRooted(spec.ThemesRoot))
            outputThemesRoot = spec.ThemesRoot.Trim().TrimStart('/', '\\');

        var themeName = manifest?.Name;
        if (string.IsNullOrWhiteSpace(themeName))
            themeName = spec.DefaultTheme;

        var destination = Path.Combine(outputRoot, outputThemesRoot, themeName, assetsDir);
        CopyDirectory(source, destination);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }

    private static IReadOnlyDictionary<string, object?> LoadData(SiteSpec spec, string rootPath)
    {
        var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
        var basePath = Path.IsPathRooted(dataRoot) ? dataRoot : Path.Combine(rootPath, dataRoot);
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var themeData = LoadThemeData(spec, rootPath);
        if (themeData is not null)
            data["theme"] = themeData;

        if (!Directory.Exists(basePath))
            return data;
        foreach (var file in Directory.EnumerateFiles(basePath, "*.json", SearchOption.AllDirectories))
        {
            object? value;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                value = ConvertJsonElement(doc.RootElement);
            }
            catch
            {
                continue;
            }

            var relative = Path.GetRelativePath(basePath, file);
            var segments = relative.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            if (segments.Length == 0) continue;
            segments[^1] = Path.GetFileNameWithoutExtension(segments[^1]);
            AddNestedData(data, segments, value);
        }

        return data;
    }

    private static Dictionary<string, object?>? LoadThemeData(SiteSpec spec, string rootPath)
    {
        var themeRoot = ResolveThemeRoot(spec, rootPath);
        if (string.IsNullOrWhiteSpace(themeRoot) || !Directory.Exists(themeRoot))
            return null;

        var manifestPath = Path.Combine(themeRoot, "theme.json");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = ConvertJsonElement(doc.RootElement) as Dictionary<string, object?>;
            if (root is null) return null;

            if (!root.TryGetValue("tokens", out var tokens) || tokens is null)
                return null;

            var themeData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = root.TryGetValue("name", out var name) ? name : spec.DefaultTheme ?? string.Empty,
                ["tokens"] = tokens
            };

            return themeData;
        }
        catch
        {
            return null;
        }
    }

    private static void AddNestedData(Dictionary<string, object?> data, string[] segments, object? value)
    {
        var current = data;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var key = segments[i];
            if (!current.TryGetValue(key, out var existing) || existing is not Dictionary<string, object?> child)
            {
                child = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[key] = child;
            }
            current = child;
        }

        current[segments[^1]] = value;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in element.EnumerateObject())
                {
                    map[prop.Name] = ConvertJsonElement(prop.Value);
                }
                return map;
            }
            case JsonValueKind.Array:
            {
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ConvertJsonElement(item));
                }
                return list;
            }
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? l : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

    private static string BuildTableOfContents(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var regex = new Regex("<h(?<level>[2-3])[^>]*>(?<text>.*?)</h\\1>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var matches = regex.Matches(html);
        if (matches.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<nav class=\"pf-toc\">");
        sb.AppendLine("  <div class=\"pf-toc-title\">On this page</div>");
        sb.AppendLine("  <ul>");
        foreach (Match match in matches)
        {
            var level = match.Groups["level"].Value;
            var text = StripTags(match.Groups["text"].Value).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            var slug = Slugify(text);
            sb.AppendLine($"    <li class=\"pf-toc-l{level}\"><a href=\"#{slug}\">{System.Web.HttpUtility.HtmlEncode(text)}</a></li>");
        }
        sb.AppendLine("  </ul>");
        sb.AppendLine("</nav>");
        return sb.ToString();
    }

    private static string StripTags(string input)
    {
        return Regex.Replace(input, "<.*?>", string.Empty);
    }

    private static void WriteSearchIndex(string outputRoot, IReadOnlyList<ContentItem> items)
    {
        if (items.Count == 0) return;

        var entries = new List<SearchIndexEntry>();
        foreach (var item in items)
        {
            if (item.Draft) continue;

            var snippet = BuildSnippet(item.HtmlContent, 240);
            entries.Add(new SearchIndexEntry
            {
                Title = item.Title,
                Url = item.OutputPath,
                Description = item.Description,
                Snippet = snippet,
                Collection = item.Collection,
                Tags = item.Tags
            });
        }

        var searchDir = Path.Combine(outputRoot, "search");
        Directory.CreateDirectory(searchDir);
        var searchPath = Path.Combine(searchDir, "index.json");
        File.WriteAllText(searchPath, JsonSerializer.Serialize(entries, WebJson.Options));
    }

    private static string BuildSnippet(string html, int maxLength)
    {
        var text = StripTags(html);
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = Regex.Replace(text, "\\s+", " ").Trim();
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength).Trim() + "...";
    }

    private static IEnumerable<ProjectSpec> LoadProjectSpecs(string? projectsRoot, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(projectsRoot) || !Directory.Exists(projectsRoot))
            yield break;

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
                // ignore invalid project specs for now
            }

            if (project is not null)
                yield return project;
        }
    }

    private static List<ContentItem> BuildContentItems(
        SiteSpec spec,
        WebSitePlan plan,
        List<RedirectSpec> redirects,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap,
        IReadOnlyDictionary<string, ProjectContentSpec> projectContentMap)
    {
        var items = new List<ContentItem>();
        if (spec.Collections is null || spec.Collections.Length == 0)
            return items;

        foreach (var collection in spec.Collections)
        {
            if (collection is null) continue;
            var include = collection.Include;
            var exclude = collection.Exclude;

            foreach (var file in EnumerateCollectionFiles(plan.RootPath, collection.Input, include, exclude))
            {
                var markdown = File.ReadAllText(file);
                var (matter, body) = FrontMatterParser.Parse(markdown);
                var effectiveBody = IncludePreprocessor.Apply(body, plan.RootPath);
                var processedBody = ShortcodeProcessor.Apply(effectiveBody, data);

                var title = matter?.Title ?? FrontMatterParser.ExtractTitleFromMarkdown(processedBody) ?? Path.GetFileNameWithoutExtension(file);
                var slug = matter?.Slug ?? Slugify(Path.GetFileNameWithoutExtension(file));
                var description = matter?.Description ?? string.Empty;
                var route = BuildRoute(collection.Output, slug, spec.TrailingSlash);

                if (matter?.Aliases is { Length: > 0 })
                {
                    foreach (var alias in matter.Aliases)
                    {
                        if (string.IsNullOrWhiteSpace(alias)) continue;
                        redirects.Add(new RedirectSpec
                        {
                            From = NormalizeAlias(alias),
                            To = route,
                            Status = 301,
                            MatchType = RedirectMatchType.Exact,
                            PreserveQuery = true
                        });
                    }
                }

                var htmlContent = MarkdownRenderer.RenderToHtml(processedBody);
                var toc = BuildTableOfContents(htmlContent);
                var projectSlug = ResolveProjectSlug(plan, file);
                if (collection.Name.Equals("projects", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(projectSlug) &&
                    projectContentMap.TryGetValue(projectSlug, out var perProject))
                {
                    var projectInclude = perProject.Include;
                    var projectExclude = perProject.Exclude;
                    if (!MatchesFile(plan.RootPath, file, collection.Input, projectInclude, projectExclude))
                        continue;
                }
                items.Add(new ContentItem
                {
                    SourcePath = file,
                    Collection = collection.Name,
                    OutputPath = route,
                    Title = title,
                    Description = description,
                    Date = matter?.Date,
                    Slug = slug,
                    Tags = matter?.Tags ?? Array.Empty<string>(),
                    Aliases = matter?.Aliases ?? Array.Empty<string>(),
                    Draft = matter?.Draft ?? false,
                    Canonical = matter?.Canonical,
                    EditPath = matter?.EditPath,
                    Layout = matter?.Layout ?? collection.DefaultLayout,
                    Template = matter?.Template,
                    HtmlContent = htmlContent,
                    TocHtml = toc,
                    ProjectSlug = projectSlug
                });
            }
        }

        return items;
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

    private static bool MatchesFile(string rootPath, string filePath, string input, string[] includePatterns, string[] excludePatterns)
    {
        var full = Path.IsPathRooted(input) ? input : Path.Combine(rootPath, input);
        var basePath = full.Contains('*')
            ? full.Split('*')[0].TrimEnd(Path.DirectorySeparatorChar)
            : full;
        if (!Directory.Exists(basePath))
            return false;

        var includes = NormalizePatterns(includePatterns);
        var excludes = NormalizePatterns(excludePatterns);
        if (excludes.Length > 0 && MatchesAny(excludes, basePath, filePath))
            return false;
        if (includes.Length == 0)
            return true;
        return MatchesAny(includes, basePath, filePath);
    }

    private static void WriteContentItem(
        string outputRoot,
        SiteSpec spec,
        string rootPath,
        ContentItem item,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap)
    {
        if (item.Draft) return;

        var targetDir = ResolveOutputDirectory(outputRoot, item.OutputPath);
        Directory.CreateDirectory(targetDir);
        var outputFile = Path.Combine(targetDir, "index.html");

        var html = RenderHtmlPage(spec, rootPath, item, data, projectMap);
        File.WriteAllText(outputFile, html);
    }

    private static string RenderHtmlPage(
        SiteSpec spec,
        string rootPath,
        ContentItem item,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap)
    {
        var cssLinks = ResolveCssLinks(spec, item.OutputPath);
        var jsLinks = ResolveJsLinks(spec, item.OutputPath);
        var preloads = RenderPreloads(spec);
        var criticalCss = RenderCriticalCss(spec, rootPath);
        var canonical = !string.IsNullOrWhiteSpace(item.Canonical) ? $"<link rel=\"canonical\" href=\"{item.Canonical}\" />" : string.Empty;

        var cssHtml = string.Join(Environment.NewLine, cssLinks.Select(c => $"<link rel=\"stylesheet\" href=\"{c}\" />"));
        var jsHtml = string.Join(Environment.NewLine, jsLinks.Select(j => $"<script src=\"{j}\" defer></script>"));
        var descriptionMeta = string.IsNullOrWhiteSpace(item.Description) ? string.Empty : $"<meta name=\"description\" content=\"{System.Web.HttpUtility.HtmlEncode(item.Description)}\" />";
        projectMap.TryGetValue(item.ProjectSlug ?? string.Empty, out var projectSpec);
        var renderContext = new ThemeRenderContext
        {
            Site = spec,
            Page = item,
            Data = data,
            Project = projectSpec,
            CssHtml = cssHtml,
            JsHtml = jsHtml,
            PreloadsHtml = preloads,
            CriticalCssHtml = criticalCss,
            CanonicalHtml = canonical,
            DescriptionMetaHtml = descriptionMeta
        };

        var themeRoot = ResolveThemeRoot(spec, rootPath);
        if (!string.IsNullOrWhiteSpace(themeRoot) && Directory.Exists(themeRoot))
        {
            var loader = new ThemeLoader();
            var manifest = loader.Load(themeRoot);
            var layoutName = item.Template ?? item.Layout ?? "base";
            var layoutPath = loader.ResolveLayoutPath(themeRoot, manifest, layoutName);
            if (!string.IsNullOrWhiteSpace(layoutPath))
            {
                var template = File.ReadAllText(layoutPath);
                var engine = ThemeEngineRegistry.Resolve(spec.ThemeEngine ?? manifest?.Engine);
                return engine.Render(template, renderContext, name =>
                {
                    var partialPath = loader.ResolvePartialPath(themeRoot, manifest, name);
                    return partialPath is null ? null : File.ReadAllText(partialPath);
                });
            }
        }

        return $@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{System.Web.HttpUtility.HtmlEncode(item.Title)}</title>
  {descriptionMeta}
  {canonical}
  {preloads}
  {criticalCss}
  {cssHtml}
</head>
<body>
  <main class=""pf-web-content"">
{item.HtmlContent}
  </main>
  {jsHtml}
</body>
</html>";
    }

    private static string RenderPreloads(SiteSpec spec)
    {
        if (spec.AssetRegistry?.Preloads is null || spec.AssetRegistry.Preloads.Length == 0)
            return string.Empty;

        return string.Join(Environment.NewLine, spec.AssetRegistry.Preloads.Select(p =>
        {
            var type = string.IsNullOrWhiteSpace(p.Type) ? string.Empty : $" type=\"{p.Type}\"";
            var cross = string.IsNullOrWhiteSpace(p.Crossorigin) ? string.Empty : $" crossorigin=\"{p.Crossorigin}\"";
            return $"<link rel=\"preload\" href=\"{p.Href}\" as=\"{p.As}\"{type}{cross} />";
        }));
    }

    private static string RenderCriticalCss(SiteSpec spec, string rootPath)
    {
        if (spec.AssetRegistry?.CriticalCss is null || spec.AssetRegistry.CriticalCss.Length == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var css in spec.AssetRegistry.CriticalCss)
        {
            if (string.IsNullOrWhiteSpace(css.Path)) continue;
            var fullPath = Path.IsPathRooted(css.Path)
                ? css.Path
                : Path.Combine(rootPath, css.Path);
            if (!File.Exists(fullPath)) continue;
            sb.AppendLine("<style>");
            sb.AppendLine(File.ReadAllText(fullPath));
            sb.AppendLine("</style>");
        }
        return sb.ToString();
    }

    private static string? ResolveThemeRoot(SiteSpec spec, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(spec.DefaultTheme)) return null;
        var themesRoot = string.IsNullOrWhiteSpace(spec.ThemesRoot) ? "themes" : spec.ThemesRoot;
        var basePath = Path.IsPathRooted(themesRoot) ? themesRoot : Path.Combine(rootPath, themesRoot);
        return Path.Combine(basePath, spec.DefaultTheme);
    }

    private static IEnumerable<string> ResolveCssLinks(SiteSpec spec, string route)
    {
        var bundles = ResolveBundles(spec, route);
        return bundles.SelectMany(b => b.Css).Distinct();
    }

    private static IEnumerable<string> ResolveJsLinks(SiteSpec spec, string route)
    {
        var bundles = ResolveBundles(spec, route);
        return bundles.SelectMany(b => b.Js).Distinct();
    }

    private static IEnumerable<AssetBundleSpec> ResolveBundles(SiteSpec spec, string route)
    {
        if (spec.AssetRegistry is null)
            return Array.Empty<AssetBundleSpec>();

        var bundleMap = spec.AssetRegistry.Bundles.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);
        var results = new List<AssetBundleSpec>();
        foreach (var mapping in spec.AssetRegistry.RouteBundles)
        {
            if (!GlobMatch(mapping.Match, route)) continue;
            foreach (var name in mapping.Bundles)
            {
                if (bundleMap.TryGetValue(name, out var bundle))
                    results.Add(bundle);
            }
        }

        return results;
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
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

    private static string ResolveOutputDirectory(string outputRoot, string route)
    {
        var trimmed = route.Trim('/');
        if (string.IsNullOrWhiteSpace(trimmed))
            return outputRoot;
        return Path.Combine(outputRoot, trimmed.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string NormalizeAlias(string alias)
    {
        if (Uri.TryCreate(alias, UriKind.Absolute, out var uri))
            return uri.AbsolutePath;
        return alias;
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

    private static IEnumerable<string> EnumerateCollectionFiles(string rootPath, string input, string[]? includePatterns, string[]? excludePatterns)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();

        var full = Path.IsPathRooted(input) ? input : Path.Combine(rootPath, input);
        if (full.Contains('*'))
            return EnumerateCollectionFilesWithWildcard(full, includePatterns ?? Array.Empty<string>(), excludePatterns ?? Array.Empty<string>());

        if (!Directory.Exists(full))
            return Array.Empty<string>();

        return FilterByPatterns(full,
            Directory.EnumerateFiles(full, "*.md", SearchOption.AllDirectories),
            includePatterns ?? Array.Empty<string>(),
            excludePatterns ?? Array.Empty<string>());
    }

    private static IEnumerable<string> EnumerateCollectionFilesWithWildcard(string path, string[] includePatterns, string[] excludePatterns)
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
            var files = Directory.EnumerateFiles(candidate, "*.md", SearchOption.AllDirectories);
            results.AddRange(FilterByPatterns(candidate, files, includePatterns, excludePatterns));
        }

        return results;
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
            File.WriteAllLines(path, lines);
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
        File.WriteAllText(path, JsonSerializer.Serialize(payload, WebJson.Options));
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
        File.WriteAllText(path, JsonSerializer.Serialize(payload, WebJson.Options));
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
