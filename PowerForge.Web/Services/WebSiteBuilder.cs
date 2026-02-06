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
public static class WebSiteBuilder
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

    private static void WriteSiteNavData(SiteSpec spec, string outputRoot, MenuSpec[] menuSpecs)
    {
        var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
        var relativeRoot = Path.IsPathRooted(dataRoot)
            ? "data"
            : dataRoot.TrimStart('/', '\\');
        var dataDir = Path.Combine(outputRoot, relativeRoot);
        if (string.IsNullOrWhiteSpace(dataDir))
            return;

        var outputPath = Path.Combine(dataDir, "site-nav.json");
        if (File.Exists(outputPath))
            return;

        Directory.CreateDirectory(dataDir);

        var menus = menuSpecs.ToDictionary(
            m => m.Name,
            m => MapMenuItems(m.Items),
            StringComparer.OrdinalIgnoreCase);

        var primary = menus.TryGetValue("main", out var main)
            ? main
            : Array.Empty<object>();

        var footer = menus
            .Where(kvp => kvp.Key.StartsWith("footer", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        var payload = new
        {
            generated = true,
            primary,
            menus,
            footer = footer.Count > 0 ? footer : null,
            actions = MapMenuItems(spec.Navigation?.Actions ?? Array.Empty<MenuItemSpec>()),
            regions = MapRegions(spec.Navigation?.Regions ?? Array.Empty<NavigationRegionSpec>()),
            footerModel = MapFooter(spec.Navigation?.Footer),
            profiles = MapProfiles(spec.Navigation?.Profiles ?? Array.Empty<NavigationProfileSpec>())
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, WebJson.Options));
    }

    private static object[] MapMenuItems(MenuItemSpec[] items)
    {
        if (items is null || items.Length == 0) return Array.Empty<object>();
        var ordered = OrderMenuItems(items);
        return ordered.Select(item => new
        {
            href = item.Url,
            text = item.Text ?? item.Title,
            title = item.Title,
            external = item.External,
            target = item.Target,
            rel = item.Rel,
            kind = item.Kind,
            @class = item.CssClass,
            ariaLabel = item.AriaLabel,
            iconHtml = item.IconHtml,
            id = item.Id,
            slot = item.Slot,
            template = item.Template,
            badge = item.Badge,
            description = item.Description,
            visibility = MapVisibility(item.Visibility),
            sections = MapSections(item.Sections),
            meta = item.Meta,
            items = MapMenuItems(item.Items)
        }).ToArray();
    }

    private static object[] MapRegions(NavigationRegionSpec[] regions)
    {
        if (regions is null || regions.Length == 0) return Array.Empty<object>();
        return regions
            .Where(region => region is not null && !string.IsNullOrWhiteSpace(region.Name))
            .Select(region => new
            {
                name = region.Name,
                title = region.Title,
                menus = region.Menus,
                includeActions = region.IncludeActions,
                @class = region.CssClass,
                items = MapMenuItems(region.Items)
            })
            .ToArray();
    }

    private static object? MapFooter(NavigationFooterSpec? footer)
    {
        if (footer is null)
            return null;

        return new
        {
            label = footer.Label,
            menus = footer.Menus,
            columns = MapFooterColumns(footer.Columns),
            legal = MapMenuItems(footer.Legal)
        };
    }

    private static object[] MapFooterColumns(NavigationFooterColumnSpec[] columns)
    {
        if (columns is null || columns.Length == 0) return Array.Empty<object>();
        return columns
            .Where(column => column is not null && !string.IsNullOrWhiteSpace(column.Name))
            .Select(column => new
            {
                name = column.Name,
                title = column.Title,
                items = MapMenuItems(column.Items)
            })
            .ToArray();
    }

    private static object[] MapProfiles(NavigationProfileSpec[] profiles)
    {
        if (profiles is null || profiles.Length == 0) return Array.Empty<object>();
        return profiles
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => new
            {
                name = profile.Name,
                priority = profile.Priority,
                paths = profile.Paths,
                collections = profile.Collections,
                layouts = profile.Layouts,
                projects = profile.Projects,
                inheritMenus = profile.InheritMenus,
                inheritActions = profile.InheritActions,
                inheritRegions = profile.InheritRegions,
                inheritFooter = profile.InheritFooter,
                menus = (profile.Menus ?? Array.Empty<MenuSpec>())
                    .Where(menu => menu is not null && !string.IsNullOrWhiteSpace(menu.Name))
                    .Select(menu => new
                    {
                        name = menu.Name,
                        label = menu.Label,
                        visibility = MapVisibility(menu.Visibility),
                        items = MapMenuItems(menu.Items)
                    })
                    .ToArray(),
                actions = MapMenuItems(profile.Actions ?? Array.Empty<MenuItemSpec>()),
                regions = MapRegions(profile.Regions ?? Array.Empty<NavigationRegionSpec>()),
                footer = MapFooter(profile.Footer)
            })
            .ToArray();
    }

    private static object? MapVisibility(NavigationVisibilitySpec? visibility)
    {
        if (visibility is null)
            return null;

        return new
        {
            paths = visibility.Paths,
            excludePaths = visibility.ExcludePaths,
            collections = visibility.Collections,
            layouts = visibility.Layouts,
            projects = visibility.Projects
        };
    }

    private static object[] MapSections(MenuSectionSpec[] sections)
    {
        if (sections is null || sections.Length == 0) return Array.Empty<object>();
        return sections
            .Where(section => section is not null)
            .Select(section => new
            {
                name = section.Name,
                title = section.Title,
                description = section.Description,
                @class = section.CssClass,
                items = MapMenuItems(section.Items),
                columns = MapColumns(section.Columns)
            })
            .ToArray();
    }

    private static object[] MapColumns(MenuColumnSpec[] columns)
    {
        if (columns is null || columns.Length == 0) return Array.Empty<object>();
        return columns
            .Where(column => column is not null)
            .Select(column => new
            {
                name = column.Name,
                title = column.Title,
                items = MapMenuItems(column.Items)
            })
            .ToArray();
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
                var relativeDir = NormalizePath(Path.GetDirectoryName(relativePath) ?? string.Empty);
                var isSectionIndex = IsSectionIndex(file);
                var isBundleIndex = IsLeafBundleIndex(file);
                var slugPath = ResolveSlugPath(relativePath, relativeDir, matter?.Slug);
                if (isSectionIndex || isBundleIndex)
                    slugPath = ApplySlugOverride(relativeDir, matter?.Slug);
                var baseOutput = ReplaceProjectPlaceholder(collection.Output, projectSlug);
                var route = BuildRoute(baseOutput, slugPath, spec.TrailingSlash);
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

        var sourceItems = items
            .Where(i => i.Kind == PageKind.Page || i.Kind == PageKind.Home)
            .Where(i => !i.Draft)
            .ToList();

        foreach (var taxonomy in spec.Taxonomies)
        {
            if (taxonomy is null || string.IsNullOrWhiteSpace(taxonomy.Name) || string.IsNullOrWhiteSpace(taxonomy.BasePath))
                continue;

            var termMap = new Dictionary<string, List<ContentItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in sourceItems)
            {
                foreach (var term in GetTaxonomyValues(item, taxonomy))
                {
                    if (!termMap.TryGetValue(term, out var list))
                    {
                        list = new List<ContentItem>();
                        termMap[term] = list;
                    }
                    list.Add(item);
                }
            }

            var taxRoute = BuildRoute(taxonomy.BasePath, string.Empty, spec.TrailingSlash);
            results.Add(new ContentItem
            {
                Collection = taxonomy.Name,
                OutputPath = taxRoute,
                Title = HumanizeSegment(taxonomy.Name),
                Description = string.Empty,
                Kind = PageKind.Taxonomy,
                Layout = taxonomy.ListLayout ?? "taxonomy",
                Meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["taxonomy"] = taxonomy.Name
                }
            });

            foreach (var term in termMap.Keys.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                var slug = Slugify(term);
                var termRoute = BuildRoute(taxonomy.BasePath, slug, spec.TrailingSlash);
                results.Add(new ContentItem
                {
                    Collection = taxonomy.Name,
                    OutputPath = termRoute,
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
            Project = context.Project
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
        foreach (var format in formats)
        {
            var outputFileName = ResolveOutputFileName(format);
            var outputFile = isNotFoundRoute && string.Equals(outputFileName, "index.html", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(outputRoot, "404.html")
                : Path.Combine(targetDir, outputFileName);
            var outputDirectory = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
            var content = RenderOutput(spec, rootPath, item, allItems, effectiveData, projectMap, menuSpecs, format);
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
        MenuSpec[] menuSpecs)
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
            return Array.Empty<string>();

        var kind = item.Kind.ToString().ToLowerInvariant();
        foreach (var rule in spec.Outputs.Rules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Kind)) continue;
            if (string.Equals(rule.Kind, kind, StringComparison.OrdinalIgnoreCase))
                return rule.Formats ?? Array.Empty<string>();
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

    private static string RenderOutput(
        SiteSpec spec,
        string rootPath,
        ContentItem item,
        IReadOnlyList<ContentItem> allItems,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap,
        MenuSpec[] menuSpecs,
        OutputFormatSpec format)
    {
        var name = format.Name.ToLowerInvariant();
        return name switch
        {
            "json" => RenderJsonOutput(spec, item, allItems),
            "rss" => RenderRssOutput(spec, item, allItems),
            _ => RenderHtmlPage(spec, rootPath, item, allItems, data, projectMap, menuSpecs)
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

    private static MenuSpec[] BuildMenuSpecs(SiteSpec spec, IReadOnlyList<ContentItem> items, string rootPath)
    {
        var result = new Dictionary<string, MenuSpec>(StringComparer.OrdinalIgnoreCase);
        var navSpec = spec.Navigation ?? new NavigationSpec();

        if (navSpec.Menus is not null)
        {
            foreach (var menu in navSpec.Menus)
            {
                if (menu is null || string.IsNullOrWhiteSpace(menu.Name)) continue;
                result[menu.Name] = CloneMenu(menu);
            }
        }

        if (navSpec.Auto is not null && navSpec.Auto.Length > 0)
        {
            foreach (var auto in navSpec.Auto)
            {
                if (auto is null || string.IsNullOrWhiteSpace(auto.Collection) || string.IsNullOrWhiteSpace(auto.Menu))
                    continue;

                var collection = spec.Collections.FirstOrDefault(c =>
                    string.Equals(c.Name, auto.Collection, StringComparison.OrdinalIgnoreCase));
                var menuItems = BuildAutoMenuItems(auto, collection, items, rootPath);
                if (menuItems.Length == 0) continue;

                if (result.TryGetValue(auto.Menu, out var existing))
                {
                    existing.Items = existing.Items.Concat(menuItems).ToArray();
                }
                else
                {
                    result[auto.Menu] = new MenuSpec
                    {
                        Name = auto.Menu,
                        Label = auto.Menu,
                        Items = menuItems
                    };
                }
            }
        }
        else if (result.Count == 0 && navSpec.AutoDefaults)
        {
            foreach (var auto in BuildDefaultAutoSpecs(spec))
            {
                if (auto is null || string.IsNullOrWhiteSpace(auto.Collection) || string.IsNullOrWhiteSpace(auto.Menu))
                    continue;

                var collection = spec.Collections.FirstOrDefault(c =>
                    string.Equals(c.Name, auto.Collection, StringComparison.OrdinalIgnoreCase));
                var menuItems = BuildAutoMenuItems(auto, collection, items, rootPath);
                if (menuItems.Length == 0) continue;

                if (result.TryGetValue(auto.Menu, out var existing))
                {
                    existing.Items = existing.Items.Concat(menuItems).ToArray();
                }
                else
                {
                    result[auto.Menu] = new MenuSpec
                    {
                        Name = auto.Menu,
                        Label = auto.Menu,
                        Items = menuItems
                    };
                }
            }
        }

        return result.Values.ToArray();
    }

    private static NavigationAutoSpec[] BuildDefaultAutoSpecs(SiteSpec spec)
    {
        if (spec.Collections is null || spec.Collections.Length == 0)
            return Array.Empty<NavigationAutoSpec>();

        var docsCollection = spec.Collections.FirstOrDefault(c =>
            string.Equals(c.Name, "docs", StringComparison.OrdinalIgnoreCase)) ??
                             spec.Collections.FirstOrDefault(c =>
                                 !string.IsNullOrWhiteSpace(c.Output) &&
                                 c.Output.StartsWith("/docs", StringComparison.OrdinalIgnoreCase));

        var mainCollection = spec.Collections.FirstOrDefault(c =>
            string.Equals(c.Name, "pages", StringComparison.OrdinalIgnoreCase)) ??
                              spec.Collections.FirstOrDefault(c =>
                                  string.IsNullOrWhiteSpace(c.Output) || c.Output == "/");

        var results = new List<NavigationAutoSpec>();
        if (docsCollection is not null)
        {
            results.Add(new NavigationAutoSpec
            {
                Collection = docsCollection.Name,
                Menu = "docs",
                MaxDepth = 3,
                IncludeIndex = true
            });
        }

        if (mainCollection is not null &&
            (docsCollection is null || !string.Equals(mainCollection.Name, docsCollection.Name, StringComparison.OrdinalIgnoreCase)))
        {
            results.Add(new NavigationAutoSpec
            {
                Collection = mainCollection.Name,
                Menu = "main",
                MaxDepth = 1,
                IncludeIndex = true
            });
        }

        return results.ToArray();
    }

    private static MenuItemSpec[] BuildAutoMenuItems(NavigationAutoSpec auto, CollectionSpec? collection, IReadOnlyList<ContentItem> items, string rootPath)
    {
        if (collection is null) return Array.Empty<MenuItemSpec>();
        var tocItems = LoadTocItems(collection, rootPath);
        if (tocItems.Length > 0)
            return BuildMenuItemsFromToc(tocItems, auto);
        var root = string.IsNullOrWhiteSpace(auto.Root) ? collection.Output : auto.Root;
        var rootNormalized = NormalizeRouteForMatch(string.IsNullOrWhiteSpace(root) ? "/" : root);
        var includeDrafts = auto.IncludeDrafts;
        var includeIndex = auto.IncludeIndex;
        var maxDepth = auto.MaxDepth;

        var nodes = new Dictionary<string, NavNode>(StringComparer.OrdinalIgnoreCase);
        var rootNode = new NavNode(rootNormalized, string.Empty, 0);
        nodes[rootNormalized] = rootNode;

        foreach (var item in items)
        {
            if (!string.Equals(item.Collection, auto.Collection, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!includeDrafts && item.Draft)
                continue;
            if (!includeIndex && item.Kind == PageKind.Section)
                continue;

            var normalized = NormalizeRouteForMatch(item.OutputPath);
            if (!IsUnderRoot(normalized, rootNormalized))
                continue;

            var relative = normalized.Substring(rootNormalized.Length).Trim('/');
            if (string.IsNullOrWhiteSpace(relative))
            {
                rootNode.Item = item;
                continue;
            }

            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = rootNode;
            var path = rootNormalized.TrimEnd('/');
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                path = path + "/" + segment + "/";
                if (!current.Children.TryGetValue(segment, out var next))
                {
                    next = new NavNode(path, segment, current.Depth + 1);
                    current.Children[segment] = next;
                    nodes[path] = next;
                }
                current = next;
            }

            current.Item = item;
        }

        var menuItems = BuildMenuItemsFromNodes(rootNode, auto, maxDepth);
        return OrderMenuItems(menuItems, auto.Sort);
    }

    private static TocItem[] LoadTocItems(CollectionSpec collection, string rootPath)
    {
        if (collection.UseToc == false)
            return Array.Empty<TocItem>();

        var tocPath = collection.TocFile;
        if (!string.IsNullOrWhiteSpace(tocPath))
        {
            var resolved = Path.IsPathRooted(tocPath) ? tocPath : Path.Combine(rootPath, tocPath);
            return LoadTocFromPath(resolved);
        }

        var inputRoot = Path.IsPathRooted(collection.Input)
            ? collection.Input
            : Path.Combine(rootPath, collection.Input);
        if (inputRoot.Contains('*'))
            return Array.Empty<TocItem>();

        var jsonPath = Path.Combine(inputRoot, "toc.json");
        if (File.Exists(jsonPath))
            return LoadTocFromPath(jsonPath);

        var yamlPath = Path.Combine(inputRoot, "toc.yml");
        if (File.Exists(yamlPath))
            return LoadTocFromPath(yamlPath);

        var yamlAltPath = Path.Combine(inputRoot, "toc.yaml");
        if (File.Exists(yamlAltPath))
            return LoadTocFromPath(yamlAltPath);

        return Array.Empty<TocItem>();
    }

    private static TocItem[] LoadTocFromPath(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<TocItem>();

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".json")
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TocItem[]>(json, WebJson.Options) ?? Array.Empty<TocItem>();
        }

        if (ext is ".yml" or ".yaml")
        {
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var items = deserializer.Deserialize<List<TocItem>>(yaml);
            return items?.ToArray() ?? Array.Empty<TocItem>();
        }

        return Array.Empty<TocItem>();
    }

    private static MenuItemSpec[] BuildMenuItemsFromToc(TocItem[] items, NavigationAutoSpec auto, int depth = 1)
    {
        if (items is null || items.Length == 0)
            return Array.Empty<MenuItemSpec>();

        if (auto.MaxDepth.HasValue && depth > auto.MaxDepth.Value)
            return Array.Empty<MenuItemSpec>();

        var list = new List<MenuItemSpec>();
        foreach (var item in items)
        {
            if (item is null) continue;
            if (item.Hidden) continue;
            var title = item.Title ?? item.Name ?? item.Href ?? item.Url ?? "Untitled";
            var menuItem = new MenuItemSpec
            {
                Title = title,
                Url = item.Url ?? item.Href,
                Items = BuildMenuItemsFromToc(item.Items ?? Array.Empty<TocItem>(), auto, depth + 1)
            };
            list.Add(menuItem);
        }

        return list.ToArray();
    }

    private static MenuItemSpec[] BuildMenuItemsFromNodes(NavNode node, NavigationAutoSpec auto, int? maxDepth)
    {
        if (node.Children.Count == 0)
            return Array.Empty<MenuItemSpec>();

        var list = new List<MenuItemSpec>();
        foreach (var child in node.Children.Values.OrderBy(c => c.Segment, StringComparer.OrdinalIgnoreCase))
        {
            if (maxDepth.HasValue && child.Depth > maxDepth.Value)
                continue;

            if (child.Item is not null && IsNavHidden(child.Item))
                continue;

            var title = ResolveNavTitle(child);
            var url = child.Item?.OutputPath;
            var icon = child.Item is null ? null : GetMetaString(child.Item.Meta, "nav.icon");
            var badge = child.Item is null ? null : GetMetaString(child.Item.Meta, "nav.badge");
            var description = child.Item is null ? null : GetMetaString(child.Item.Meta, "nav.description");
            var weight = child.Item?.Order;
            var navWeight = child.Item is null ? null : GetMetaInt(child.Item.Meta, "nav.weight");
            if (navWeight.HasValue) weight = navWeight;

            var itemSpec = new MenuItemSpec
            {
                Title = title,
                Url = url,
                Icon = icon,
                Badge = badge,
                Description = description,
                Weight = weight,
                Match = child.Path
            };

            itemSpec.Items = BuildMenuItemsFromNodes(child, auto, maxDepth);
            list.Add(itemSpec);
        }

        return list.ToArray();
    }

    private static bool IsNavHidden(ContentItem item)
    {
        if (item.Meta is null || item.Meta.Count == 0) return false;
        if (TryGetMetaBool(item.Meta, "nav.hidden", out var hidden))
            return hidden;
        return false;
    }

    private static string ResolveNavTitle(NavNode node)
    {
        if (node.Item is not null)
        {
            var overrideTitle = GetMetaString(node.Item.Meta, "nav.title");
            return string.IsNullOrWhiteSpace(overrideTitle) ? node.Item.Title : overrideTitle;
        }

        return HumanizeSegment(node.Segment);
    }

    private static MenuSpec CloneMenu(MenuSpec menu)
    {
        return new MenuSpec
        {
            Name = menu.Name,
            Label = menu.Label,
            Visibility = CloneVisibility(menu.Visibility),
            Items = CloneMenuItems(menu.Items)
        };
    }

    private static MenuItemSpec[] CloneMenuItems(MenuItemSpec[] items)
    {
        if (items is null || items.Length == 0) return Array.Empty<MenuItemSpec>();
        return items.Select(i => new MenuItemSpec
        {
            Id = i.Id,
            Title = i.Title,
            Text = i.Text,
            Url = i.Url,
            Icon = i.Icon,
            IconHtml = i.IconHtml,
            Kind = i.Kind,
            Slot = i.Slot,
            Template = i.Template,
            CssClass = i.CssClass,
            AriaLabel = i.AriaLabel,
            Badge = i.Badge,
            Description = i.Description,
            Target = i.Target,
            Rel = i.Rel,
            External = i.External,
            Weight = i.Weight,
            Match = i.Match,
            Visibility = CloneVisibility(i.Visibility),
            Sections = CloneSections(i.Sections),
            Meta = CloneMeta(i.Meta),
            Items = CloneMenuItems(i.Items)
        }).ToArray();
    }

    private static NavigationRuntime BuildNavigation(SiteSpec spec, ContentItem currentItem, MenuSpec[] menuSpecs)
    {
        var navSpec = spec.Navigation ?? new NavigationSpec();
        var currentPath = NormalizeRouteForMatch(currentItem.OutputPath);
        var context = new NavRenderContext(
            currentPath,
            currentItem.Collection ?? string.Empty,
            currentItem.Layout ?? string.Empty,
            currentItem.ProjectSlug ?? string.Empty);

        var activeProfile = ResolveNavigationProfile(navSpec, context);

        var effectiveMenus = MergeMenus(
            menuSpecs,
            activeProfile?.Menus ?? Array.Empty<MenuSpec>(),
            activeProfile?.InheritMenus ?? true);

        var effectiveActions = MergeItems(
            navSpec.Actions,
            activeProfile?.Actions ?? Array.Empty<MenuItemSpec>(),
            activeProfile?.InheritActions ?? true);

        var effectiveRegions = MergeRegions(
            navSpec.Regions,
            activeProfile?.Regions ?? Array.Empty<NavigationRegionSpec>(),
            activeProfile?.InheritRegions ?? true);

        var effectiveFooter = MergeFooter(
            navSpec.Footer,
            activeProfile?.Footer,
            activeProfile?.InheritFooter ?? true);

        var nav = new NavigationRuntime
        {
            ActiveProfile = activeProfile?.Name
        };

        if (effectiveMenus.Length > 0)
        {
            nav.Menus = effectiveMenus
                .Where(menu => IsVisible(menu.Visibility, context))
                .Select(menu => new NavigationMenu
                {
                    Name = menu.Name,
                    Label = menu.Label,
                    Items = BuildMenuItems(menu.Items, context, spec.LinkRules)
                })
                .ToArray();
        }

        nav.Actions = BuildMenuItems(effectiveActions, context, spec.LinkRules);
        nav.Regions = BuildRegions(effectiveRegions, nav.Menus, nav.Actions, context, spec.LinkRules);
        nav.Footer = BuildFooter(effectiveFooter, nav.Menus, context, spec.LinkRules);

        return nav;
    }

    private static NavigationVisibilitySpec? CloneVisibility(NavigationVisibilitySpec? visibility)
    {
        if (visibility is null)
            return null;
        return new NavigationVisibilitySpec
        {
            Paths = visibility.Paths?.ToArray() ?? Array.Empty<string>(),
            ExcludePaths = visibility.ExcludePaths?.ToArray() ?? Array.Empty<string>(),
            Collections = visibility.Collections?.ToArray() ?? Array.Empty<string>(),
            Layouts = visibility.Layouts?.ToArray() ?? Array.Empty<string>(),
            Projects = visibility.Projects?.ToArray() ?? Array.Empty<string>()
        };
    }

    private static MenuSectionSpec[] CloneSections(MenuSectionSpec[]? sections)
    {
        if (sections is null || sections.Length == 0)
            return Array.Empty<MenuSectionSpec>();
        return sections.Select(section => new MenuSectionSpec
        {
            Name = section.Name,
            Title = section.Title,
            Description = section.Description,
            CssClass = section.CssClass,
            Items = CloneMenuItems(section.Items),
            Columns = CloneColumns(section.Columns)
        }).ToArray();
    }

    private static MenuColumnSpec[] CloneColumns(MenuColumnSpec[]? columns)
    {
        if (columns is null || columns.Length == 0)
            return Array.Empty<MenuColumnSpec>();
        return columns.Select(column => new MenuColumnSpec
        {
            Name = column.Name,
            Title = column.Title,
            Items = CloneMenuItems(column.Items)
        }).ToArray();
    }

    private static Dictionary<string, object?>? CloneMeta(Dictionary<string, object?>? meta)
    {
        if (meta is null || meta.Count == 0)
            return null;
        return meta.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static NavigationRegionSpec[] CloneRegions(NavigationRegionSpec[]? regions)
    {
        if (regions is null || regions.Length == 0)
            return Array.Empty<NavigationRegionSpec>();
        return regions.Select(region => new NavigationRegionSpec
        {
            Name = region.Name,
            Title = region.Title,
            Menus = region.Menus?.ToArray() ?? Array.Empty<string>(),
            Items = CloneMenuItems(region.Items),
            IncludeActions = region.IncludeActions,
            CssClass = region.CssClass
        }).ToArray();
    }

    private static NavigationFooterSpec? CloneFooter(NavigationFooterSpec? footer)
    {
        if (footer is null)
            return null;
        return new NavigationFooterSpec
        {
            Label = footer.Label,
            Columns = CloneFooterColumns(footer.Columns),
            Menus = footer.Menus?.ToArray() ?? Array.Empty<string>(),
            Legal = CloneMenuItems(footer.Legal)
        };
    }

    private static NavigationFooterColumnSpec[] CloneFooterColumns(NavigationFooterColumnSpec[]? columns)
    {
        if (columns is null || columns.Length == 0)
            return Array.Empty<NavigationFooterColumnSpec>();
        return columns.Select(column => new NavigationFooterColumnSpec
        {
            Name = column.Name,
            Title = column.Title,
            Items = CloneMenuItems(column.Items)
        }).ToArray();
    }

    private static NavigationProfileSpec? ResolveNavigationProfile(NavigationSpec navSpec, NavRenderContext context)
    {
        if (navSpec.Profiles is null || navSpec.Profiles.Length == 0)
            return null;

        NavigationProfileSpec? best = null;
        var bestScore = int.MinValue;
        foreach (var profile in navSpec.Profiles)
        {
            if (profile is null)
                continue;
            if (!MatchesProfile(profile, context))
                continue;

            var score = (profile.Priority ?? 0) * 100;
            score += profile.Paths?.Length ?? 0;
            score += profile.Collections?.Length ?? 0;
            score += profile.Layouts?.Length ?? 0;
            score += profile.Projects?.Length ?? 0;

            if (best is null || score > bestScore)
            {
                best = profile;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool MatchesProfile(NavigationProfileSpec profile, NavRenderContext context)
    {
        if (profile.Paths is { Length: > 0 } && !AnyPathMatches(profile.Paths, context.Path))
            return false;

        if (profile.Collections is { Length: > 0 } &&
            !profile.Collections.Any(value => value.Equals(context.Collection, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (profile.Layouts is { Length: > 0 } &&
            !profile.Layouts.Any(value => value.Equals(context.Layout, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (profile.Projects is { Length: > 0 } &&
            !profile.Projects.Any(value => value.Equals(context.Project, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private static MenuSpec[] MergeMenus(MenuSpec[] baseMenus, MenuSpec[] profileMenus, bool inherit)
    {
        var map = new Dictionary<string, MenuSpec>(StringComparer.OrdinalIgnoreCase);
        if (inherit)
        {
            foreach (var menu in baseMenus ?? Array.Empty<MenuSpec>())
            {
                if (menu is null || string.IsNullOrWhiteSpace(menu.Name))
                    continue;
                map[menu.Name] = CloneMenu(menu);
            }
        }

        foreach (var menu in profileMenus ?? Array.Empty<MenuSpec>())
        {
            if (menu is null || string.IsNullOrWhiteSpace(menu.Name))
                continue;
            map[menu.Name] = CloneMenu(menu);
        }

        return map.Values.ToArray();
    }

    private static MenuItemSpec[] MergeItems(MenuItemSpec[] baseItems, MenuItemSpec[] profileItems, bool inherit)
    {
        if (!inherit)
            return CloneMenuItems(profileItems ?? Array.Empty<MenuItemSpec>());

        return CloneMenuItems((baseItems ?? Array.Empty<MenuItemSpec>())
            .Concat(profileItems ?? Array.Empty<MenuItemSpec>())
            .ToArray());
    }

    private static NavigationRegionSpec[] MergeRegions(NavigationRegionSpec[] baseRegions, NavigationRegionSpec[] profileRegions, bool inherit)
    {
        var map = new Dictionary<string, NavigationRegionSpec>(StringComparer.OrdinalIgnoreCase);
        if (inherit)
        {
            foreach (var region in CloneRegions(baseRegions))
            {
                if (string.IsNullOrWhiteSpace(region.Name))
                    continue;
                map[region.Name] = region;
            }
        }

        foreach (var region in CloneRegions(profileRegions))
        {
            if (string.IsNullOrWhiteSpace(region.Name))
                continue;
            map[region.Name] = region;
        }

        return map.Values.ToArray();
    }

    private static NavigationFooterSpec? MergeFooter(NavigationFooterSpec? baseFooter, NavigationFooterSpec? profileFooter, bool inherit)
    {
        if (!inherit)
            return CloneFooter(profileFooter);

        var baseClone = CloneFooter(baseFooter);
        var profileClone = CloneFooter(profileFooter);
        if (baseClone is null)
            return profileClone;
        if (profileClone is null)
            return baseClone;

        var merged = new NavigationFooterSpec
        {
            Label = string.IsNullOrWhiteSpace(profileClone.Label) ? baseClone.Label : profileClone.Label,
            Menus = baseClone.Menus
                .Concat(profileClone.Menus)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Legal = CloneMenuItems(baseClone.Legal.Concat(profileClone.Legal).ToArray())
        };

        var columnMap = new Dictionary<string, NavigationFooterColumnSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in baseClone.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name))
                continue;
            columnMap[column.Name] = new NavigationFooterColumnSpec
            {
                Name = column.Name,
                Title = column.Title,
                Items = CloneMenuItems(column.Items)
            };
        }
        foreach (var column in profileClone.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name))
                continue;
            columnMap[column.Name] = new NavigationFooterColumnSpec
            {
                Name = column.Name,
                Title = column.Title,
                Items = CloneMenuItems(column.Items)
            };
        }

        merged.Columns = columnMap.Values.ToArray();
        return merged;
    }

    private static NavigationRegion[] BuildRegions(
        NavigationRegionSpec[] regions,
        NavigationMenu[] menus,
        NavigationItem[] actions,
        NavRenderContext context,
        LinkRulesSpec? linkRules)
    {
        if (regions is null || regions.Length == 0)
            return Array.Empty<NavigationRegion>();

        var menuMap = menus.ToDictionary(menu => menu.Name, StringComparer.OrdinalIgnoreCase);
        var result = new List<NavigationRegion>();
        foreach (var region in regions)
        {
            if (region is null || string.IsNullOrWhiteSpace(region.Name))
                continue;

            var items = new List<NavigationItem>();
            foreach (var menuName in region.Menus ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(menuName))
                    continue;
                if (!menuMap.TryGetValue(menuName, out var menu))
                    continue;
                items.AddRange(menu.Items);
            }

            if (region.IncludeActions && actions.Length > 0)
                items.AddRange(actions);

            var custom = BuildMenuItems(region.Items, context, linkRules);
            if (custom.Length > 0)
                items.AddRange(custom);

            result.Add(new NavigationRegion
            {
                Name = region.Name,
                Title = region.Title,
                CssClass = region.CssClass,
                Items = items.ToArray()
            });
        }

        return result.ToArray();
    }

    private static NavigationFooter? BuildFooter(
        NavigationFooterSpec? footer,
        NavigationMenu[] menus,
        NavRenderContext context,
        LinkRulesSpec? linkRules)
    {
        if (footer is null)
            return null;

        var menuMap = menus.ToDictionary(menu => menu.Name, StringComparer.OrdinalIgnoreCase);
        var columns = new List<NavigationFooterColumn>();
        foreach (var column in footer.Columns ?? Array.Empty<NavigationFooterColumnSpec>())
        {
            if (column is null)
                continue;
            var items = BuildMenuItems(column.Items, context, linkRules);
            columns.Add(new NavigationFooterColumn
            {
                Name = column.Name,
                Title = column.Title,
                Items = items
            });
        }

        foreach (var menuName in footer.Menus ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(menuName))
                continue;
            if (!menuMap.TryGetValue(menuName, out var menu))
                continue;
            columns.Add(new NavigationFooterColumn
            {
                Name = menu.Name,
                Title = menu.Label ?? menu.Name,
                Items = menu.Items
            });
        }

        return new NavigationFooter
        {
            Label = footer.Label,
            Columns = columns.ToArray(),
            Legal = BuildMenuItems(footer.Legal, context, linkRules)
        };
    }

    private static bool IsVisible(NavigationVisibilitySpec? visibility, NavRenderContext context)
    {
        if (visibility is null)
            return true;

        if (visibility.Paths is { Length: > 0 } && !AnyPathMatches(visibility.Paths, context.Path))
            return false;

        if (visibility.ExcludePaths is { Length: > 0 } && AnyPathMatches(visibility.ExcludePaths, context.Path))
            return false;

        if (visibility.Collections is { Length: > 0 } &&
            !visibility.Collections.Any(value => value.Equals(context.Collection, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (visibility.Layouts is { Length: > 0 } &&
            !visibility.Layouts.Any(value => value.Equals(context.Layout, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (visibility.Projects is { Length: > 0 } &&
            !visibility.Projects.Any(value => value.Equals(context.Project, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private static bool AnyPathMatches(IEnumerable<string> patterns, string path)
    {
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;
            var normalized = pattern.StartsWith("/", StringComparison.Ordinal) ? pattern : "/" + pattern;
            if (GlobMatch(normalized, path))
                return true;
        }
        return false;
    }

    private readonly struct NavRenderContext
    {
        public NavRenderContext(string path, string collection, string layout, string project)
        {
            Path = path;
            Collection = collection ?? string.Empty;
            Layout = layout ?? string.Empty;
            Project = project ?? string.Empty;
        }

        public string Path { get; }
        public string Collection { get; }
        public string Layout { get; }
        public string Project { get; }
    }

    private sealed class NavNode
    {
        public NavNode(string path, string segment, int depth)
        {
            Path = path;
            Segment = segment;
            Depth = depth;
        }

        public string Path { get; }
        public string Segment { get; }
        public int Depth { get; }
        public ContentItem? Item { get; set; }
        public Dictionary<string, NavNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TocItem
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Href { get; set; }
        public string? Url { get; set; }
        public bool Hidden { get; set; }
        public TocItem[]? Items { get; set; }
    }

    private static NavigationItem[] BuildMenuItems(MenuItemSpec[] items, NavRenderContext context, LinkRulesSpec? linkRules)
    {
        if (items is null || items.Length == 0) return Array.Empty<NavigationItem>();

        var ordered = OrderMenuItems(items);
        var result = new List<NavigationItem>();
        foreach (var item in ordered)
        {
            if (!IsVisible(item.Visibility, context))
                continue;

            var url = item.Url ?? string.Empty;
            var normalized = NormalizeRouteForMatch(url);
            var isExternal = item.External ?? IsExternalUrl(url);
            var isActive = !isExternal && MatchesMenuItem(item, context.Path, normalized, exactOnly: true);
            var isAncestor = !isExternal && !isActive && MatchesMenuItem(item, context.Path, normalized, exactOnly: false);

            var target = item.Target;
            var rel = item.Rel;
            if (isExternal && string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(linkRules?.ExternalTarget))
                target = linkRules.ExternalTarget;
            if (isExternal && string.IsNullOrWhiteSpace(rel) && !string.IsNullOrWhiteSpace(linkRules?.ExternalRel))
                rel = linkRules.ExternalRel;

            var children = BuildMenuItems(item.Items, context, linkRules);
            if (children.Any(c => c.IsActive || c.IsAncestor))
                isAncestor = true;

            var sections = BuildSections(item.Sections, context, linkRules);
            if (sections.Any(section =>
                    section.Items.Any(child => child.IsActive || child.IsAncestor) ||
                    section.Columns.Any(column => column.Items.Any(child => child.IsActive || child.IsAncestor))))
                isAncestor = true;

            result.Add(new NavigationItem
            {
                Id = item.Id,
                Title = item.Title,
                Text = item.Text,
                Url = item.Url,
                Icon = item.Icon,
                IconHtml = item.IconHtml,
                Kind = item.Kind,
                Slot = item.Slot,
                Template = item.Template,
                CssClass = item.CssClass,
                AriaLabel = item.AriaLabel,
                Badge = item.Badge,
                Description = item.Description,
                Target = target,
                Rel = rel,
                External = isExternal,
                Weight = item.Weight,
                Match = item.Match,
                IsActive = isActive,
                IsAncestor = isAncestor,
                Sections = sections,
                Meta = CloneMeta(item.Meta),
                Items = children
            });
        }

        return result.ToArray();
    }

    private static NavigationSection[] BuildSections(MenuSectionSpec[] sections, NavRenderContext context, LinkRulesSpec? linkRules)
    {
        if (sections is null || sections.Length == 0)
            return Array.Empty<NavigationSection>();

        var list = new List<NavigationSection>();
        foreach (var section in sections)
        {
            if (section is null)
                continue;

            var sectionItems = BuildMenuItems(section.Items, context, linkRules);
            var sectionColumns = BuildColumns(section.Columns, context, linkRules);
            if (sectionItems.Length == 0 && sectionColumns.Length == 0 && string.IsNullOrWhiteSpace(section.Title) && string.IsNullOrWhiteSpace(section.Name))
                continue;

            list.Add(new NavigationSection
            {
                Name = section.Name,
                Title = section.Title,
                Description = section.Description,
                CssClass = section.CssClass,
                Items = sectionItems,
                Columns = sectionColumns
            });
        }

        return list.ToArray();
    }

    private static NavigationColumn[] BuildColumns(MenuColumnSpec[] columns, NavRenderContext context, LinkRulesSpec? linkRules)
    {
        if (columns is null || columns.Length == 0)
            return Array.Empty<NavigationColumn>();

        var list = new List<NavigationColumn>();
        foreach (var column in columns)
        {
            if (column is null)
                continue;

            var items = BuildMenuItems(column.Items, context, linkRules);
            if (items.Length == 0 && string.IsNullOrWhiteSpace(column.Title) && string.IsNullOrWhiteSpace(column.Name))
                continue;

            list.Add(new NavigationColumn
            {
                Name = column.Name,
                Title = column.Title,
                Items = items
            });
        }

        return list.ToArray();
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

    private static MenuItemSpec[] OrderMenuItems(MenuItemSpec[] items, string? sort)
    {
        if (items is null || items.Length == 0) return Array.Empty<MenuItemSpec>();
        if (string.IsNullOrWhiteSpace(sort))
            return OrderMenuItems(items).ToArray();

        var tokens = sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        var list = items.ToList();
        IOrderedEnumerable<MenuItemSpec>? ordered = null;
        foreach (var token in tokens)
        {
            switch (token)
            {
                case "order":
                case "weight":
                    ordered = ordered is null
                        ? list.OrderBy(i => i.Weight ?? 0)
                        : ordered.ThenBy(i => i.Weight ?? 0);
                    break;
                case "title":
                    ordered = ordered is null
                        ? list.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                        : ordered.ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase);
                    break;
                default:
                    break;
            }
        }

        return (ordered ?? list.OrderBy(i => i.Weight ?? 0).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)).ToArray();
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

    private static bool IsUnderRoot(string path, string root)
    {
        var rootNormalized = NormalizeRouteForMatch(root);
        var pathNormalized = NormalizeRouteForMatch(path);
        if (string.IsNullOrWhiteSpace(rootNormalized) || rootNormalized == "/")
            return true;
        return pathNormalized.StartsWith(rootNormalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("//", StringComparison.OrdinalIgnoreCase);
    }

    private static BreadcrumbItem[] BuildBreadcrumbs(SiteSpec spec, ContentItem item, MenuSpec[] menuSpecs)
    {
        var current = NormalizeRouteForMatch(item.OutputPath);
        var crumbs = new List<BreadcrumbItem>();
        var nav = BuildNavigation(spec, item, menuSpecs);

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

        var actionTitle = FindNavTitle(nav.Actions, route);
        if (!string.IsNullOrWhiteSpace(actionTitle))
            return actionTitle;

        foreach (var region in nav.Regions)
        {
            var title = FindNavTitle(region.Items, route);
            if (!string.IsNullOrWhiteSpace(title))
                return title;
        }

        if (nav.Footer is not null)
        {
            foreach (var column in nav.Footer.Columns)
            {
                var title = FindNavTitle(column.Items, route);
                if (!string.IsNullOrWhiteSpace(title))
                    return title;
            }

            var legalTitle = FindNavTitle(nav.Footer.Legal, route);
            if (!string.IsNullOrWhiteSpace(legalTitle))
                return legalTitle;
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

            var sectionTitle = FindNavTitle(item.Sections, route);
            if (!string.IsNullOrWhiteSpace(sectionTitle))
                return sectionTitle;

            if (item.Items.Length == 0) continue;
            var child = FindNavTitle(item.Items, route);
            if (!string.IsNullOrWhiteSpace(child))
                return child;
        }
        return null;
    }

    private static string? FindNavTitle(IEnumerable<NavigationSection> sections, string route)
    {
        foreach (var section in sections)
        {
            var sectionItemsTitle = FindNavTitle(section.Items, route);
            if (!string.IsNullOrWhiteSpace(sectionItemsTitle))
                return sectionItemsTitle;

            foreach (var column in section.Columns)
            {
                var columnTitle = FindNavTitle(column.Items, route);
                if (!string.IsNullOrWhiteSpace(columnTitle))
                    return columnTitle;
            }
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
        var head = spec.Head;
        if (head is not null)
        {
            var links = RenderHeadLinks(head);
            if (!string.IsNullOrWhiteSpace(links))
                parts.Add(links);

            var meta = RenderHeadMeta(head);
            if (!string.IsNullOrWhiteSpace(meta))
                parts.Add(meta);

            if (!string.IsNullOrWhiteSpace(head.Html))
                parts.Add(head.Html!);
        }

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

    private static string RenderHeadLinks(HeadSpec head)
    {
        if (head.Links.Length == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var link in head.Links)
        {
            if (string.IsNullOrWhiteSpace(link.Rel) || string.IsNullOrWhiteSpace(link.Href))
                continue;

            var rel = System.Web.HttpUtility.HtmlEncode(link.Rel);
            var href = System.Web.HttpUtility.HtmlEncode(link.Href);
            var type = string.IsNullOrWhiteSpace(link.Type) ? string.Empty : $" type=\"{System.Web.HttpUtility.HtmlEncode(link.Type)}\"";
            var sizes = string.IsNullOrWhiteSpace(link.Sizes) ? string.Empty : $" sizes=\"{System.Web.HttpUtility.HtmlEncode(link.Sizes)}\"";
            var cross = string.IsNullOrWhiteSpace(link.Crossorigin) ? string.Empty : $" crossorigin=\"{System.Web.HttpUtility.HtmlEncode(link.Crossorigin)}\"";
            sb.AppendLine($"<link rel=\"{rel}\" href=\"{href}\"{type}{sizes}{cross} />");
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderHeadMeta(HeadSpec head)
    {
        if (head.Meta.Length == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var meta in head.Meta)
        {
            if (string.IsNullOrWhiteSpace(meta.Content))
                continue;

            var name = string.IsNullOrWhiteSpace(meta.Name) ? string.Empty : $" name=\"{System.Web.HttpUtility.HtmlEncode(meta.Name)}\"";
            var property = string.IsNullOrWhiteSpace(meta.Property) ? string.Empty : $" property=\"{System.Web.HttpUtility.HtmlEncode(meta.Property)}\"";
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(property))
                continue;

            var content = System.Web.HttpUtility.HtmlEncode(meta.Content);
            sb.AppendLine($"<meta{name}{property} content=\"{content}\" />");
        }
        return sb.ToString().TrimEnd();
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
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
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

    private static IEnumerable<string> EnumerateCollectionFiles(string rootPath, string[] contentRoots, string input, string[]? includePatterns, string[]? excludePatterns)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var full in BuildCollectionInputCandidates(rootPath, contentRoots, input))
        {
            if (full.Contains('*', StringComparison.Ordinal))
            {
                foreach (var file in EnumerateCollectionFilesWithWildcard(full, includePatterns ?? Array.Empty<string>(), excludePatterns ?? Array.Empty<string>()))
                {
                    if (seen.Add(file))
                        results.Add(file);
                }
                continue;
            }

            if (!Directory.Exists(full))
                continue;

            foreach (var file in FilterByPatterns(full,
                         Directory.EnumerateFiles(full, "*.md", SearchOption.AllDirectories),
                         includePatterns ?? Array.Empty<string>(),
                         excludePatterns ?? Array.Empty<string>()))
            {
                if (seen.Add(file))
                    results.Add(file);
            }
        }

        return results;
    }

    private static IEnumerable<string> BuildCollectionInputCandidates(string rootPath, string[] contentRoots, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();
        if (Path.IsPathRooted(input))
            return new[] { Path.GetFullPath(input) };

        var normalizedInput = input.Trim();
        var normalizedRoot = Path.GetFullPath(rootPath);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(normalizedRoot, normalizedInput))
        };

        var inputSegments = normalizedInput.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var firstSegment = inputSegments.Length == 0 ? string.Empty : inputSegments[0];
        foreach (var contentRoot in contentRoots)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                continue;

            var fullContentRoot = Path.GetFullPath(contentRoot);
            candidates.Add(Path.GetFullPath(Path.Combine(fullContentRoot, normalizedInput)));

            if (string.IsNullOrWhiteSpace(firstSegment))
                continue;
            var rootName = Path.GetFileName(fullContentRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!firstSegment.Equals(rootName, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = normalizedInput.Substring(firstSegment.Length).TrimStart('/', '\\');
            candidates.Add(string.IsNullOrWhiteSpace(remainder)
                ? fullContentRoot
                : Path.GetFullPath(Path.Combine(fullContentRoot, remainder)));
        }

        return candidates;
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

    private static bool IsPathWithinBase(string basePath, string filePath)
    {
        var fullBase = Path.GetFullPath(basePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullFile = Path.GetFullPath(filePath);

        if (fullFile.Equals(fullBase, StringComparison.OrdinalIgnoreCase))
            return true;

        return fullFile.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
