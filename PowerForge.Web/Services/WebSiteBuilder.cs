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

        var data = LoadData(spec, plan, projectSpecs);
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
        CopyStaticAssets(spec, plan.RootPath, outDir);
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
        var manifest = loader.Load(themeRoot, ResolveThemesRoot(spec, rootPath));
        if (manifest is null)
            return;

        var outputThemesFolder = ResolveThemesFolder(spec);

        var chain = BuildThemeChain(themeRoot, manifest);
        foreach (var entry in chain)
        {
            var assetsDir = entry.Manifest.AssetsPath ?? "assets";
            if (string.IsNullOrWhiteSpace(assetsDir))
                continue;

            var source = Path.Combine(entry.Root, assetsDir);
            if (!Directory.Exists(source))
                continue;

            var entryThemeName = string.IsNullOrWhiteSpace(entry.Manifest.Name)
                ? Path.GetFileName(entry.Root)
                : entry.Manifest.Name;
            if (string.IsNullOrWhiteSpace(entryThemeName))
                entryThemeName = spec.DefaultTheme ?? "theme";

            var destination = Path.Combine(outputRoot, outputThemesFolder, entryThemeName, assetsDir);
            CopyDirectory(source, destination);
        }
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

    private static void CopyStaticAssets(SiteSpec spec, string rootPath, string outputRoot)
    {
        if (spec.StaticAssets is null || spec.StaticAssets.Length == 0)
            return;

        foreach (var asset in spec.StaticAssets)
        {
            if (string.IsNullOrWhiteSpace(asset.Source))
                continue;

            var sourcePath = Path.IsPathRooted(asset.Source)
                ? asset.Source
                : Path.Combine(rootPath, asset.Source);

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                continue;

            var destination = (asset.Destination ?? string.Empty).TrimStart('/', '\\');
            if (File.Exists(sourcePath))
            {
                var destPath = string.IsNullOrWhiteSpace(destination)
                    ? Path.Combine(outputRoot, Path.GetFileName(sourcePath))
                    : (Path.HasExtension(destination)
                        ? Path.Combine(outputRoot, destination)
                        : Path.Combine(outputRoot, destination, Path.GetFileName(sourcePath)));

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrWhiteSpace(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(sourcePath, destPath, true);
                continue;
            }

            var targetRoot = string.IsNullOrWhiteSpace(destination)
                ? outputRoot
                : Path.Combine(outputRoot, destination);
            CopyDirectory(sourcePath, targetRoot);
        }
    }

    private static IReadOnlyDictionary<string, object?> LoadData(SiteSpec spec, WebSitePlan plan, IReadOnlyList<ProjectSpec> projects)
    {
        var rootPath = plan.RootPath;
        var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
        var basePath = Path.IsPathRooted(dataRoot) ? dataRoot : Path.Combine(rootPath, dataRoot);
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var themeData = LoadThemeData(spec, rootPath);
        if (themeData is not null)
            data["theme"] = themeData;

        LoadDataFromFolder(basePath, data);
        LoadProjectData(plan, projects, data);

        return data;
    }

    private static void LoadProjectData(WebSitePlan plan, IReadOnlyList<ProjectSpec> projects, Dictionary<string, object?> data)
    {
        if (plan.Projects is null || plan.Projects.Length == 0) return;

        var projectData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var projectSpecMap = projects
            .Where(p => !string.IsNullOrWhiteSpace(p.Slug))
            .ToDictionary(p => p.Slug, StringComparer.OrdinalIgnoreCase);

        foreach (var project in plan.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.Slug)) continue;
            var dataRoot = Path.Combine(project.RootPath, "data");
            if (!Directory.Exists(dataRoot)) continue;

            var projectBag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            LoadDataFromFolder(dataRoot, projectBag);

            if (projectSpecMap.TryGetValue(project.Slug, out var spec))
            {
                projectBag["spec"] = spec;
            }

            if (projectBag.Count > 0)
                projectData[project.Slug] = projectBag;
        }

        if (projectData.Count > 0)
            data["projects"] = projectData;
    }

    private static IReadOnlyDictionary<string, object?> ResolveDataForProject(
        IReadOnlyDictionary<string, object?> data,
        string? projectSlug)
    {
        if (string.IsNullOrWhiteSpace(projectSlug))
            return data;
        if (!data.TryGetValue("projects", out var projectsObj) || projectsObj is not IReadOnlyDictionary<string, object?> projects)
            return data;
        if (!projects.TryGetValue(projectSlug, out var projectData))
            return data;

        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in data)
            merged[kvp.Key] = kvp.Value;
        merged["project"] = projectData;
        return merged;
    }

    private static void LoadDataFromFolder(string basePath, Dictionary<string, object?> data)
    {
        if (!Directory.Exists(basePath))
            return;

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
    }

    private static Dictionary<string, object?>? LoadThemeData(SiteSpec spec, string rootPath)
    {
        var themeRoot = ResolveThemeRoot(spec, rootPath);
        if (string.IsNullOrWhiteSpace(themeRoot) || !Directory.Exists(themeRoot))
            return null;

        var loader = new ThemeLoader();
        var manifest = loader.Load(themeRoot, ResolveThemesRoot(spec, rootPath));
        if (manifest?.Tokens is null || manifest.Tokens.Count == 0)
            return null;

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = string.IsNullOrWhiteSpace(manifest.Name) ? spec.DefaultTheme ?? string.Empty : manifest.Name,
            ["tokens"] = manifest.Tokens
        };
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
                Tags = item.Tags,
                Project = item.ProjectSlug,
                Meta = item.Meta.Count == 0 ? null : item.Meta
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
            var themeRoot = ResolveThemeRoot(spec, plan.RootPath);
            var loader = new ThemeLoader();
            ThemeManifest? manifest = null;
            ITemplateEngine? shortcodeEngine = null;
            Func<string, string?>? partialResolver = null;
            if (!string.IsNullOrWhiteSpace(themeRoot) && Directory.Exists(themeRoot))
            {
                manifest = loader.Load(themeRoot, ResolveThemesRoot(spec, plan.RootPath));
                shortcodeEngine = ThemeEngineRegistry.Resolve(spec.ThemeEngine ?? manifest?.Engine);
                partialResolver = name =>
                {
                    var path = loader.ResolvePartialPath(themeRoot, manifest, name);
                    return path is null ? null : File.ReadAllText(path);
                };
            }

            foreach (var file in EnumerateCollectionFiles(plan.RootPath, collection.Input, include, exclude))
            {
                var markdown = File.ReadAllText(file);
                var (matter, body) = FrontMatterParser.Parse(markdown);
                var effectiveBody = IncludePreprocessor.Apply(body, plan.RootPath);
                var projectSlug = ResolveProjectSlug(plan, file);
                var dataForShortcodes = ResolveDataForProject(data, projectSlug);
                var shortcodeContext = new ShortcodeRenderContext
                {
                    Site = spec,
                    FrontMatter = matter,
                    Data = dataForShortcodes,
                    ThemeManifest = manifest,
                    ThemeRoot = themeRoot,
                    Engine = shortcodeEngine,
                    PartialResolver = partialResolver
                };
                var processedBody = ShortcodeProcessor.Apply(effectiveBody, shortcodeContext);

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

                var htmlContent = ShouldRenderMarkdown(matter?.Meta)
                    ? MarkdownRenderer.RenderToHtml(processedBody)
                    : processedBody;
                var toc = BuildTableOfContents(htmlContent);
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
                    ProjectSlug = projectSlug,
                    Meta = matter?.Meta ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                });
            }
        }

        return items;
    }

    private static bool ShouldRenderMarkdown(Dictionary<string, object?>? meta)
    {
        if (meta is null) return true;

        if (TryGetMetaBool(meta, "raw_html", out var rawHtml) && rawHtml)
            return false;

        if (TryGetMetaString(meta, "render", out var render) && render.Equals("html", StringComparison.OrdinalIgnoreCase))
            return false;

        if (TryGetMetaString(meta, "format", out var format) && format.Equals("html", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool TryGetMetaBool(Dictionary<string, object?> meta, string key, out bool value)
    {
        value = false;
        if (!meta.TryGetValue(key, out var obj) || obj is null) return false;
        if (obj is bool b)
        {
            value = b;
            return true;
        }
        if (obj is string s && bool.TryParse(s, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    private static bool TryGetMetaString(Dictionary<string, object?> meta, string key, out string value)
    {
        value = string.Empty;
        if (!meta.TryGetValue(key, out var obj) || obj is null) return false;
        value = obj.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetMetaString(Dictionary<string, object?>? meta, string key)
    {
        if (meta is null)
            return string.Empty;

        return TryGetMetaString(meta, key, out var value) ? value : string.Empty;
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

        var effectiveData = ResolveDataForProject(data, item.ProjectSlug);
        var html = RenderHtmlPage(spec, rootPath, item, effectiveData, projectMap);
        File.WriteAllText(outputFile, html);
    }

    private static string RenderHtmlPage(
        SiteSpec spec,
        string rootPath,
        ContentItem item,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap)
    {
        var themeRoot = ResolveThemeRoot(spec, rootPath);
        var loader = new ThemeLoader();
        var manifest = !string.IsNullOrWhiteSpace(themeRoot) && Directory.Exists(themeRoot)
            ? loader.Load(themeRoot, ResolveThemesRoot(spec, rootPath))
            : null;
        var assetRegistry = BuildAssetRegistry(spec, themeRoot, manifest);

        var cssLinks = ResolveCssLinks(assetRegistry, item.OutputPath);
        var jsLinks = ResolveJsLinks(assetRegistry, item.OutputPath);
        var preloads = RenderPreloads(assetRegistry);
        var criticalCss = RenderCriticalCss(assetRegistry, rootPath);
        var canonical = !string.IsNullOrWhiteSpace(item.Canonical) ? $"<link rel=\"canonical\" href=\"{item.Canonical}\" />" : string.Empty;

        var cssHtml = RenderCssLinks(cssLinks, assetRegistry);
        var jsHtml = string.Join(Environment.NewLine, jsLinks.Select(j => $"<script src=\"{j}\" defer></script>"));
        var descriptionMeta = string.IsNullOrWhiteSpace(item.Description) ? string.Empty : $"<meta name=\"description\" content=\"{System.Web.HttpUtility.HtmlEncode(item.Description)}\" />";
        projectMap.TryGetValue(item.ProjectSlug ?? string.Empty, out var projectSpec);
        var breadcrumbs = BuildBreadcrumbs(spec, item);
        var headHtml = BuildHeadHtml(spec, item, rootPath);
        var bodyClass = BuildBodyClass(spec, item);
        var openGraph = BuildOpenGraphHtml(spec, item);
        var structuredData = BuildStructuredDataHtml(spec, item, breadcrumbs);
        var extraCss = GetMetaString(item.Meta, "extra_css");
        var extraScripts = BuildExtraScriptsHtml(item, rootPath);

        var renderContext = new ThemeRenderContext
        {
            Site = spec,
            Page = item,
            Data = data,
            Project = projectSpec,
            Navigation = BuildNavigation(spec, item.OutputPath),
            Breadcrumbs = breadcrumbs,
            CurrentPath = item.OutputPath,
            CssHtml = cssHtml,
            JsHtml = jsHtml,
            PreloadsHtml = preloads,
            CriticalCssHtml = criticalCss,
            CanonicalHtml = canonical,
            DescriptionMetaHtml = descriptionMeta,
            HeadHtml = headHtml,
            OpenGraphHtml = openGraph,
            StructuredDataHtml = structuredData,
            ExtraCssHtml = extraCss,
            ExtraScriptsHtml = extraScripts,
            BodyClass = bodyClass
        };

        if (!string.IsNullOrWhiteSpace(themeRoot) && Directory.Exists(themeRoot))
        {
            var layoutName = item.Template ?? item.Layout ?? manifest?.DefaultLayout ?? "base";
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
  {headHtml}
  {openGraph}
  {structuredData}
  {extraCss}
  {cssHtml}
</head>
<body{(string.IsNullOrWhiteSpace(bodyClass) ? string.Empty : $" class=\"{bodyClass}\"")}>
  <main class=""pf-web-content"">
{item.HtmlContent}
  </main>
  {jsHtml}
  {extraScripts}
</body>
</html>";
    }

    private static NavigationRuntime BuildNavigation(SiteSpec spec, string currentPath)
    {
        var nav = new NavigationRuntime();
        if (spec.Navigation?.Menus is null || spec.Navigation.Menus.Length == 0)
            return nav;

        nav.Menus = spec.Navigation.Menus
            .Select(m => new NavigationMenu
            {
                Name = m.Name,
                Label = m.Label,
                Items = BuildMenuItems(m.Items, currentPath)
            })
            .ToArray();
        return nav;
    }

    private static NavigationItem[] BuildMenuItems(MenuItemSpec[] items, string currentPath)
    {
        if (items is null || items.Length == 0) return Array.Empty<NavigationItem>();

        var ordered = OrderMenuItems(items);
        var result = new List<NavigationItem>();
        foreach (var item in ordered)
        {
            var url = item.Url ?? string.Empty;
            var normalized = NormalizeRouteForMatch(url);
            var isExternal = item.External ?? IsExternalUrl(url);
            var isActive = !isExternal && MatchesMenuItem(item, currentPath, normalized, exactOnly: true);
            var isAncestor = !isExternal && !isActive && MatchesMenuItem(item, currentPath, normalized, exactOnly: false);

            var children = BuildMenuItems(item.Items, currentPath);
            if (children.Any(c => c.IsActive || c.IsAncestor))
                isAncestor = true;

            result.Add(new NavigationItem
            {
                Title = item.Title,
                Url = item.Url,
                Icon = item.Icon,
                Badge = item.Badge,
                Description = item.Description,
                Target = item.Target,
                Rel = item.Rel,
                External = isExternal,
                Weight = item.Weight,
                Match = item.Match,
                IsActive = isActive,
                IsAncestor = isAncestor,
                Items = children
            });
        }

        return result.ToArray();
    }

    private static IEnumerable<MenuItemSpec> OrderMenuItems(IEnumerable<MenuItemSpec> items)
    {
        if (items is null) return Array.Empty<MenuItemSpec>();
        var list = items.ToList();
        var hasWeights = list.Any(i => i.Weight.HasValue);
        if (!hasWeights) return list;
        return list
            .OrderBy(i => i.Weight ?? 0)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool MatchesMenuItem(MenuItemSpec item, string currentPath, string normalizedUrl, bool exactOnly)
    {
        if (!string.IsNullOrWhiteSpace(item.Match))
            return GlobMatch(item.Match, currentPath);

        if (string.IsNullOrWhiteSpace(normalizedUrl))
            return false;

        if (string.Equals(currentPath, normalizedUrl, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!exactOnly && normalizedUrl.Length > 1 &&
            currentPath.StartsWith(normalizedUrl, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string NormalizeRouteForMatch(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var trimmed = url.Trim();
        var hashIndex = trimmed.IndexOf('#');
        if (hashIndex >= 0)
            trimmed = trimmed.Substring(0, hashIndex);
        var queryIndex = trimmed.IndexOf('?');
        if (queryIndex >= 0)
            trimmed = trimmed.Substring(0, queryIndex);

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase))
        {
            var absoluteValue = trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase)
                ? "https:" + trimmed
                : trimmed;
            if (Uri.TryCreate(absoluteValue, UriKind.Absolute, out var absolute))
                trimmed = absolute.AbsolutePath;
        }

        if (!trimmed.StartsWith("/")) trimmed = "/" + trimmed;
        if (!trimmed.EndsWith("/")) trimmed += "/";
        return trimmed;
    }

    private static bool IsExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("//", StringComparison.OrdinalIgnoreCase);
    }

    private static BreadcrumbItem[] BuildBreadcrumbs(SiteSpec spec, ContentItem item)
    {
        var current = NormalizeRouteForMatch(item.OutputPath);
        var crumbs = new List<BreadcrumbItem>();
        var nav = BuildNavigation(spec, item.OutputPath);

        var homeTitle = FindNavTitle(nav, "/") ?? "Home";
        crumbs.Add(new BreadcrumbItem { Title = homeTitle, Url = "/", IsCurrent = current == "/" });
        if (current == "/")
            return crumbs.ToArray();

        var segments = current.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var path = string.Empty;
        for (var i = 0; i < segments.Length; i++)
        {
            path += "/" + segments[i];
            var route = path + "/";
            var isCurrent = i == segments.Length - 1;
            var navTitle = FindNavTitle(nav, route);
            var breadcrumbTitle = GetMetaString(item.Meta, "breadcrumb_title");
            var title = isCurrent
                ? (!string.IsNullOrWhiteSpace(breadcrumbTitle) ? breadcrumbTitle : navTitle ?? item.Title)
                : navTitle ?? HumanizeSegment(segments[i]);
            crumbs.Add(new BreadcrumbItem
            {
                Title = title,
                Url = route,
                IsCurrent = isCurrent
            });
        }

        return crumbs.ToArray();
    }

    private static string? FindNavTitle(NavigationRuntime nav, string route)
    {
        foreach (var menu in nav.Menus)
        {
            var title = FindNavTitle(menu.Items, route);
            if (!string.IsNullOrWhiteSpace(title))
                return title;
        }

        return null;
    }

    private static string? FindNavTitle(IEnumerable<NavigationItem> items, string route)
    {
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Url) &&
                string.Equals(NormalizeRouteForMatch(item.Url), NormalizeRouteForMatch(route), StringComparison.OrdinalIgnoreCase))
                return item.Title;
            if (item.Items.Length == 0) continue;
            var child = FindNavTitle(item.Items, route);
            if (!string.IsNullOrWhiteSpace(child))
                return child;
        }
        return null;
    }

    private static string HumanizeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return segment;
        var text = segment.Replace('-', ' ').Replace('_', ' ');
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text);
    }

    private static string RenderPreloads(AssetRegistrySpec? assets)
    {
        if (assets?.Preloads is null || assets.Preloads.Length == 0)
            return string.Empty;

        return string.Join(Environment.NewLine, assets.Preloads.Select(p =>
        {
            var type = string.IsNullOrWhiteSpace(p.Type) ? string.Empty : $" type=\"{p.Type}\"";
            var cross = string.IsNullOrWhiteSpace(p.Crossorigin) ? string.Empty : $" crossorigin=\"{p.Crossorigin}\"";
            return $"<link rel=\"preload\" href=\"{p.Href}\" as=\"{p.As}\"{type}{cross} />";
        }));
    }

    private static string RenderCriticalCss(AssetRegistrySpec? assets, string rootPath)
    {
        if (assets?.CriticalCss is null || assets.CriticalCss.Length == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var css in assets.CriticalCss)
        {
            if (string.IsNullOrWhiteSpace(css.Path)) continue;
            var fullPath = Path.IsPathRooted(css.Path)
                ? css.Path
                : Path.Combine(rootPath, css.Path);
            if (!File.Exists(fullPath)) continue;
            sb.Append("<style>");
            sb.Append(File.ReadAllText(fullPath));
            sb.AppendLine("</style>");
        }
        return sb.ToString();
    }

    private static string RenderCssLinks(IEnumerable<string> cssLinks, AssetRegistrySpec? assets)
    {
        var links = cssLinks.ToArray();
        if (links.Length == 0) return string.Empty;

        if (assets?.CssStrategy is not null &&
            assets.CssStrategy.Equals("async", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join(Environment.NewLine, links.Select(RenderAsyncCssLink));
        }

        return string.Join(Environment.NewLine, links.Select(c => $"<link rel=\"stylesheet\" href=\"{c}\" />"));
    }

    private static string RenderAsyncCssLink(string href)
    {
        return $@"<link rel=""stylesheet"" href=""{href}"" media=""print"" onload=""this.media='all'"" />
<noscript><link rel=""stylesheet"" href=""{href}"" /></noscript>";
    }

    private static string BuildHeadHtml(SiteSpec spec, ContentItem item, string rootPath)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(spec.Head?.Html))
            parts.Add(spec.Head.Html!);
        var pageHead = GetMetaString(item.Meta, "head_html");
        if (!string.IsNullOrWhiteSpace(pageHead))
            parts.Add(pageHead);
        var headFile = GetMetaString(item.Meta, "head_file");
        if (!string.IsNullOrWhiteSpace(headFile))
        {
            var resolved = ResolveMetaFilePath(item, rootPath, headFile);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                parts.Add(File.ReadAllText(resolved));
        }
        return string.Join(Environment.NewLine, parts);
    }

    private static string BuildExtraScriptsHtml(ContentItem item, string rootPath)
    {
        var parts = new List<string>();
        var inline = GetMetaString(item.Meta, "extra_scripts");
        if (!string.IsNullOrWhiteSpace(inline))
            parts.Add(inline);

        var scriptsFile = GetMetaString(item.Meta, "extra_scripts_file");
        if (!string.IsNullOrWhiteSpace(scriptsFile))
        {
            var resolved = ResolveMetaFilePath(item, rootPath, scriptsFile);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                parts.Add(File.ReadAllText(resolved));
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string BuildBodyClass(SiteSpec spec, ContentItem item)
    {
        var classes = new List<string>();
        if (!string.IsNullOrWhiteSpace(spec.Head?.BodyClass))
            classes.Add(spec.Head.BodyClass!);
        var pageClass = GetMetaString(item.Meta, "body_class");
        if (!string.IsNullOrWhiteSpace(pageClass))
            classes.Add(pageClass);
        return string.Join(" ", classes.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildOpenGraphHtml(SiteSpec spec, ContentItem item)
    {
        if (spec.Social is null || !spec.Social.Enabled)
            return string.Empty;

        if (TryGetMetaBool(item.Meta, "social", out var enabled) && !enabled)
            return string.Empty;

        if (TryGetMetaBool(item.Meta, "social", out enabled) == false)
            return string.Empty;

        var title = GetMetaString(item.Meta, "social_title");
        if (string.IsNullOrWhiteSpace(title))
            title = item.Title;
        var description = GetMetaString(item.Meta, "social_description");
        if (string.IsNullOrWhiteSpace(description))
            description = item.Description;
        var url = string.IsNullOrWhiteSpace(item.Canonical)
            ? ResolveAbsoluteUrl(spec.BaseUrl, item.OutputPath)
            : item.Canonical;
        var siteName = string.IsNullOrWhiteSpace(spec.Social.SiteName) ? spec.Name : spec.Social.SiteName;
        var imageOverride = GetMetaString(item.Meta, "social_image");
        var image = ResolveAbsoluteUrl(spec.BaseUrl, string.IsNullOrWhiteSpace(imageOverride) ? spec.Social.Image : imageOverride);
        var twitterCard = string.IsNullOrWhiteSpace(spec.Social.TwitterCard) ? "summary" : spec.Social.TwitterCard;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!-- Open Graph -->");
        sb.AppendLine($@"<meta property=""og:title"" content=""{System.Web.HttpUtility.HtmlEncode(title)}"" />");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($@"<meta property=""og:description"" content=""{System.Web.HttpUtility.HtmlEncode(description)}"" />");
        sb.AppendLine(@"<meta property=""og:type"" content=""website"" />");
        if (!string.IsNullOrWhiteSpace(url))
            sb.AppendLine($@"<meta property=""og:url"" content=""{System.Web.HttpUtility.HtmlEncode(url)}"" />");
        if (!string.IsNullOrWhiteSpace(image))
            sb.AppendLine($@"<meta property=""og:image"" content=""{System.Web.HttpUtility.HtmlEncode(image)}"" />");
        if (!string.IsNullOrWhiteSpace(siteName))
            sb.AppendLine($@"<meta property=""og:site_name"" content=""{System.Web.HttpUtility.HtmlEncode(siteName)}"" />");

        sb.AppendLine();
        sb.AppendLine("<!-- Twitter Card -->");
        sb.AppendLine($@"<meta name=""twitter:card"" content=""{System.Web.HttpUtility.HtmlEncode(twitterCard)}"" />");
        sb.AppendLine($@"<meta name=""twitter:title"" content=""{System.Web.HttpUtility.HtmlEncode(title)}"" />");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($@"<meta name=""twitter:description"" content=""{System.Web.HttpUtility.HtmlEncode(description)}"" />");
        if (!string.IsNullOrWhiteSpace(image))
            sb.AppendLine($@"<meta name=""twitter:image"" content=""{System.Web.HttpUtility.HtmlEncode(image)}"" />");

        return sb.ToString().TrimEnd();
    }

    private static string BuildStructuredDataHtml(SiteSpec spec, ContentItem item, BreadcrumbItem[] breadcrumbs)
    {
        if (spec.StructuredData is null || !spec.StructuredData.Enabled)
            return string.Empty;

        if (TryGetMetaBool(item.Meta, "structured_data", out var enabled) && !enabled)
            return string.Empty;

        if (TryGetMetaBool(item.Meta, "structured_data", out enabled) == false)
            return string.Empty;

        if (!spec.StructuredData.Breadcrumbs || breadcrumbs.Length == 0)
            return string.Empty;

        var baseUrl = spec.BaseUrl?.TrimEnd('/') ?? string.Empty;
        var lines = new List<string>
        {
            "<script type=\"application/ld+json\">",
            "  {",
            "      \"@context\": \"https://schema.org\",",
            "      \"@type\": \"BreadcrumbList\",",
            "      \"itemListElement\": ["
        };

        for (var i = 0; i < breadcrumbs.Length; i++)
        {
            var crumb = breadcrumbs[i];
            var url = ResolveAbsoluteUrl(baseUrl, crumb.Url);
            var name = System.Text.Json.JsonSerializer.Serialize(crumb.Title ?? string.Empty);
            var itemUrl = System.Text.Json.JsonSerializer.Serialize(url ?? string.Empty);
            var suffix = i == breadcrumbs.Length - 1 ? string.Empty : ",";
            lines.Add($"          {{ \"@type\": \"ListItem\", \"position\": {i + 1}, \"name\": {name}, \"item\": {itemUrl} }}{suffix}");
        }

        lines.Add("      ]");
        lines.Add("  }");
        lines.Add("  </script>");

        return string.Join(Environment.NewLine, lines);
    }

    private static string? ResolveMetaFilePath(ContentItem item, string rootPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        if (Path.IsPathRooted(filePath))
            return filePath;

        var baseDir = Path.GetDirectoryName(item.SourcePath);
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            var candidate = Path.Combine(baseDir, filePath);
            if (File.Exists(candidate))
                return candidate;
        }

        var rootCandidate = Path.Combine(rootPath, filePath);
        return rootCandidate;
    }

    private static string ResolveAbsoluteUrl(string? baseUrl, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return path;
        if (string.IsNullOrWhiteSpace(baseUrl)) return path;
        var trimmedBase = baseUrl.TrimEnd('/');
        var trimmedPath = path.StartsWith("/") ? path : "/" + path;
        return trimmedBase + trimmedPath;
    }

    private static string? ResolveThemeRoot(SiteSpec spec, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(spec.DefaultTheme)) return null;
        var basePath = ResolveThemesRoot(spec, rootPath);
        return string.IsNullOrWhiteSpace(basePath) ? null : Path.Combine(basePath, spec.DefaultTheme);
    }

    private static string? ResolveThemesRoot(SiteSpec spec, string rootPath)
    {
        var themesRoot = string.IsNullOrWhiteSpace(spec.ThemesRoot) ? "themes" : spec.ThemesRoot;
        return Path.IsPathRooted(themesRoot) ? themesRoot : Path.Combine(rootPath, themesRoot);
    }

    private static IEnumerable<(string Root, ThemeManifest Manifest)> BuildThemeChain(string themeRoot, ThemeManifest manifest)
    {
        var chain = new List<(string Root, ThemeManifest Manifest)>();
        var current = manifest;
        var currentRoot = themeRoot;
        while (current is not null)
        {
            chain.Add((currentRoot, current));
            if (current.Base is null || string.IsNullOrWhiteSpace(current.BaseRoot))
                break;
            currentRoot = current.BaseRoot;
            current = current.Base;
        }

        chain.Reverse();
        return chain;
    }

    private static IEnumerable<string> ResolveCssLinks(AssetRegistrySpec? assets, string route)
    {
        var bundles = ResolveBundles(assets, route);
        return bundles.SelectMany(b => b.Css).Distinct();
    }

    private static IEnumerable<string> ResolveJsLinks(AssetRegistrySpec? assets, string route)
    {
        var bundles = ResolveBundles(assets, route);
        return bundles.SelectMany(b => b.Js).Distinct();
    }

    private static IEnumerable<AssetBundleSpec> ResolveBundles(AssetRegistrySpec? assets, string route)
    {
        if (assets is null)
            return Array.Empty<AssetBundleSpec>();

        var bundleMap = assets.Bundles.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);
        var results = new List<AssetBundleSpec>();
        foreach (var mapping in assets.RouteBundles)
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

    private static AssetRegistrySpec? BuildAssetRegistry(
        SiteSpec spec,
        string? themeRoot,
        ThemeManifest? manifest)
    {
        AssetRegistrySpec? themeAssets = null;
        if (manifest is not null && !string.IsNullOrWhiteSpace(themeRoot))
        {
            foreach (var entry in BuildThemeChain(themeRoot, manifest))
            {
                if (entry.Manifest.Assets is null) continue;
                if (entry.Manifest.Base is not null &&
                    ReferenceEquals(entry.Manifest.Assets, entry.Manifest.Base.Assets))
                    continue;
                var themeName = string.IsNullOrWhiteSpace(entry.Manifest.Name)
                    ? Path.GetFileName(entry.Root)
                    : entry.Manifest.Name;
                if (string.IsNullOrWhiteSpace(themeName)) continue;
                var normalized = NormalizeThemeAssets(entry.Manifest.Assets, themeName, spec);
                themeAssets = MergeAssetRegistryForTheme(themeAssets, normalized);
            }
        }

        return MergeAssetRegistry(themeAssets, spec.AssetRegistry);
    }

    private static AssetRegistrySpec? MergeAssetRegistry(AssetRegistrySpec? baseAssets, AssetRegistrySpec? overrides)
    {
        if (baseAssets is null && overrides is null) return null;
        if (baseAssets is null) return CloneAssets(overrides);
        if (overrides is null) return CloneAssets(baseAssets);

        var bundleMap = baseAssets.Bundles.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in overrides.Bundles)
            bundleMap[bundle.Name] = CloneBundle(bundle);

        var routeBundles = overrides.RouteBundles.Length > 0 ? overrides.RouteBundles : baseAssets.RouteBundles;
        var preloads = overrides.Preloads.Length > 0 ? overrides.Preloads : baseAssets.Preloads;
        var criticalCss = overrides.CriticalCss.Length > 0 ? overrides.CriticalCss : baseAssets.CriticalCss;
        var cssStrategy = !string.IsNullOrWhiteSpace(overrides.CssStrategy) ? overrides.CssStrategy : baseAssets.CssStrategy;

        return new AssetRegistrySpec
        {
            Bundles = bundleMap.Values.ToArray(),
            RouteBundles = routeBundles.Select(r => new RouteBundleSpec
            {
                Match = r.Match,
                Bundles = r.Bundles.ToArray()
            }).ToArray(),
            Preloads = preloads.Select(p => new PreloadSpec
            {
                Href = p.Href,
                As = p.As,
                Type = p.Type,
                Crossorigin = p.Crossorigin
            }).ToArray(),
            CriticalCss = criticalCss.Select(c => new CriticalCssSpec
            {
                Name = c.Name,
                Path = c.Path
            }).ToArray(),
            CssStrategy = cssStrategy
        };
    }

    private static AssetRegistrySpec? MergeAssetRegistryForTheme(AssetRegistrySpec? baseAssets, AssetRegistrySpec? child)
    {
        if (baseAssets is null && child is null) return null;
        if (baseAssets is null) return CloneAssets(child);
        if (child is null) return CloneAssets(baseAssets);

        var bundleMap = baseAssets.Bundles.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in child.Bundles)
            bundleMap[bundle.Name] = CloneBundle(bundle);

        var criticalMap = baseAssets.CriticalCss.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var css in child.CriticalCss)
            criticalMap[css.Name] = new CriticalCssSpec { Name = css.Name, Path = css.Path };

        return new AssetRegistrySpec
        {
            Bundles = bundleMap.Values.ToArray(),
            RouteBundles = baseAssets.RouteBundles.Concat(child.RouteBundles)
                .Select(r => new RouteBundleSpec
                {
                    Match = r.Match,
                    Bundles = r.Bundles.ToArray()
                }).ToArray(),
            Preloads = baseAssets.Preloads.Concat(child.Preloads)
                .Select(p => new PreloadSpec
                {
                    Href = p.Href,
                    As = p.As,
                    Type = p.Type,
                    Crossorigin = p.Crossorigin
                }).ToArray(),
            CriticalCss = criticalMap.Values.ToArray(),
            CssStrategy = child.CssStrategy ?? baseAssets.CssStrategy
        };
    }

    private static AssetRegistrySpec? NormalizeThemeAssets(AssetRegistrySpec assets, string themeName, SiteSpec spec)
    {
        if (string.IsNullOrWhiteSpace(themeName)) return CloneAssets(assets);
        var themesRoot = ResolveThemesFolder(spec);
        var urlPrefix = "/" + themesRoot.TrimEnd('/', '\\') + "/" + themeName.Trim().Trim('/', '\\') + "/";
        var filePrefix = themesRoot.TrimEnd('/', '\\') + "/" + themeName.Trim().Trim('/', '\\') + "/";

        var normalized = CloneAssets(assets) ?? new AssetRegistrySpec();
        foreach (var bundle in normalized.Bundles)
        {
            bundle.Css = bundle.Css.Select(path => NormalizeAssetUrl(path, urlPrefix)).ToArray();
            bundle.Js = bundle.Js.Select(path => NormalizeAssetUrl(path, urlPrefix)).ToArray();
        }

        foreach (var preload in normalized.Preloads)
            preload.Href = NormalizeAssetUrl(preload.Href, urlPrefix);

        foreach (var css in normalized.CriticalCss)
        {
            if (!string.IsNullOrWhiteSpace(css.Path) && !Path.IsPathRooted(css.Path) && !IsExternalPath(css.Path))
                css.Path = filePrefix + css.Path.TrimStart('/', '\\');
        }

        return normalized;
    }

    private static string NormalizeAssetUrl(string path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (IsExternalPath(path) || path.StartsWith("/", StringComparison.Ordinal))
            return path;
        return prefix + path.TrimStart('/', '\\');
    }

    private static bool IsExternalPath(string path)
    {
        return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    private static AssetRegistrySpec? CloneAssets(AssetRegistrySpec? spec)
    {
        if (spec is null) return null;
        return new AssetRegistrySpec
        {
            Bundles = spec.Bundles.Select(CloneBundle).ToArray(),
            RouteBundles = spec.RouteBundles.Select(r => new RouteBundleSpec
            {
                Match = r.Match,
                Bundles = r.Bundles.ToArray()
            }).ToArray(),
            Preloads = spec.Preloads.Select(p => new PreloadSpec
            {
                Href = p.Href,
                As = p.As,
                Type = p.Type,
                Crossorigin = p.Crossorigin
            }).ToArray(),
            CriticalCss = spec.CriticalCss.Select(c => new CriticalCssSpec
            {
                Name = c.Name,
                Path = c.Path
            }).ToArray(),
            CssStrategy = spec.CssStrategy
        };
    }

    private static AssetBundleSpec CloneBundle(AssetBundleSpec bundle)
    {
        return new AssetBundleSpec
        {
            Name = bundle.Name,
            Css = bundle.Css.ToArray(),
            Js = bundle.Js.ToArray()
        };
    }

    private static string ResolveThemesFolder(SiteSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.ThemesRoot)) return "themes";
        if (Path.IsPathRooted(spec.ThemesRoot)) return "themes";
        return spec.ThemesRoot.Trim().TrimStart('/', '\\');
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
