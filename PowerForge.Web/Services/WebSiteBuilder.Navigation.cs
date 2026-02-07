using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
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
            if (IsGeneratedPaginationItem(item))
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
            Template = menu.Template,
            CssClass = menu.CssClass,
            Meta = CloneMeta(menu.Meta),
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
                    Template = menu.Template,
                    CssClass = menu.CssClass,
                    Meta = CloneMeta(menu.Meta),
                    Items = BuildMenuItems(menu.Items, context, spec.LinkRules)
                })
                .ToArray();
        }

        nav.Actions = BuildMenuItems(effectiveActions, context, spec.LinkRules);
        nav.Regions = BuildRegions(effectiveRegions, nav.Menus, nav.Actions, context, spec.LinkRules);
        nav.Footer = BuildFooter(effectiveFooter, nav.Menus, context, spec.LinkRules);

        return nav;
    }

    private static LocalizationRuntime BuildLocalizationRuntime(SiteSpec spec, ContentItem page, IReadOnlyList<ContentItem> allItems)
    {
        var localization = ResolveLocalizationConfig(spec);
        var currentCode = ResolveEffectiveLanguageCode(localization, page.Language);
        var currentPath = string.IsNullOrWhiteSpace(page.OutputPath) ? "/" : page.OutputPath;

        var languages = new List<LocalizationLanguageRuntime>();
        foreach (var language in localization.Languages)
        {
            var url = ResolveLocalizedPageUrl(spec, localization, page, allItems, language.Code, currentCode);
            languages.Add(new LocalizationLanguageRuntime
            {
                Code = language.Code,
                Label = language.Label,
                Prefix = language.Prefix,
                IsDefault = language.IsDefault,
                IsCurrent = language.Code.Equals(currentCode, StringComparison.OrdinalIgnoreCase),
                Url = string.IsNullOrWhiteSpace(url) ? currentPath : url
            });
        }

        if (languages.Count == 0)
        {
            languages.Add(new LocalizationLanguageRuntime
            {
                Code = currentCode,
                Label = currentCode,
                Prefix = currentCode,
                IsDefault = true,
                IsCurrent = true,
                Url = currentPath
            });
        }

        var current = languages.FirstOrDefault(l => l.IsCurrent) ?? languages[0];
        return new LocalizationRuntime
        {
            Enabled = localization.Enabled,
            Current = current,
            Languages = languages.ToArray()
        };
    }

    private static string ResolveLocalizedPageUrl(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        ContentItem page,
        IReadOnlyList<ContentItem> allItems,
        string targetLanguage,
        string currentLanguage)
    {
        if (allItems.Count > 0 && !string.IsNullOrWhiteSpace(page.TranslationKey))
        {
            var translated = allItems
                .Where(i => !i.Draft)
                .Where(i => string.Equals(i.ProjectSlug ?? string.Empty, page.ProjectSlug ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Where(i => !string.IsNullOrWhiteSpace(i.TranslationKey))
                .FirstOrDefault(i =>
                    i.TranslationKey!.Equals(page.TranslationKey, StringComparison.OrdinalIgnoreCase) &&
                    ResolveEffectiveLanguageCode(localization, i.Language).Equals(targetLanguage, StringComparison.OrdinalIgnoreCase));
            if (translated is not null)
                return translated.OutputPath;
        }

        var baseRoute = StripLanguagePrefix(localization, page.OutputPath);
        if (string.IsNullOrWhiteSpace(baseRoute))
            baseRoute = "/";

        if (!ResolveEffectiveLanguageCode(localization, targetLanguage).Equals(currentLanguage, StringComparison.OrdinalIgnoreCase))
            return ApplyLanguagePrefixToRoute(spec, baseRoute, targetLanguage);

        return ApplyLanguagePrefixToRoute(spec, baseRoute, currentLanguage);
    }

    private static string ResolveItemLanguage(
        SiteSpec spec,
        string relativePath,
        FrontMatter? matter,
        out string localizedRelativePath,
        out string localizedRelativeDir)
    {
        var localization = ResolveLocalizationConfig(spec);
        var normalizedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
        localizedRelativePath = normalizedRelativePath;
        localizedRelativeDir = NormalizePath(Path.GetDirectoryName(normalizedRelativePath) ?? string.Empty);

        if (!localization.Enabled)
            return ResolveEffectiveLanguageCode(localization, ResolveLanguageFromFrontMatter(matter));

        string? pathLanguage = null;
        if (localization.DetectFromPath && TryExtractLeadingSegment(normalizedRelativePath, out var segment, out var remainder))
        {
            if (TryResolveConfiguredLanguage(localization, segment, matchByPrefix: true, out var fromPath))
            {
                pathLanguage = fromPath;
                localizedRelativePath = remainder;
                localizedRelativeDir = NormalizePath(Path.GetDirectoryName(remainder) ?? string.Empty);
            }
        }

        var frontMatterLanguage = ResolveLanguageFromFrontMatter(matter);
        if (TryResolveConfiguredLanguage(localization, frontMatterLanguage, matchByPrefix: true, out var resolvedFrontMatterLanguage))
            return resolvedFrontMatterLanguage;

        return !string.IsNullOrWhiteSpace(pathLanguage)
            ? pathLanguage
            : localization.DefaultLanguage;
    }

    private static string ResolveLanguageFromFrontMatter(FrontMatter? matter)
    {
        if (matter?.Meta is null || matter.Meta.Count == 0)
            return string.Empty;

        if (TryGetMetaString(matter.Meta, "language", out var language))
            return NormalizeLanguageToken(language);
        if (TryGetMetaString(matter.Meta, "lang", out language))
            return NormalizeLanguageToken(language);
        if (TryGetMetaString(matter.Meta, "i18n.language", out language))
            return NormalizeLanguageToken(language);
        if (TryGetMetaString(matter.Meta, "i18n.lang", out language))
            return NormalizeLanguageToken(language);

        return string.Empty;
    }

    private static string ResolveTranslationKey(FrontMatter? matter, string? collectionName, string localizedRelativePath)
    {
        if (matter?.Meta is not null)
        {
            if (TryGetMetaString(matter.Meta, "translation_key", out var translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
            if (TryGetMetaString(matter.Meta, "translation.key", out translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
            if (TryGetMetaString(matter.Meta, "i18n.key", out translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
        }

        var collection = string.IsNullOrWhiteSpace(collectionName)
            ? "content"
            : collectionName.Trim().ToLowerInvariant();
        var path = NormalizePath(Path.ChangeExtension(localizedRelativePath, null) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(path))
            path = "index";
        return $"{collection}:{path.ToLowerInvariant()}";
    }

    private static string ApplyLanguagePrefixToRoute(SiteSpec spec, string route, string? language)
    {
        var localization = ResolveLocalizationConfig(spec);
        if (!localization.Enabled)
            return route;

        var languageCode = ResolveEffectiveLanguageCode(localization, language);
        if (!localization.ByCode.TryGetValue(languageCode, out var languageSpec))
            return route;

        if (languageSpec.IsDefault && !localization.PrefixDefaultLanguage)
            return route;

        var prefix = NormalizePath(languageSpec.Prefix);
        if (string.IsNullOrWhiteSpace(prefix))
            return route;

        var stripped = StripLanguagePrefix(localization, route);
        var withoutLeadingSlash = stripped.TrimStart('/');
        var prefixed = string.IsNullOrWhiteSpace(withoutLeadingSlash)
            ? "/" + prefix
            : "/" + prefix + "/" + withoutLeadingSlash;

        return EnsureTrailingSlash(prefixed, spec.TrailingSlash);
    }

    private static string StripLanguagePrefix(ResolvedLocalizationConfig localization, string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return "/";

        var normalized = route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route;
        if (!TryExtractLeadingSegment(normalized.TrimStart('/'), out var segment, out var remainder))
            return normalized;

        var token = NormalizeLanguageToken(segment);
        if (string.IsNullOrWhiteSpace(token))
            return normalized;

        if (!localization.ByPrefix.ContainsKey(token))
            return normalized;

        return string.IsNullOrWhiteSpace(remainder) ? "/" : "/" + remainder;
    }

    private static bool TryExtractLeadingSegment(string value, out string segment, out string remainder)
    {
        segment = string.Empty;
        remainder = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var slash = normalized.IndexOf('/');
        if (slash < 0)
        {
            segment = normalized;
            remainder = string.Empty;
            return true;
        }

        segment = normalized.Substring(0, slash);
        remainder = normalized[(slash + 1)..];
        return true;
    }

    private static ResolvedLocalizationConfig ResolveLocalizationConfig(SiteSpec spec)
    {
        var localizationSpec = spec.Localization;
        var defaultLanguage = NormalizeLanguageToken(localizationSpec?.DefaultLanguage);
        if (string.IsNullOrWhiteSpace(defaultLanguage))
            defaultLanguage = "en";

        var entries = new List<ResolvedLocalizationLanguage>();
        if (localizationSpec?.Languages is { Length: > 0 })
        {
            foreach (var language in localizationSpec.Languages)
            {
                if (language is null || language.Disabled)
                    continue;

                var code = NormalizeLanguageToken(language.Code);
                if (string.IsNullOrWhiteSpace(code) || entries.Any(e => e.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var prefix = NormalizePath(string.IsNullOrWhiteSpace(language.Prefix) ? code : language.Prefix);
                if (string.IsNullOrWhiteSpace(prefix))
                    prefix = code;

                entries.Add(new ResolvedLocalizationLanguage
                {
                    Code = code,
                    Label = string.IsNullOrWhiteSpace(language.Label) ? code : language.Label.Trim(),
                    Prefix = prefix,
                    IsDefault = language.Default
                });
            }
        }

        if (entries.Count == 0)
        {
            entries.Add(new ResolvedLocalizationLanguage
            {
                Code = defaultLanguage,
                Label = defaultLanguage,
                Prefix = defaultLanguage,
                IsDefault = true
            });
        }

        var explicitDefault = entries.FirstOrDefault(e => e.IsDefault);
        if (explicitDefault is null)
            explicitDefault = entries.FirstOrDefault(e => e.Code.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase)) ?? entries[0];

        foreach (var entry in entries)
            entry.IsDefault = entry.Code.Equals(explicitDefault.Code, StringComparison.OrdinalIgnoreCase);

        var byCode = new Dictionary<string, ResolvedLocalizationLanguage>(StringComparer.OrdinalIgnoreCase);
        var byPrefix = new Dictionary<string, ResolvedLocalizationLanguage>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!byCode.ContainsKey(entry.Code))
                byCode[entry.Code] = entry;

            var normalizedPrefix = NormalizeLanguageToken(entry.Prefix);
            if (!string.IsNullOrWhiteSpace(normalizedPrefix) && !byPrefix.ContainsKey(normalizedPrefix))
                byPrefix[normalizedPrefix] = entry;
        }

        return new ResolvedLocalizationConfig
        {
            Enabled = localizationSpec?.Enabled == true && byCode.Count > 0,
            DetectFromPath = localizationSpec?.DetectFromPath ?? true,
            PrefixDefaultLanguage = localizationSpec?.PrefixDefaultLanguage == true,
            DefaultLanguage = explicitDefault.Code,
            Languages = entries.ToArray(),
            ByCode = byCode,
            ByPrefix = byPrefix
        };
    }

    private static string ResolveEffectiveLanguageCode(ResolvedLocalizationConfig localization, string? language)
    {
        if (TryResolveConfiguredLanguage(localization, language, matchByPrefix: true, out var resolved))
            return resolved;
        return localization.DefaultLanguage;
    }

    private static bool TryResolveConfiguredLanguage(
        ResolvedLocalizationConfig localization,
        string? language,
        bool matchByPrefix,
        out string resolved)
    {
        resolved = string.Empty;
        var token = NormalizeLanguageToken(language);
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (localization.ByCode.TryGetValue(token, out var byCode))
        {
            resolved = byCode.Code;
            return true;
        }

        if (matchByPrefix && localization.ByPrefix.TryGetValue(token, out var byPrefix))
        {
            resolved = byPrefix.Code;
            return true;
        }

        return false;
    }

    private static string NormalizeLanguageToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Trim().Replace('_', '-').Trim('/').ToLowerInvariant();
    }

    private sealed class ResolvedLocalizationConfig
    {
        public bool Enabled { get; init; }
        public bool DetectFromPath { get; init; }
        public bool PrefixDefaultLanguage { get; init; }
        public string DefaultLanguage { get; init; } = "en";
        public ResolvedLocalizationLanguage[] Languages { get; init; } = Array.Empty<ResolvedLocalizationLanguage>();
        public Dictionary<string, ResolvedLocalizationLanguage> ByCode { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ResolvedLocalizationLanguage> ByPrefix { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ResolvedLocalizationLanguage
    {
        public string Code { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string Prefix { get; init; } = string.Empty;
        public bool IsDefault { get; set; }
    }

    private static VersioningRuntime BuildVersioningRuntime(SiteSpec spec, string? currentPath)
    {
        var versioning = spec.Versioning;
        if (versioning is null || !versioning.Enabled || versioning.Versions is null || versioning.Versions.Length == 0)
            return new VersioningRuntime();

        var versionMap = new Dictionary<string, VersionRuntimeItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in versioning.Versions)
        {
            if (version is null || string.IsNullOrWhiteSpace(version.Name))
                continue;

            if (versionMap.ContainsKey(version.Name))
                continue;

            versionMap[version.Name] = new VersionRuntimeItem
            {
                Name = version.Name.Trim(),
                Label = string.IsNullOrWhiteSpace(version.Label) ? version.Name.Trim() : version.Label.Trim(),
                Url = ResolveVersionUrl(versioning.BasePath, version),
                Default = version.Default,
                Latest = version.Latest,
                Deprecated = version.Deprecated
            };
        }

        var versions = versionMap.Values.ToArray();
        if (versions.Length == 0)
            return new VersioningRuntime();

        var current = ResolveCurrentVersion(versioning.Current, currentPath, versions);
        var latest = versions.FirstOrDefault(v => v.Latest) ?? versions.FirstOrDefault(v => v.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase)) ?? versions[0];
        var @default = versions.FirstOrDefault(v => v.Default) ?? versions[0];

        foreach (var version in versions)
            version.IsCurrent = version.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase);

        return new VersioningRuntime
        {
            Enabled = true,
            BasePath = NormalizeVersionBasePath(versioning.BasePath),
            Current = current,
            Latest = latest,
            Default = @default,
            Versions = versions
        };
    }

    private static VersionRuntimeItem ResolveCurrentVersion(string? configuredCurrent, string? currentPath, VersionRuntimeItem[] versions)
    {
        if (!string.IsNullOrWhiteSpace(configuredCurrent))
        {
            var configured = versions.FirstOrDefault(v => v.Name.Equals(configuredCurrent.Trim(), StringComparison.OrdinalIgnoreCase));
            if (configured is not null)
                return configured;
        }

        var normalizedCurrentPath = NormalizeRouteForMatch(currentPath);
        if (!string.IsNullOrWhiteSpace(normalizedCurrentPath))
        {
            var inferred = versions
                .Select(v => new
                {
                    Version = v,
                    Url = NormalizeRouteForMatch(v.Url)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Url) && normalizedCurrentPath.StartsWith(x.Url, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Url.Length)
                .Select(x => x.Version)
                .FirstOrDefault();
            if (inferred is not null)
                return inferred;
        }

        return versions.FirstOrDefault(v => v.Default) ??
               versions.FirstOrDefault(v => v.Latest) ??
               versions[0];
    }

    private static string ResolveVersionUrl(string? basePath, VersionSpec version)
    {
        if (!string.IsNullOrWhiteSpace(version.Url))
            return NormalizeRouteForMatch(version.Url);

        var versionName = version.Name.Trim('/');
        if (string.IsNullOrWhiteSpace(versionName))
            return NormalizeRouteForMatch(basePath);

        var normalizedBasePath = NormalizeVersionBasePath(basePath);
        if (string.IsNullOrWhiteSpace(normalizedBasePath) || normalizedBasePath == "/")
            return NormalizeRouteForMatch("/" + versionName + "/");

        return NormalizeRouteForMatch($"{normalizedBasePath}/{versionName}/");
    }

    private static string NormalizeVersionBasePath(string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return string.Empty;

        var normalized = NormalizeRouteForMatch(basePath);
        return normalized == "/"
            ? "/"
            : normalized.TrimEnd('/');
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
            Template = region.Template,
            CssClass = region.CssClass,
            Meta = CloneMeta(region.Meta)
        }).ToArray();
    }

    private static NavigationFooterSpec? CloneFooter(NavigationFooterSpec? footer)
    {
        if (footer is null)
            return null;
        return new NavigationFooterSpec
        {
            Label = footer.Label,
            Template = footer.Template,
            CssClass = footer.CssClass,
            Meta = CloneMeta(footer.Meta),
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
            Template = column.Template,
            CssClass = column.CssClass,
            Meta = CloneMeta(column.Meta),
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
            Template = string.IsNullOrWhiteSpace(profileClone.Template) ? baseClone.Template : profileClone.Template,
            CssClass = string.IsNullOrWhiteSpace(profileClone.CssClass) ? baseClone.CssClass : profileClone.CssClass,
            Meta = profileClone.Meta?.Count > 0 ? CloneMeta(profileClone.Meta) : CloneMeta(baseClone.Meta),
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
                Template = column.Template,
                CssClass = column.CssClass,
                Meta = CloneMeta(column.Meta),
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
                Template = column.Template,
                CssClass = column.CssClass,
                Meta = CloneMeta(column.Meta),
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
                Template = region.Template,
                CssClass = region.CssClass,
                Meta = CloneMeta(region.Meta),
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
                Template = column.Template,
                CssClass = column.CssClass,
                Meta = CloneMeta(column.Meta),
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
                Template = menu.Template,
                CssClass = menu.CssClass,
                Meta = CloneMeta(menu.Meta),
                Items = menu.Items
            });
        }

        return new NavigationFooter
        {
            Label = footer.Label,
            Template = footer.Template,
            CssClass = footer.CssClass,
            Meta = CloneMeta(footer.Meta),
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


}

