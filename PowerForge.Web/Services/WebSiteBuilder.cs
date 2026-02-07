using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PowerForge.Web;

/// <summary>Builds a static site from configuration and content.</summary>
public static partial class WebSiteBuilder
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex TocHeaderRegex = new("<h(?<level>[2-3])[^>]*>(?<text>.*?)</h\\1>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex StripTagsRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex HrefRegex = new("href\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex CodeBlockRegex = new("<pre(?<preAttrs>[^>]*)>\\s*<code(?<codeAttrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ClassAttrRegex = new("class\\s*=\\s*\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    /// <summary>Builds the site output.</summary>
    /// <param name="spec">Site configuration.</param>
    /// <param name="plan">Resolved site plan.</param>
    /// <param name="outputPath">Output directory.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>Result payload describing the build output.</returns>
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
        var cacheRoot = ResolveCacheRoot(spec, plan.RootPath);
        var items = BuildContentItems(spec, plan, redirects, data, projectMap, projectContentMap, cacheRoot);
        items.AddRange(BuildTaxonomyItems(spec, items));
        var menuSpecs = BuildMenuSpecs(spec, items, plan.RootPath);
        foreach (var item in items)
        {
            WriteContentItem(outDir, spec, plan.RootPath, item, items, data, projectMap, menuSpecs);
        }

        CopyThemeAssets(spec, plan.RootPath, outDir);
        CopyStaticAssets(spec, plan.RootPath, outDir);
        WriteSiteNavData(spec, outDir, menuSpecs);
        WriteSearchIndex(outDir, items);
        WriteLinkCheckReport(spec, items, metaDir);

        var redirectsPayload = new
        {
            routeOverrides = spec.RouteOverrides,
            redirects = redirects
        };
        File.WriteAllText(redirectsPath, JsonSerializer.Serialize(redirectsPayload, jsonOptions));
        WriteRedirectOutputs(outDir, redirects);
        EnsureNoJekyllFile(outDir);

        return new WebBuildResult
        {
            OutputPath = outDir,
            PlanPath = planPath,
            SpecPath = specPath,
            RedirectsPath = redirectsPath,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    private static void EnsureNoJekyllFile(string outputRoot)
    {
        var markerPath = Path.Combine(outputRoot, ".nojekyll");
        if (!File.Exists(markerPath))
            File.WriteAllText(markerPath, string.Empty);
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
        NormalizeKnownData(data);

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
                value = NormalizeMarkdownData(value);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to load data file {file}: {ex.GetType().Name}: {ex.Message}");
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

    private static void NormalizeKnownData(Dictionary<string, object?> data)
    {
        if (data is null) return;

        NormalizeNamedData(data, "faq", NormalizeFaqData);
        NormalizeNamedData(data, "showcase", NormalizeShowcaseData);
        NormalizeNamedData(data, "benchmarks", NormalizeBenchmarksData);
        NormalizeNamedData(data, "pricing", NormalizePricingData);

        if (data.TryGetValue("projects", out var projectsObj) &&
            projectsObj is Dictionary<string, object?> projects)
        {
            foreach (var project in projects.Values)
            {
                if (project is Dictionary<string, object?> projectData)
                    NormalizeKnownData(projectData);
            }
        }
    }

    private static void NormalizeNamedData(
        Dictionary<string, object?> data,
        string key,
        Action<Dictionary<string, object?>> normalizer)
    {
        if (data.TryGetValue(key, out var value) && value is Dictionary<string, object?> map)
            normalizer(map);
    }

    private static void NormalizeFaqData(Dictionary<string, object?> faq)
    {
        if (!TryGetList(faq, "sections", out var sections))
            return;

        foreach (var sectionObj in sections)
        {
            if (sectionObj is not Dictionary<string, object?> section)
                continue;

            if (!TryGetList(section, "items", out var items))
                continue;

            foreach (var itemObj in items)
            {
                if (itemObj is not Dictionary<string, object?> item)
                    continue;

                CopyIfMissing(item, "question", item, "q", "title");
                CopyIfMissing(item, "answer", item, "a", "text", "summary");

                if (item.TryGetValue("answer", out var answer))
                    item["answer"] = NormalizeStringList(answer);
            }
        }
    }

    private static void NormalizeShowcaseData(Dictionary<string, object?> showcase)
    {
        if (!TryGetList(showcase, "cards", out var cards))
            return;

        foreach (var cardObj in cards)
        {
            if (cardObj is not Dictionary<string, object?> card)
                continue;

            CopyIfMissing(card, "icon_svg", card, "iconSvg", "iconHtml");

            if (TryGetList(card, "meta", out var metaItems))
            {
                foreach (var metaObj in metaItems)
                {
                    if (metaObj is not Dictionary<string, object?> meta)
                        continue;
                    CopyIfMissing(meta, "icon_svg", meta, "iconSvg", "iconHtml");
                }
            }

            if (card.TryGetValue("details", out var detailsObj) && detailsObj is Dictionary<string, object?> details)
            {
                if (details.TryGetValue("items", out var items))
                    details["items"] = NormalizeStringList(items);
            }

            if (card.TryGetValue("features", out var features))
                card["features"] = NormalizeStringList(features);

            if (card.TryGetValue("gallery", out var galleryObj) && galleryObj is Dictionary<string, object?> gallery)
            {
                if (TryGetList(gallery, "themes", out var themes))
                {
                    foreach (var themeObj in themes)
                    {
                        if (themeObj is not Dictionary<string, object?> theme)
                            continue;
                        if (!TryGetList(theme, "slides", out var slides))
                            continue;
                        foreach (var slideObj in slides)
                        {
                            if (slideObj is not Dictionary<string, object?> slide)
                                continue;
                            CopyIfMissing(slide, "thumb_label", slide, "thumbLabel");
                            CopyIfMissing(slide, "thumb_src", slide, "thumbSrc");
                        }
                    }
                }
            }

            if (TryGetList(card, "actions", out var actions))
            {
                foreach (var actionObj in actions)
                {
                    if (actionObj is not Dictionary<string, object?> action)
                        continue;
                    CopyIfMissing(action, "icon_svg", action, "iconSvg", "iconHtml");
                }
            }

            if (card.TryGetValue("status", out var statusObj) && statusObj is Dictionary<string, object?> status)
            {
                CopyIfMissing(status, "dot_style", status, "dotStyle");
            }
        }
    }

    private static void NormalizeBenchmarksData(Dictionary<string, object?> benchmarks)
    {
        if (benchmarks.TryGetValue("about", out var aboutObj) && aboutObj is Dictionary<string, object?> about)
        {
            if (TryGetList(about, "cards", out var cards))
            {
                foreach (var cardObj in cards)
                {
                    if (cardObj is not Dictionary<string, object?> card)
                        continue;

                    if (card.TryGetValue("paragraphs", out var paragraphs))
                        card["paragraphs"] = NormalizeStringList(paragraphs);
                    if (card.TryGetValue("list", out var list))
                        card["list"] = NormalizeStringList(list);
                }
            }
        }

        if (benchmarks.TryGetValue("notes", out var notesObj) && notesObj is Dictionary<string, object?> notes)
        {
            if (notes.TryGetValue("items", out var items))
                notes["items"] = NormalizeStringList(items);
        }
    }

    private static void NormalizePricingData(Dictionary<string, object?> pricing)
    {
        if (TryGetList(pricing, "cards", out var cards))
        {
            foreach (var cardObj in cards)
            {
                if (cardObj is not Dictionary<string, object?> card)
                    continue;

                CopyIfMissing(card, "icon_svg", card, "iconSvg", "iconHtml");
                CopyIfMissing(card, "icon_class", card, "iconClass");
                CopyIfMissing(card, "amount_class", card, "amountClass");

                if (card.TryGetValue("features", out var features))
                    card["features"] = NormalizeStringList(features);

                if (card.TryGetValue("cta", out var ctaObj) && ctaObj is Dictionary<string, object?> cta)
                {
                    CopyIfMissing(cta, "icon_svg", cta, "iconSvg", "iconHtml");
                }
                else
                {
                    var ctaLabel = ReadString(card, "cta_label", "ctaLabel");
                    var ctaHref = ReadString(card, "cta_href", "ctaHref");
                    if (!string.IsNullOrWhiteSpace(ctaLabel) || !string.IsNullOrWhiteSpace(ctaHref))
                    {
                        card["cta"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["label"] = ctaLabel ?? string.Empty,
                            ["href"] = ctaHref ?? string.Empty
                        };
                    }
                }
            }
        }

        if (pricing.TryGetValue("note", out var noteObj) && noteObj is Dictionary<string, object?> note)
        {
            if (note.TryGetValue("paragraphs", out var paragraphs))
                note["paragraphs"] = NormalizeStringList(paragraphs);
        }
    }

    private static void CopyIfMissing(
        Dictionary<string, object?> target,
        string targetKey,
        Dictionary<string, object?> source,
        params string[] sourceKeys)
    {
        if (target.ContainsKey(targetKey))
            return;

        foreach (var sourceKey in sourceKeys)
        {
            if (source.TryGetValue(sourceKey, out var value) && value is not null)
            {
                target[targetKey] = value;
                return;
            }
        }
    }

    private static bool TryGetList(Dictionary<string, object?> map, string key, out List<object?> list)
    {
        list = new List<object?>();
        if (!map.TryGetValue(key, out var value) || value is null)
            return false;

        if (value is List<object?> listValue)
        {
            list = listValue;
            return true;
        }

        if (value is IEnumerable<object?> enumerable && value is not string)
        {
            list = enumerable.ToList();
            map[key] = list;
            return true;
        }

        return false;
    }

    private static object? NormalizeStringList(object? value)
    {
        if (value is null) return null;
        if (value is List<object?> list)
            return list;
        if (value is IEnumerable<object?> enumerable && value is not string)
            return enumerable.ToList();
        if (value is string text)
            return new List<object?> { text };
        return value;
    }

    private static string? ReadString(Dictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (map.TryGetValue(key, out var value) && value is not null)
                return value.ToString();
        }
        return null;
    }

    private static object? NormalizeMarkdownData(object? value)
    {
        if (value is Dictionary<string, object?> map)
        {
            foreach (var key in map.Keys.ToList())
            {
                map[key] = NormalizeMarkdownData(map[key]);
            }

            foreach (var key in map.Keys.ToList())
            {
                if (!IsMarkdownKey(key))
                    continue;

                var baseKey = StripMarkdownSuffix(key);
                if (string.IsNullOrWhiteSpace(baseKey))
                    continue;

                if (!map.ContainsKey(baseKey) || map[baseKey] is null)
                    map[baseKey] = RenderMarkdownValue(map[key]);
            }

            return map;
        }

        if (value is List<object?> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                list[i] = NormalizeMarkdownData(list[i]);
            }
            return list;
        }

        return value;
    }

    private static bool IsMarkdownKey(string key)
        => key.EndsWith("_md", StringComparison.OrdinalIgnoreCase) ||
           key.EndsWith("_markdown", StringComparison.OrdinalIgnoreCase);

    private static string StripMarkdownSuffix(string key)
    {
        if (key.EndsWith("_markdown", StringComparison.OrdinalIgnoreCase))
            return key[..^9];
        if (key.EndsWith("_md", StringComparison.OrdinalIgnoreCase))
            return key[..^3];
        return key;
    }

    private static object? RenderMarkdownValue(object? value)
    {
        if (value is null) return null;

        if (value is string text)
            return MarkdownRenderer.RenderToHtml(text);

        if (value is IEnumerable<object?> list && value is not string)
        {
            var rendered = new List<object?>();
            foreach (var item in list)
            {
                if (item is string itemText)
                    rendered.Add(MarkdownRenderer.RenderToHtml(itemText));
                else
                    rendered.Add(item);
            }
            return rendered;
        }

        return value;
    }

    private static string BuildTableOfContents(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var matches = TocHeaderRegex.Matches(html);
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
        return StripTagsRegex.Replace(input, string.Empty);
    }

    private static void WriteSearchIndex(string outputRoot, IReadOnlyList<ContentItem> items)
    {
        if (items.Count == 0) return;

        var entries = new List<SearchIndexEntry>();
        foreach (var item in items)
        {
            if (item.Draft) continue;
            if (item.Kind != PageKind.Page && item.Kind != PageKind.Home)
                continue;

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

    private static void WriteLinkCheckReport(SiteSpec spec, IReadOnlyList<ContentItem> items, string metaDir)
    {
        if (spec.LinkCheck?.Enabled != true)
            return;

        var errors = new List<Dictionary<string, string>>();
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item.Draft) continue;
            if (item.Kind != PageKind.Page && item.Kind != PageKind.Home)
                continue;
            routes.Add(NormalizeRouteForMatch(item.OutputPath));
        }

        var skipPatterns = spec.LinkCheck.Skip ?? Array.Empty<string>();
        foreach (var item in items)
        {
            if (item.Draft) continue;
            if (item.Kind != PageKind.Page && item.Kind != PageKind.Home)
                continue;

            foreach (var href in ExtractLinks(item.HtmlContent))
            {
                if (string.IsNullOrWhiteSpace(href)) continue;
                if (ShouldSkipLink(href, skipPatterns)) continue;
                if (IsExternalUrl(href) && spec.LinkCheck.IncludeExternal != true) continue;
                if (href.StartsWith("#")) continue;
                if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;
                if (href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) continue;
                if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;

                var normalized = ResolveLinkTarget(item.OutputPath, href);
                if (string.IsNullOrWhiteSpace(normalized)) continue;
                if (!routes.Contains(normalized))
                {
                    errors.Add(new Dictionary<string, string>
                    {
                        ["page"] = item.OutputPath,
                        ["href"] = href,
                        ["target"] = normalized
                    });
                }
            }
        }

        var payload = new Dictionary<string, object?>
        {
            ["checkedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["errorCount"] = errors.Count,
            ["errors"] = errors
        };

        var path = Path.Combine(metaDir, "linkcheck.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload, WebJson.Options));
    }

    private static IEnumerable<string> ExtractLinks(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            yield break;

        var matches = HrefRegex.Matches(html);
        foreach (Match match in matches)
        {
            if (match.Groups.Count < 2) continue;
            yield return match.Groups[1].Value;
        }
    }

    private static string ResolveLinkTarget(string pagePath, string href)
    {
        var cleaned = href;
        var hashIndex = cleaned.IndexOf('#');
        if (hashIndex >= 0)
            cleaned = cleaned.Substring(0, hashIndex);
        var queryIndex = cleaned.IndexOf('?');
        if (queryIndex >= 0)
            cleaned = cleaned.Substring(0, queryIndex);

        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        if (cleaned.StartsWith("/"))
            return NormalizeRouteForMatch(cleaned);

        var baseUri = new Uri("http://local" + NormalizeRouteForMatch(pagePath));
        var target = new Uri(baseUri, cleaned);
        return NormalizeRouteForMatch(target.AbsolutePath);
    }

    private static bool ShouldSkipLink(string href, string[] patterns)
    {
        if (patterns.Length == 0) return false;
        foreach (var pattern in patterns)
        {
            if (GlobMatch(pattern, href))
                return true;
        }
        return false;
    }

    private static string BuildSnippet(string html, int maxLength)
    {
        var text = StripTags(html);
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = WhitespaceRegex.Replace(text, " ").Trim();
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
            catch (Exception ex)
            {
                Trace.TraceWarning($"Invalid project spec {projectFile}: {ex.GetType().Name}: {ex.Message}");
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
        IReadOnlyDictionary<string, ProjectContentSpec> projectContentMap,
        string? cacheRoot)
    {
        var items = new List<ContentItem>();
        if (spec.Collections is null || spec.Collections.Length == 0)
            return items;

        var contentRoots = BuildContentRoots(plan);

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

            var markdownFiles = EnumerateCollectionFiles(plan.RootPath, contentRoots, collection.Input, include, exclude).ToList();
            var leafBundleRoots = BuildLeafBundleRoots(markdownFiles);

            foreach (var file in markdownFiles)
            {
                if (IsUnderAnyRoot(file, leafBundleRoots) && !IsLeafBundleIndex(file))
                    continue;

                var markdown = File.ReadAllText(file);
                var (matter, body) = FrontMatterParser.Parse(markdown);
                var effectiveBody = IncludePreprocessor.Apply(body, plan.RootPath);
                var projectSlug = ResolveProjectSlug(plan, file);
                projectMap.TryGetValue(projectSlug ?? string.Empty, out var projectSpec);
                var editUrl = EditLinkResolver.Resolve(spec, projectSpec, plan.RootPath, file, matter?.EditPath);
                if (matter is not null)
                    matter.EditUrl = editUrl;
                var dataForShortcodes = ResolveDataForProject(data, projectSlug);
                var shortcodeContext = new ShortcodeRenderContext
                {
                    Site = spec,
                    RootPath = plan.RootPath,
                    FrontMatter = matter,
                    EditUrl = editUrl,
                    Project = projectSpec,
                    SourcePath = file,
                    Data = dataForShortcodes,
                    ThemeManifest = manifest,
                    ThemeRoot = themeRoot,
                    Engine = shortcodeEngine,
                    PartialResolver = partialResolver
                };
                var processedBody = ShortcodeProcessor.Apply(effectiveBody, shortcodeContext);
                var skipMarkdown = false;
                if (TryRenderDataOverride(matter?.Meta, shortcodeContext, out var dataOverride, out var dataMode))
                {
                    var mode = NormalizeDataRenderMode(dataMode);
                    if (mode == DataRenderMode.Append)
                    {
                        processedBody = string.IsNullOrWhiteSpace(processedBody)
                            ? dataOverride
                            : $"{processedBody}{Environment.NewLine}{dataOverride}";
                    }
                    else if (mode == DataRenderMode.Prepend)
                    {
                        processedBody = string.IsNullOrWhiteSpace(processedBody)
                            ? dataOverride
                            : $"{dataOverride}{Environment.NewLine}{processedBody}";
                    }
                    else
                    {
                        processedBody = dataOverride;
                        skipMarkdown = true;
                    }
                }

                processedBody = ApplyContentTemplate(processedBody, matter?.Meta, shortcodeContext);

                var title = matter?.Title ?? FrontMatterParser.ExtractTitleFromMarkdown(processedBody) ?? Path.GetFileNameWithoutExtension(file);
                var description = matter?.Description ?? string.Empty;
                var collectionRoot = ResolveCollectionRootForFile(plan.RootPath, contentRoots, collection.Input, file);
                var relativePath = ResolveRelativePath(collectionRoot, file);
                var resolvedLanguage = ResolveItemLanguage(spec, relativePath, matter, out var localizedRelativePath, out var localizedRelativeDir);
                var relativeDir = localizedRelativeDir;
                var isSectionIndex = IsSectionIndex(file);
                var isBundleIndex = IsLeafBundleIndex(file);
                var slugPath = ResolveSlugPath(localizedRelativePath, relativeDir, matter?.Slug);
                if (isSectionIndex || isBundleIndex)
                    slugPath = ApplySlugOverride(relativeDir, matter?.Slug);
                var baseOutput = ReplaceProjectPlaceholder(collection.Output, projectSlug);
                var route = BuildRoute(baseOutput, slugPath, spec.TrailingSlash);
                route = ApplyLanguagePrefixToRoute(spec, route, resolvedLanguage);
                var kind = ResolvePageKind(route, collection, isSectionIndex);
                var layout = matter?.Layout;
                if (string.IsNullOrWhiteSpace(layout))
                {
                    layout = kind == PageKind.Section
                        ? (string.IsNullOrWhiteSpace(collection.ListLayout) ? collection.DefaultLayout : collection.ListLayout)
                        : collection.DefaultLayout;
                }

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

                var renderMarkdown = ShouldRenderMarkdown(matter?.Meta);
                if (skipMarkdown)
                    renderMarkdown = false;
                var htmlContent = renderMarkdown
                    ? RenderMarkdown(processedBody, file, spec.Cache, cacheRoot)
                    : processedBody;
                var meta = matter?.Meta ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                htmlContent = NormalizeCodeBlockClasses(htmlContent, ResolvePrismDefaultLanguage(meta, spec));
                EnsurePrismAssets(meta, htmlContent, spec, plan.RootPath);
                var toc = BuildTableOfContents(htmlContent);
                if (collection.Name.Equals("projects", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(projectSlug) &&
                    projectContentMap.TryGetValue(projectSlug, out var perProject))
                {
                    var projectInclude = perProject.Include;
                    var projectExclude = perProject.Exclude;
                    if (!MatchesFile(plan.RootPath, contentRoots, file, collection.Input, projectInclude, projectExclude))
                        continue;
                }
                items.Add(new ContentItem
                {
                    SourcePath = file,
                    Collection = collection.Name,
                    OutputPath = route,
                    Language = resolvedLanguage,
                    TranslationKey = ResolveTranslationKey(matter, collection.Name, localizedRelativePath),
                    Title = title,
                    Description = description,
                    Date = matter?.Date,
                    Order = matter?.Order,
                    Slug = slugPath,
                    Tags = matter?.Tags ?? Array.Empty<string>(),
                    Aliases = matter?.Aliases ?? Array.Empty<string>(),
                    Draft = matter?.Draft ?? false,
                    Canonical = matter?.Canonical,
                    EditPath = matter?.EditPath,
                    EditUrl = editUrl,
                    Layout = layout,
                    Template = matter?.Template,
                    Kind = kind,
                    HtmlContent = htmlContent,
                    TocHtml = toc,
                    Resources = isSectionIndex || isBundleIndex
                        ? BuildBundleResources(Path.GetDirectoryName(file) ?? string.Empty)
                        : Array.Empty<PageResource>(),
                    ProjectSlug = projectSlug,
                    Meta = meta,
                    Outputs = ResolveOutputs(matter?.Meta, collection)
                });
            }
        }

        return items;
    }

    private static List<ContentItem> BuildTaxonomyItems(SiteSpec spec, IReadOnlyList<ContentItem> items)
    {
        var results = new List<ContentItem>();
        if (spec.Taxonomies is null || spec.Taxonomies.Length == 0)
            return results;

        var localization = ResolveLocalizationConfig(spec);
        var sourceItems = items
            .Where(i => i.Kind == PageKind.Page || i.Kind == PageKind.Home)
            .Where(i => !i.Draft)
            .ToList();

        foreach (var taxonomy in spec.Taxonomies)
        {
            if (taxonomy is null || string.IsNullOrWhiteSpace(taxonomy.Name) || string.IsNullOrWhiteSpace(taxonomy.BasePath))
                continue;

            var termMap = new Dictionary<string, Dictionary<string, List<ContentItem>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in sourceItems)
            {
                var language = ResolveEffectiveLanguageCode(localization, item.Language);
                if (!termMap.TryGetValue(language, out var languageMap))
                {
                    languageMap = new Dictionary<string, List<ContentItem>>(StringComparer.OrdinalIgnoreCase);
                    termMap[language] = languageMap;
                }

                foreach (var term in GetTaxonomyValues(item, taxonomy))
                {
                    if (!languageMap.TryGetValue(term, out var list))
                    {
                        list = new List<ContentItem>();
                        languageMap[term] = list;
                    }
                    list.Add(item);
                }
            }

            foreach (var languageEntry in termMap)
            {
                var language = languageEntry.Key;
                var languageTerms = languageEntry.Value;
                if (languageTerms.Count == 0)
                    continue;

                var taxRoute = BuildRoute(taxonomy.BasePath, string.Empty, spec.TrailingSlash);
                taxRoute = ApplyLanguagePrefixToRoute(spec, taxRoute, language);
                results.Add(new ContentItem
                {
                    Collection = taxonomy.Name,
                    OutputPath = taxRoute,
                    Language = language,
                    TranslationKey = $"taxonomy:{taxonomy.Name}",
                    Title = HumanizeSegment(taxonomy.Name),
                    Description = string.Empty,
                    Kind = PageKind.Taxonomy,
                    Layout = taxonomy.ListLayout ?? "taxonomy",
                    Meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["taxonomy"] = taxonomy.Name
                    }
                });

                foreach (var term in languageTerms.Keys.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                {
                    var slug = Slugify(term);
                    var termRoute = BuildRoute(taxonomy.BasePath, slug, spec.TrailingSlash);
                    termRoute = ApplyLanguagePrefixToRoute(spec, termRoute, language);
                    results.Add(new ContentItem
                    {
                        Collection = taxonomy.Name,
                        OutputPath = termRoute,
                        Language = language,
                        TranslationKey = $"taxonomy:{taxonomy.Name}:term:{slug}",
                        Title = term,
                        Description = string.Empty,
                        Kind = PageKind.Term,
                        Layout = taxonomy.TermLayout ?? "term",
                        Meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["taxonomy"] = taxonomy.Name,
                            ["term"] = term
                        }
                    });
                }
            }
        }

        return results;
    }

    private static IEnumerable<string> GetTaxonomyValues(ContentItem item, TaxonomySpec taxonomy)
    {
        if (taxonomy.Name.Equals("tags", StringComparison.OrdinalIgnoreCase))
            return item.Tags ?? Array.Empty<string>();

        if (item.Meta is not null && TryGetMetaValue(item.Meta, taxonomy.Name, out var value))
        {
            if (value is IEnumerable<object?> list)
                return list.Select(v => v?.ToString() ?? string.Empty)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray();

            if (value is string s && !string.IsNullOrWhiteSpace(s))
                return new[] { s };
        }

        return Array.Empty<string>();
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
    {
        return Path.GetFileName(filePath).Equals("index.md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSectionIndex(string filePath)
    {
        return Path.GetFileName(filePath).Equals("_index.md", StringComparison.OrdinalIgnoreCase);
    }

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

    private static string[] BuildContentRoots(WebSitePlan plan)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.ContentRoot))
            roots.Add(Path.GetFullPath(plan.ContentRoot));

        if (plan.ContentRoots is { Length: > 0 })
        {
            roots.AddRange(plan.ContentRoots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath));
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private static string? ResolveCollectionRootForFile(string rootPath, string[] contentRoots, string input, string filePath)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        foreach (var full in BuildCollectionInputCandidates(rootPath, contentRoots, input))
        {
            if (!full.Contains('*', StringComparison.Ordinal))
            {
                var candidateRoot = Path.GetFullPath(full);
                if (IsPathWithinBase(candidateRoot, filePath))
                    return candidateRoot;
                continue;
            }

            var normalized = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var parts = normalized.Split('*');
            if (parts.Length != 2)
                continue;

            var basePath = parts[0].TrimEnd(Path.DirectorySeparatorChar);
            var tail = parts[1].TrimStart(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(tail))
                continue;

            if (!IsPathWithinBase(basePath, filePath))
                continue;

            var relative = Path.GetRelativePath(basePath, filePath);
            var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;

            var wildcardSegment = segments[0];
            var candidate = Path.Combine(basePath, wildcardSegment, tail);
            return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static string ReplaceProjectPlaceholder(string output, string? projectSlug)
    {
        if (string.IsNullOrWhiteSpace(output))
            return output;
        if (string.IsNullOrWhiteSpace(projectSlug))
            return output.Replace("{project}", string.Empty, StringComparison.OrdinalIgnoreCase);
        return output.Replace("{project}", projectSlug, StringComparison.OrdinalIgnoreCase);
    }

    private static PageKind ResolvePageKind(string route, CollectionSpec collection, bool isSectionIndex)
    {
        if (isSectionIndex) return PageKind.Section;
        if (string.Equals(route, "/", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(collection.Output, "/", StringComparison.OrdinalIgnoreCase))
            return PageKind.Home;
        return PageKind.Page;
    }

    private static PageResource[] BuildBundleResources(string bundleRoot)
    {
        if (string.IsNullOrWhiteSpace(bundleRoot) || !Directory.Exists(bundleRoot))
            return Array.Empty<PageResource>();

        var resources = new List<PageResource>();
        foreach (var file in Directory.EnumerateFiles(bundleRoot, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = Path.GetRelativePath(bundleRoot, file).Replace('\\', '/');
            resources.Add(new PageResource
            {
                SourcePath = file,
                Name = Path.GetFileName(file),
                RelativePath = relative,
                MediaType = ResolveMediaType(file)
            });
        }

        return resources.ToArray();
    }

    private static string? ResolveMediaType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html",
            ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".json" => "application/json",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".ico" => "image/x-icon",
            _ => null
        };
    }

    private static string[] ResolveOutputs(Dictionary<string, object?>? meta, CollectionSpec collection)
    {
        var outputs = TryGetMetaStringList(meta, "outputs");
        if (outputs.Length > 0)
            return outputs;
        if (collection.Outputs.Length > 0)
            return collection.Outputs;
        return Array.Empty<string>();
    }

    private static string[] TryGetMetaStringList(Dictionary<string, object?>? meta, string key)
    {
        if (meta is null || meta.Count == 0) return Array.Empty<string>();
        if (!TryGetMetaValue(meta, key, out var value) || value is null)
            return Array.Empty<string>();

        if (value is IEnumerable<object?> list)
        {
            return list.Select(v => v?.ToString() ?? string.Empty)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
        }

        if (value is string s && !string.IsNullOrWhiteSpace(s))
            return new[] { s };

        return Array.Empty<string>();
    }

    private static string? ResolveCacheRoot(SiteSpec spec, string rootPath)
    {
        if (spec.Cache?.Enabled != true)
            return null;

        var root = spec.Cache.Root;
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(rootPath, ".cache", "powerforge-web");

        var full = Path.IsPathRooted(root) ? root : Path.Combine(rootPath, root);
        Directory.CreateDirectory(full);
        return full;
    }

    private static string RenderMarkdown(string content, string sourcePath, BuildCacheSpec? cache, string? cacheRoot)
    {
        if (cache?.Enabled != true || string.IsNullOrWhiteSpace(cacheRoot))
            return MarkdownRenderer.RenderToHtml(content);

        var key = ComputeCacheKey(content, sourcePath, cache);
        var cacheFile = Path.Combine(cacheRoot, key + ".html");
        if (File.Exists(cacheFile))
            return File.ReadAllText(cacheFile);

        var html = MarkdownRenderer.RenderToHtml(content);
        File.WriteAllText(cacheFile, html);
        return html;
    }

    private static void EnsurePrismAssets(Dictionary<string, object?> meta, string htmlContent, SiteSpec spec, string rootPath)
    {
        if (meta is null) return;

        if (TryGetMetaBool(meta, "prism", out var prismEnabled) && !prismEnabled)
            return;

        var prismSpec = spec.Prism;
        var mode = GetMetaStringOrNull(meta, "prism_mode") ?? prismSpec?.Mode ?? "auto";
        if (mode.Equals("off", StringComparison.OrdinalIgnoreCase))
            return;

        var hasCode = ContainsMarkdownCode(htmlContent);
        var always = mode.Equals("always", StringComparison.OrdinalIgnoreCase);
        if (!hasCode && !always && !(prismEnabled))
            return;

        if (MetaContains(meta, "extra_css", "prism") || MetaContains(meta, "extra_scripts", "prism"))
            return;

        var sourceOverride = GetMetaStringOrNull(meta, "prism_source");
        var source = sourceOverride
            ?? prismSpec?.Source
            ?? spec.AssetPolicy?.Mode
            ?? "cdn";

        var localAssets = ResolvePrismLocalAssets(meta, prismSpec);
        var localExists = LocalPrismAssetsExist(localAssets, rootPath);
        var sourceIsLocal = source.Equals("local", StringComparison.OrdinalIgnoreCase);
        var sourceIsHybrid = source.Equals("hybrid", StringComparison.OrdinalIgnoreCase);
        var useLocal = (sourceIsLocal && localExists) || (sourceIsHybrid && localExists);
        if (sourceIsLocal && !localExists)
            Trace.TraceWarning("Prism source is set to local, but local assets are missing. Falling back to CDN.");

        var css = useLocal
            ? string.Join(Environment.NewLine, new[]
            {
                $"<link rel=\"stylesheet\" href=\"{localAssets.light}\" media=\"(prefers-color-scheme: light)\" />",
                $"<link rel=\"stylesheet\" href=\"{localAssets.dark}\" media=\"(prefers-color-scheme: dark)\" />"
            })
            : BuildPrismCdnCss(meta, prismSpec);

        var scripts = useLocal
            ? string.Join(Environment.NewLine, new[]
            {
                $"<script src=\"{localAssets.core}\"></script>",
                $"<script src=\"{localAssets.autoloader}\"></script>",
                BuildPrismInitScript(localAssets.langPath)
            })
            : BuildPrismCdnScripts(meta, prismSpec);

        AppendMetaHtml(meta, "extra_css", css);
        AppendMetaHtml(meta, "extra_scripts", scripts);
    }

    private static (string light, string dark, string core, string autoloader, string langPath) ResolvePrismLocalAssets(
        Dictionary<string, object?> meta,
        PrismSpec? prismSpec)
    {
        var local = prismSpec?.Local;
        var lightOverride = GetMetaStringOrNull(meta, "prism_css_light") ?? local?.ThemeLight ?? prismSpec?.ThemeLight;
        var darkOverride = GetMetaStringOrNull(meta, "prism_css_dark") ?? local?.ThemeDark ?? prismSpec?.ThemeDark;
        var light = ResolvePrismThemeHref(lightOverride, isCdn: false, cdnBase: null, defaultCdnName: "prism", defaultLocalPath: "/assets/prism/prism.css");
        var dark = ResolvePrismThemeHref(darkOverride, isCdn: false, cdnBase: null, defaultCdnName: "prism-okaidia", defaultLocalPath: "/assets/prism/prism-okaidia.css");
        var core = GetMetaStringOrNull(meta, "prism_core") ?? local?.Core ?? "/assets/prism/prism-core.js";
        var autoloader = GetMetaStringOrNull(meta, "prism_autoloader") ?? local?.Autoloader ?? "/assets/prism/prism-autoloader.js";
        var langPath = GetMetaStringOrNull(meta, "prism_lang_path") ?? local?.LanguagesPath ?? "/assets/prism/components/";
        return (light, dark, core, autoloader, langPath);
    }

    private static bool LocalPrismAssetsExist((string light, string dark, string core, string autoloader, string langPath) assets, string rootPath)
    {
        var paths = new[] { assets.light, assets.dark, assets.core, assets.autoloader };
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var full = ResolveLocalAssetPath(path, rootPath);
            if (string.IsNullOrWhiteSpace(full) || !File.Exists(full))
                return false;
        }
        return true;
    }

    private static string? ResolveLocalAssetPath(string href, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        var trimmed = href.TrimStart('/');
        var relative = trimmed.Replace('/', Path.DirectorySeparatorChar);
        var primary = Path.Combine(rootPath, relative);
        if (File.Exists(primary))
            return primary;
        var staticPath = Path.Combine(rootPath, "static", relative);
        return File.Exists(staticPath) ? staticPath : primary;
    }

    private static string BuildPrismCdnCss(Dictionary<string, object?> meta, PrismSpec? prismSpec)
    {
        var cdn = GetMetaStringOrNull(meta, "prism_cdn") ?? prismSpec?.CdnBase ?? "https://cdn.jsdelivr.net/npm/prismjs@1.29.0";
        cdn = cdn.TrimEnd('/');
        var lightOverride = GetMetaStringOrNull(meta, "prism_css_light") ?? prismSpec?.ThemeLight;
        var darkOverride = GetMetaStringOrNull(meta, "prism_css_dark") ?? prismSpec?.ThemeDark;
        var light = ResolvePrismThemeHref(lightOverride, isCdn: true, cdnBase: cdn, defaultCdnName: "prism", defaultLocalPath: "/assets/prism/prism.css");
        var dark = ResolvePrismThemeHref(darkOverride, isCdn: true, cdnBase: cdn, defaultCdnName: "prism-okaidia", defaultLocalPath: "/assets/prism/prism-okaidia.css");
        return string.Join(Environment.NewLine, new[]
        {
            $"<link rel=\"stylesheet\" href=\"{light}\" media=\"(prefers-color-scheme: light)\" />",
            $"<link rel=\"stylesheet\" href=\"{dark}\" media=\"(prefers-color-scheme: dark)\" />"
        });
    }

    private static string BuildPrismCdnScripts(Dictionary<string, object?> meta, PrismSpec? prismSpec)
    {
        var cdn = GetMetaStringOrNull(meta, "prism_cdn") ?? prismSpec?.CdnBase ?? "https://cdn.jsdelivr.net/npm/prismjs@1.29.0";
        cdn = cdn.TrimEnd('/');
        return string.Join(Environment.NewLine, new[]
        {
            $"<script src=\"{cdn}/components/prism-core.min.js\"></script>",
            $"<script src=\"{cdn}/plugins/autoloader/prism-autoloader.min.js\"></script>",
            BuildPrismInitScript($"{cdn}/components/")
        });
    }

    private static string BuildPrismInitScript(string languagesPath)
    {
        var safePath = (languagesPath ?? string.Empty).Replace("'", "\\'");
        return
            "<script>(function(){" +
            "var p=window.Prism;" +
            "if(!p){return;}" +
            "if(p.plugins&&p.plugins.autoloader){p.plugins.autoloader.languages_path='" + safePath + "';}" +
            "var run=function(){if(!document.querySelector('code[class*=\\\"language-\\\"] .token')){p.highlightAll();}};" +
            "if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded', run);}" +
            "else{run();}" +
            "})();</script>";
    }

    private static string ResolvePrismThemeHref(
        string? value,
        bool isCdn,
        string? cdnBase,
        string defaultCdnName,
        string defaultLocalPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!isCdn)
                return defaultLocalPath;
            var cdn = (cdnBase ?? string.Empty).TrimEnd('/');
            return $"{cdn}/themes/{defaultCdnName}.min.css";
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.StartsWith("/"))
            return trimmed;

        if (trimmed.Contains("/"))
            return "/" + trimmed.TrimStart('/');

        if (!isCdn)
        {
            if (trimmed.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                return "/" + trimmed.TrimStart('/');
            return "/assets/prism/prism-" + trimmed + ".css";
        }

        var name = trimmed.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring(0, trimmed.Length - 4)
            : trimmed;
        var cdnRoot = (cdnBase ?? string.Empty).TrimEnd('/');
        return $"{cdnRoot}/themes/{name}.min.css";
    }

    private static bool ContainsMarkdownCode(string htmlContent)
    {
        if (string.IsNullOrWhiteSpace(htmlContent)) return false;
        return htmlContent.IndexOf("class=\"language-", StringComparison.OrdinalIgnoreCase) >= 0 ||
               htmlContent.IndexOf("class=language-", StringComparison.OrdinalIgnoreCase) >= 0 ||
               htmlContent.IndexOf("<pre><code", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeCodeBlockClasses(string htmlContent, string defaultLanguageClass)
    {
        if (string.IsNullOrWhiteSpace(htmlContent)) return htmlContent;

        return CodeBlockRegex.Replace(
            htmlContent,
            match =>
            {
                var preAttrs = match.Groups["preAttrs"].Value;
                var codeAttrs = match.Groups["codeAttrs"].Value;

                var updatedPre = EnsureClass(preAttrs, "code-block");
                var updatedCode = EnsureLanguageClass(codeAttrs, defaultLanguageClass);

                return $"<pre{updatedPre}><code{updatedCode}>";
            });
    }

    private static string EnsureLanguageClass(string attrs, string defaultLanguageClass)
    {
        if (string.IsNullOrWhiteSpace(attrs))
            return $" class=\"{defaultLanguageClass}\"";

        var classMatch = ClassAttrRegex.Match(attrs);
        if (!classMatch.Success)
            return attrs + $" class=\"{defaultLanguageClass}\"";

        var classes = classMatch.Groups["value"].Value;
        if (classes.IndexOf("language-", StringComparison.OrdinalIgnoreCase) >= 0)
            return attrs;

        var replacement = classMatch.Value.Replace(classes, (classes + " " + defaultLanguageClass).Trim());
        return attrs.Replace(classMatch.Value, replacement);
    }

    private static string ResolvePrismDefaultLanguage(Dictionary<string, object?>? meta, SiteSpec spec)
    {
        var metaLanguage = meta is null ? null : GetMetaStringOrNull(meta, "prism_default_language");
        var configured = metaLanguage ?? spec.Prism?.DefaultLanguage;
        if (string.IsNullOrWhiteSpace(configured))
            return "language-plain";
        return configured.StartsWith("language-", StringComparison.OrdinalIgnoreCase)
            ? configured
            : $"language-{configured}";
    }

    private static string EnsureClass(string attrs, string className)
    {
        if (string.IsNullOrWhiteSpace(attrs))
            return $" class=\"{className}\"";

        var classMatch = ClassAttrRegex.Match(attrs);
        if (!classMatch.Success)
            return attrs + $" class=\"{className}\"";

        var classes = classMatch.Groups["value"].Value;
        if (classes.IndexOf(className, StringComparison.OrdinalIgnoreCase) >= 0)
            return attrs;

        var replacement = classMatch.Value.Replace(classes, (classes + " " + className).Trim());
        return attrs.Replace(classMatch.Value, replacement);
    }

    private static void AppendMetaHtml(Dictionary<string, object?> meta, string key, string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return;
        if (meta.TryGetValue(key, out var existing) && existing is string existingText && !string.IsNullOrWhiteSpace(existingText))
            meta[key] = existingText + Environment.NewLine + html;
        else
            meta[key] = html;
    }

    private static bool MetaContains(Dictionary<string, object?> meta, string key, string needle)
    {
        if (!meta.TryGetValue(key, out var existing) || existing is not string text) return false;
        return text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ComputeCacheKey(string content, string sourcePath, BuildCacheSpec? cache)
    {
        var mode = cache?.Mode ?? "contenthash";
        var input = mode.Equals("mtime", StringComparison.OrdinalIgnoreCase)
            ? $"{sourcePath}|{File.GetLastWriteTimeUtc(sourcePath).Ticks}"
            : content;

        using var sha = SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
        if (!TryGetMetaValue(meta, key, out var obj) || obj is null) return false;
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
        if (!TryGetMetaValue(meta, key, out var obj) || obj is null) return false;
        value = obj.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryRenderDataOverride(
        Dictionary<string, object?>? meta,
        ShortcodeRenderContext context,
        out string html,
        out string mode)
    {
        html = string.Empty;
        mode = string.Empty;
        if (meta is null || context is null)
            return false;

        if (!TryGetMetaString(meta, "data_shortcode", out var shortcode) &&
            !TryGetMetaString(meta, "data.shortcode", out shortcode))
            return false;

        var dataPath = GetMetaString(meta, "data_path");
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = GetMetaString(meta, "data.path");
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = GetMetaString(meta, "data_key");
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = GetMetaString(meta, "data.key");
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = GetMetaString(meta, "data");
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = shortcode;

        if (!TryResolveDataPath(context.Data, dataPath, out _))
            return false;

        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["data"] = dataPath
        };

        html = context.TryRenderThemeShortcode(shortcode, attrs) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(html) && ShortcodeRegistry.TryGet(shortcode, out var handler))
            html = handler(context, attrs);

        if (string.IsNullOrWhiteSpace(html))
            return false;

        mode = GetMetaString(meta, "data_mode");
        if (string.IsNullOrWhiteSpace(mode))
            mode = GetMetaString(meta, "data.mode");
        return true;
    }

    private static string ApplyContentTemplate(
        string content,
        Dictionary<string, object?>? meta,
        ShortcodeRenderContext context)
    {
        if (string.IsNullOrWhiteSpace(content) || meta is null || context is null)
            return content;

        var engineName = ResolveContentEngineName(meta);
        if (string.IsNullOrWhiteSpace(engineName))
            return content;

        var engine = ResolveContentEngine(engineName, context);
        if (engine is null)
            return content;

        var page = new ContentItem
        {
            Title = context.FrontMatter?.Title ?? string.Empty,
            Description = context.FrontMatter?.Description ?? string.Empty,
            Meta = context.FrontMatter?.Meta ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            EditUrl = context.FrontMatter?.EditUrl ?? context.EditUrl
        };

        var renderContext = new ThemeRenderContext
        {
            Site = context.Site,
            Page = page,
            Data = context.Data,
            Project = context.Project,
            Localization = BuildLocalizationRuntime(context.Site, page, Array.Empty<ContentItem>()),
            Versioning = BuildVersioningRuntime(context.Site, string.Empty)
        };

        var resolver = context.PartialResolver ?? (_ => null);
        return engine.Render(content, renderContext, resolver);
    }

    private static string? ResolveContentEngineName(Dictionary<string, object?>? meta)
    {
        if (meta is null) return null;
        if (TryGetMetaString(meta, "content_engine", out var engine))
            return engine;
        if (TryGetMetaString(meta, "content.engine", out engine))
            return engine;
        if (TryGetMetaString(meta, "content.template_engine", out engine))
            return engine;
        return null;
    }

    private static ITemplateEngine? ResolveContentEngine(string engineName, ShortcodeRenderContext context)
    {
        if (string.IsNullOrWhiteSpace(engineName))
            return null;

        if (engineName.Equals("theme", StringComparison.OrdinalIgnoreCase) ||
            engineName.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Engine is not null)
                return context.Engine;

            var themeEngine = context.Site.ThemeEngine ?? context.ThemeManifest?.Engine;
            return ThemeEngineRegistry.Resolve(themeEngine);
        }

        return ThemeEngineRegistry.Resolve(engineName);
    }

    private static bool TryResolveDataPath(IReadOnlyDictionary<string, object?> data, string path, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        object? current = data;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is IReadOnlyDictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out current))
                    return false;
                continue;
            }
            return false;
        }

        value = current;
        return true;
    }

    private enum DataRenderMode
    {
        Override,
        Append,
        Prepend
    }

    private static DataRenderMode NormalizeDataRenderMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return DataRenderMode.Override;
        if (mode.Equals("append", StringComparison.OrdinalIgnoreCase))
            return DataRenderMode.Append;
        if (mode.Equals("prepend", StringComparison.OrdinalIgnoreCase))
            return DataRenderMode.Prepend;
        return DataRenderMode.Override;
    }

    private static bool TryGetMetaValue(Dictionary<string, object?> meta, string key, out object? value)
    {
        value = null;
        if (meta is null || string.IsNullOrWhiteSpace(key)) return false;
        var parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        object? current = meta;
        foreach (var part in parts)
        {
            if (current is IReadOnlyDictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out current))
                    return false;
                continue;
            }

            return false;
        }

        value = current;
        return true;
    }

    private static int? GetMetaInt(Dictionary<string, object?>? meta, string key)
    {
        if (meta is null) return null;
        if (!TryGetMetaValue(meta, key, out var value) || value is null)
            return null;
        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (value is string s && int.TryParse(s, out var parsed)) return parsed;
        return null;
    }

    private static string GetMetaString(Dictionary<string, object?>? meta, string key)
    {
        if (meta is null)
            return string.Empty;

        return TryGetMetaString(meta, key, out var value) ? value : string.Empty;
    }

    private static string? GetMetaStringOrNull(Dictionary<string, object?>? meta, string key)
    {
        if (meta is null)
            return null;

        return TryGetMetaString(meta, key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
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

    private static bool MatchesFile(string rootPath, string[] contentRoots, string filePath, string input, string[] includePatterns, string[] excludePatterns)
    {
        var includes = NormalizePatterns(includePatterns);
        var excludes = NormalizePatterns(excludePatterns);

        foreach (var full in BuildCollectionInputCandidates(rootPath, contentRoots, input))
        {
            var basePath = full.Contains('*', StringComparison.Ordinal)
                ? full.Split('*')[0].TrimEnd(Path.DirectorySeparatorChar)
                : full;
            if (!Directory.Exists(basePath))
                continue;
            if (!IsPathWithinBase(basePath, filePath))
                continue;

            if (excludes.Length > 0 && MatchesAny(excludes, basePath, filePath))
                return false;

            if (includes.Length == 0 || MatchesAny(includes, basePath, filePath))
                return true;
        }

        return false;
    }

    private static void WriteContentItem(
        string outputRoot,
        SiteSpec spec,
        string rootPath,
        ContentItem item,
        IReadOnlyList<ContentItem> allItems,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap,
        MenuSpec[] menuSpecs)
    {
        if (item.Draft) return;

        var targetDir = ResolveOutputDirectory(outputRoot, item.OutputPath);
        var isNotFoundRoute = string.Equals(NormalizePath(item.OutputPath), "404", StringComparison.OrdinalIgnoreCase);

        var effectiveData = ResolveDataForProject(data, item.ProjectSlug);
        var formats = ResolveOutputFormats(spec, item);
        var outputs = ResolveOutputRuntime(spec, item, formats);
        var alternateHeadLinksHtml = RenderAlternateOutputHeadLinks(outputs);
        foreach (var format in formats)
        {
            var outputFileName = ResolveOutputFileName(format);
            var outputFile = isNotFoundRoute && string.Equals(outputFileName, "index.html", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(outputRoot, "404.html")
                : Path.Combine(targetDir, outputFileName);
            var outputDirectory = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
            var content = RenderOutput(spec, rootPath, item, allItems, effectiveData, projectMap, menuSpecs, format, outputs, alternateHeadLinksHtml);
            File.WriteAllText(outputFile, content);
        }
        CopyPageResources(item, isNotFoundRoute ? outputRoot : targetDir);
    }

    private static string RenderHtmlPage(
        SiteSpec spec,
        string rootPath,
        ContentItem item,
        IReadOnlyList<ContentItem> allItems,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap,
        MenuSpec[] menuSpecs,
        IReadOnlyList<OutputRuntime> outputs,
        string alternateHeadLinksHtml)
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
        var breadcrumbs = BuildBreadcrumbs(spec, item, menuSpecs);
        var listItems = ResolveListItems(item, allItems);
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
            Items = listItems,
            Data = data,
            Project = projectSpec,
            Navigation = BuildNavigation(spec, item, menuSpecs),
            Localization = BuildLocalizationRuntime(spec, item, allItems),
            Versioning = BuildVersioningRuntime(spec, item.OutputPath),
            Outputs = outputs.ToArray(),
            FeedUrl = outputs.FirstOrDefault(o => string.Equals(o.Name, "rss", StringComparison.OrdinalIgnoreCase))?.Url,
            Breadcrumbs = breadcrumbs,
            CurrentPath = item.OutputPath,
            CssHtml = cssHtml,
            JsHtml = jsHtml,
            PreloadsHtml = preloads,
            CriticalCssHtml = criticalCss,
            CanonicalHtml = canonical,
            DescriptionMetaHtml = descriptionMeta,
            HeadHtml = string.IsNullOrWhiteSpace(alternateHeadLinksHtml)
                ? headHtml
                : string.Join(Environment.NewLine, new[] { headHtml, alternateHeadLinksHtml }.Where(v => !string.IsNullOrWhiteSpace(v))),
            OpenGraphHtml = openGraph,
            StructuredDataHtml = structuredData,
            ExtraCssHtml = extraCss,
            ExtraScriptsHtml = extraScripts,
            BodyClass = bodyClass,
            Taxonomy = ResolveTaxonomy(spec, item),
            Term = ResolveTerm(item)
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

        var htmlLang = System.Web.HttpUtility.HtmlEncode(renderContext.Localization.Current.Code ?? "en");
        return $@"<!doctype html>
<html lang=""{htmlLang}"">
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

    private static OutputFormatSpec[] ResolveOutputFormats(SiteSpec spec, ContentItem item)
    {
        var formatNames = item.Outputs.Length > 0 ? item.Outputs : ResolveOutputRule(spec, item);
        if (formatNames.Length == 0)
            formatNames = new[] { "html" };

        var formats = new List<OutputFormatSpec>();
        foreach (var name in formatNames)
        {
            var format = ResolveOutputFormatSpec(spec, name);
            if (format is not null)
                formats.Add(format);
        }

        if (formats.Count == 0)
            formats.Add(new OutputFormatSpec { Name = "html", MediaType = "text/html", Suffix = "html" });

        return formats.ToArray();
    }

    private static string[] ResolveOutputRule(SiteSpec spec, ContentItem item)
    {
        if (spec.Outputs?.Rules is null || spec.Outputs.Rules.Length == 0)
            return ResolveImplicitOutputRule(item);

        var kind = item.Kind.ToString().ToLowerInvariant();
        foreach (var rule in spec.Outputs.Rules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Kind)) continue;
            if (string.Equals(rule.Kind, kind, StringComparison.OrdinalIgnoreCase))
                return rule.Formats ?? Array.Empty<string>();
        }

        return ResolveImplicitOutputRule(item);
    }

    private static string[] ResolveImplicitOutputRule(ContentItem item)
    {
        if (item is null)
            return Array.Empty<string>();

        if (item.Kind == PageKind.Section &&
            string.Equals(item.Collection, "blog", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "html", "rss" };
        }

        if ((item.Kind == PageKind.Taxonomy || item.Kind == PageKind.Term) &&
            (string.Equals(item.Collection, "tags", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Collection, "categories", StringComparison.OrdinalIgnoreCase)))
        {
            return new[] { "html", "rss" };
        }

        return Array.Empty<string>();
    }

    private static OutputFormatSpec? ResolveOutputFormatSpec(SiteSpec spec, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (spec.Outputs?.Formats is not null)
        {
            var match = spec.Outputs.Formats.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return name.ToLowerInvariant() switch
        {
            "html" => new OutputFormatSpec { Name = "html", MediaType = "text/html", Suffix = "html" },
            "rss" => new OutputFormatSpec { Name = "rss", MediaType = "application/rss+xml", Suffix = "xml", Rel = "alternate" },
            "json" => new OutputFormatSpec { Name = "json", MediaType = "application/json", Suffix = "json" },
            _ => new OutputFormatSpec { Name = name, MediaType = "text/plain", Suffix = name, IsPlainText = true }
        };
    }

    private static string ResolveOutputFileName(OutputFormatSpec format)
    {
        if (string.IsNullOrWhiteSpace(format.Suffix) || format.Suffix.Equals("html", StringComparison.OrdinalIgnoreCase))
            return "index.html";
        return $"index.{format.Suffix}";
    }

    private static OutputRuntime[] ResolveOutputRuntime(SiteSpec spec, ContentItem item, IReadOnlyList<OutputFormatSpec> formats)
    {
        if (formats.Count == 0)
            return Array.Empty<OutputRuntime>();

        var outputs = formats
            .Where(format => format is not null && !string.IsNullOrWhiteSpace(format.Name))
            .Select(format =>
            {
                var name = format.Name.Trim();
                var route = ResolveOutputRoute(item.OutputPath, format);
                var url = string.IsNullOrWhiteSpace(route)
                    ? string.Empty
                    : (string.IsNullOrWhiteSpace(spec.BaseUrl) ? route : CombineAbsoluteUrl(spec.BaseUrl, route));
                return new OutputRuntime
                {
                    Name = name.ToLowerInvariant(),
                    Url = url,
                    MediaType = string.IsNullOrWhiteSpace(format.MediaType) ? "text/html" : format.MediaType,
                    Rel = format.Rel,
                    IsCurrent = string.Equals(name, "html", StringComparison.OrdinalIgnoreCase)
                };
            })
            .GroupBy(output => output.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return outputs;
    }

    private static string RenderAlternateOutputHeadLinks(IReadOnlyList<OutputRuntime> outputs)
    {
        if (outputs is null || outputs.Count == 0)
            return string.Empty;

        var lines = outputs
            .Where(output => !output.IsCurrent)
            .Where(output => !string.IsNullOrWhiteSpace(output.Url))
            .Select(output =>
            {
                var rel = string.IsNullOrWhiteSpace(output.Rel) ? "alternate" : output.Rel!;
                var relEncoded = System.Web.HttpUtility.HtmlEncode(rel);
                var typeEncoded = System.Web.HttpUtility.HtmlEncode(output.MediaType);
                var hrefEncoded = System.Web.HttpUtility.HtmlEncode(output.Url);
                var titleEncoded = System.Web.HttpUtility.HtmlEncode(output.Name.ToUpperInvariant());
                return $"<link rel=\"{relEncoded}\" type=\"{typeEncoded}\" href=\"{hrefEncoded}\" title=\"{titleEncoded}\" />";
            })
            .ToArray();

        return lines.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, lines);
    }

    private static string ResolveOutputRoute(string outputPath, OutputFormatSpec format)
    {
        var baseRoute = NormalizeRouteForMatch(outputPath);
        if (string.IsNullOrWhiteSpace(format.Suffix) || format.Suffix.Equals("html", StringComparison.OrdinalIgnoreCase))
            return baseRoute;

        var prefix = baseRoute.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "/";

        if (prefix == "/")
            return $"/index.{format.Suffix}";

        return $"{prefix}/index.{format.Suffix}";
    }

    private static string CombineAbsoluteUrl(string baseUrl, string path)
    {
        var normalizedBase = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedBase))
            return path;

        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? "/"
            : (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path.TrimStart('/'));
        return normalizedBase + normalizedPath;
    }

    private static string RenderOutput(
        SiteSpec spec,
        string rootPath,
        ContentItem item,
        IReadOnlyList<ContentItem> allItems,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap,
        MenuSpec[] menuSpecs,
        OutputFormatSpec format,
        IReadOnlyList<OutputRuntime> outputs,
        string alternateHeadLinksHtml)
    {
        var name = format.Name.ToLowerInvariant();
        return name switch
        {
            "json" => RenderJsonOutput(spec, item, allItems),
            "rss" => RenderRssOutput(spec, item, allItems),
            _ => RenderHtmlPage(spec, rootPath, item, allItems, data, projectMap, menuSpecs, outputs, alternateHeadLinksHtml)
        };
    }

    private static string RenderJsonOutput(SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> items)
    {
        var listItems = ResolveListItems(item, items);
        var payload = new Dictionary<string, object?>
        {
            ["title"] = item.Title,
            ["description"] = item.Description,
            ["url"] = item.OutputPath,
            ["kind"] = item.Kind.ToString().ToLowerInvariant(),
            ["collection"] = item.Collection,
            ["tags"] = item.Tags,
            ["date"] = item.Date?.ToString("O"),
            ["content"] = item.HtmlContent,
            ["items"] = listItems.Select(i => new Dictionary<string, object?>
            {
                ["title"] = i.Title,
                ["url"] = i.OutputPath,
                ["description"] = i.Description,
                ["date"] = i.Date?.ToString("O"),
                ["tags"] = i.Tags
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, WebJson.Options);
    }

    private static string RenderRssOutput(SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> items)
    {
        var listItems = ResolveListItems(item, items);
        var baseUrl = spec.BaseUrl?.TrimEnd('/') ?? string.Empty;
        var channelTitle = string.IsNullOrWhiteSpace(item.Title) ? spec.Name : item.Title;
        var channelLink = string.IsNullOrWhiteSpace(baseUrl) ? item.OutputPath : baseUrl + item.OutputPath;
        var channelDescription = string.IsNullOrWhiteSpace(item.Description) ? spec.Name : item.Description;

        var feedItems = listItems
            .OrderByDescending(i => i.Date ?? DateTime.MinValue)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .Select(i =>
            {
                var link = string.IsNullOrWhiteSpace(baseUrl) ? i.OutputPath : baseUrl + i.OutputPath;
                var description = string.IsNullOrWhiteSpace(i.Description) ? BuildSnippet(i.HtmlContent, 200) : i.Description;
                var pubDate = i.Date?.ToUniversalTime().ToString("r") ?? DateTime.UtcNow.ToString("r");
                return new XElement("item",
                    new XElement("title", i.Title),
                    new XElement("link", link),
                    new XElement("description", description),
                    new XElement("pubDate", pubDate));
            })
            .ToArray();

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("rss",
                new XAttribute("version", "2.0"),
                new XElement("channel",
                    new XElement("title", channelTitle),
                    new XElement("link", channelLink),
                    new XElement("description", channelDescription),
                    feedItems)));

        using var sw = new StringWriter();
        doc.Save(sw);
        return sw.ToString();
    }

    private static void CopyPageResources(ContentItem item, string targetDir)
    {
        if (item.Resources is null || item.Resources.Length == 0)
            return;

        foreach (var resource in item.Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.SourcePath) || !File.Exists(resource.SourcePath))
                continue;

            var relative = resource.RelativePath ?? string.Empty;
            var target = string.IsNullOrWhiteSpace(relative)
                ? Path.Combine(targetDir, resource.Name)
                : Path.Combine(targetDir, relative.Replace('/', Path.DirectorySeparatorChar));
            var targetFolder = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetFolder))
                Directory.CreateDirectory(targetFolder);
            File.Copy(resource.SourcePath, target, overwrite: true);
        }
    }

    private static IReadOnlyList<ContentItem> ResolveListItems(ContentItem item, IReadOnlyList<ContentItem> items)
    {
        if (item.Kind == PageKind.Section)
        {
            var current = NormalizeRouteForMatch(item.OutputPath);
            return items
                .Where(i => !i.Draft)
                .Where(i => i.Collection == item.Collection)
                .Where(i => i.OutputPath != item.OutputPath)
                .Where(i => i.Kind == PageKind.Page || i.Kind == PageKind.Home)
                .Where(i => NormalizeRouteForMatch(i.OutputPath).StartsWith(current, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Order ?? int.MaxValue)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (item.Kind == PageKind.Taxonomy)
        {
            var taxonomy = GetMetaString(item.Meta, "taxonomy");
            return items
                .Where(i => i.Kind == PageKind.Term)
                .Where(i => string.Equals(GetMetaString(i.Meta, "taxonomy"), taxonomy, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (item.Kind == PageKind.Term)
        {
            var taxonomy = GetMetaString(item.Meta, "taxonomy");
            var term = GetMetaString(item.Meta, "term");
            if (string.IsNullOrWhiteSpace(term))
                return Array.Empty<ContentItem>();

            return items
                .Where(i => !i.Draft)
                .Where(i => i.Kind == PageKind.Page || i.Kind == PageKind.Home)
                .Where(i => GetTaxonomyValues(i, new TaxonomySpec { Name = taxonomy }).Any(t =>
                    string.Equals(t, term, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(i => i.Order ?? int.MaxValue)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Array.Empty<ContentItem>();
    }

    private static TaxonomySpec? ResolveTaxonomy(SiteSpec spec, ContentItem item)
    {
        var key = GetMetaString(item.Meta, "taxonomy");
        if (string.IsNullOrWhiteSpace(key)) return null;
        return spec.Taxonomies.FirstOrDefault(t => string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveTerm(ContentItem item)
    {
        var term = GetMetaString(item.Meta, "term");
        return string.IsNullOrWhiteSpace(term) ? null : term;
    }

}


