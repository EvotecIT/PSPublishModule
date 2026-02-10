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
        nav.Surfaces = BuildSurfaces(navSpec, nav.Menus);

        return nav;
    }

    private static NavigationSurfaceRuntime[] BuildSurfaces(NavigationSpec spec, NavigationMenu[] menus)
    {
        if (spec?.Surfaces is null || spec.Surfaces.Length == 0)
            return Array.Empty<NavigationSurfaceRuntime>();

        var map = menus
            .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Name))
            .ToDictionary(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase);

        var surfaces = new List<NavigationSurfaceRuntime>();
        foreach (var surface in spec.Surfaces)
        {
            if (surface is null || string.IsNullOrWhiteSpace(surface.Name))
                continue;

            var primaryName = string.IsNullOrWhiteSpace(surface.PrimaryMenu) ? "main" : surface.PrimaryMenu.Trim();
            var sidebarName = string.IsNullOrWhiteSpace(surface.SidebarMenu) ? null : surface.SidebarMenu.Trim();
            var productsName = string.IsNullOrWhiteSpace(surface.ProductsMenu) ? null : surface.ProductsMenu.Trim();

            map.TryGetValue(primaryName, out var primary);
            var sidebar = sidebarName is not null && map.TryGetValue(sidebarName, out var s) ? s : null;
            var products = productsName is not null && map.TryGetValue(productsName, out var p) ? p : null;

            surfaces.Add(new NavigationSurfaceRuntime
            {
                Name = surface.Name.Trim(),
                Primary = primary,
                Sidebar = sidebar,
                Products = products
            });
        }

        return surfaces.ToArray();
    }
}
