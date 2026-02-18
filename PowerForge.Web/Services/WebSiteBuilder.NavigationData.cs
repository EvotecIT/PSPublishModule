using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Navigation payload projection helpers.</summary>
public static partial class WebSiteBuilder
{
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
        Directory.CreateDirectory(dataDir);
        var json = BuildSiteNavJson(spec, menuSpecs);
        WriteAllTextIfChanged(outputPath, json);
    }

    private static string BuildSiteNavJson(SiteSpec spec, MenuSpec[] menuSpecs)
    {
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
            schemaVersion = 1,
            generated = true,
            primary,
            menus,
            menuModels = MapMenus(menuSpecs),
            footer = footer.Count > 0 ? footer : null,
            actions = MapMenuItems(spec.Navigation?.Actions ?? Array.Empty<MenuItemSpec>()),
            regions = MapRegions(spec.Navigation?.Regions ?? Array.Empty<NavigationRegionSpec>()),
            footerModel = MapFooter(spec.Navigation?.Footer),
            profiles = MapProfiles(spec.Navigation?.Profiles ?? Array.Empty<NavigationProfileSpec>()),
            surfaces = MapSurfaces(spec, menuSpecs)
        };

        return JsonSerializer.Serialize(payload, WebJson.Options);
    }

    private static object? MapSurfaces(SiteSpec spec, MenuSpec[] menuSpecs)
    {
        if (spec is null)
            return null;

        var navSpec = spec.Navigation;
        if (navSpec is null)
            return null;

        var surfaces = ResolveSurfaceSpecs(spec, menuSpecs);
        if (surfaces.Length == 0)
            return null;

        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var surface in surfaces)
        {
            if (surface is null || string.IsNullOrWhiteSpace(surface.Name))
                continue;

            var name = surface.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var contextPath = NormalizeRouteForMatch(string.IsNullOrWhiteSpace(surface.Path) ? "/" : surface.Path);
            var contextCollection = surface.Collection ?? InferSurfaceCollection(spec, name) ?? string.Empty;
            var contextLayout = surface.Layout ?? InferSurfaceLayout(name) ?? string.Empty;
            var contextProject = surface.Project ?? string.Empty;
            var context = new NavRenderContext(contextPath, contextCollection, contextLayout, contextProject);

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

            var fullMenuMap = effectiveMenus.ToDictionary(
                m => m.Name,
                m => MapMenuItems(m.Items),
                StringComparer.OrdinalIgnoreCase);

            var primaryMenuName = string.IsNullOrWhiteSpace(surface.PrimaryMenu) ? "main" : surface.PrimaryMenu.Trim();
            var sidebarMenuName = string.IsNullOrWhiteSpace(surface.SidebarMenu) ? null : surface.SidebarMenu.Trim();
            var productsMenuName = string.IsNullOrWhiteSpace(surface.ProductsMenu) ? null : surface.ProductsMenu.Trim();

            var includeMenus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(primaryMenuName))
                includeMenus.Add(primaryMenuName);
            if (!string.IsNullOrWhiteSpace(sidebarMenuName))
                includeMenus.Add(sidebarMenuName!);
            if (!string.IsNullOrWhiteSpace(productsMenuName))
                includeMenus.Add(productsMenuName!);

            foreach (var menu in effectiveMenus)
            {
                if (menu is null || string.IsNullOrWhiteSpace(menu.Name))
                    continue;
                if (menu.Name.StartsWith("footer", StringComparison.OrdinalIgnoreCase))
                    includeMenus.Add(menu.Name);
            }

            var menuSubset = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var menuName in includeMenus)
            {
                if (string.IsNullOrWhiteSpace(menuName))
                    continue;
                if (fullMenuMap.TryGetValue(menuName, out var items))
                    menuSubset[menuName] = items;
            }

            var primaryItems = menuSubset.TryGetValue(primaryMenuName, out var primaryResolved)
                ? primaryResolved
                : Array.Empty<object>();

            object[]? sidebarItems = null;
            if (!string.IsNullOrWhiteSpace(sidebarMenuName) && menuSubset.TryGetValue(sidebarMenuName!, out var sidebarResolved))
                sidebarItems = sidebarResolved;

            object[]? productsItems = null;
            if (!string.IsNullOrWhiteSpace(productsMenuName) && menuSubset.TryGetValue(productsMenuName!, out var productsResolved))
                productsItems = productsResolved;

            var footerMenus = menuSubset
                .Where(kvp => kvp.Key.StartsWith("footer", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            result[name] = new
            {
                context = new
                {
                    path = contextPath,
                    collection = string.IsNullOrWhiteSpace(contextCollection) ? null : contextCollection,
                    layout = string.IsNullOrWhiteSpace(contextLayout) ? null : contextLayout,
                    project = string.IsNullOrWhiteSpace(contextProject) ? null : contextProject
                },
                profile = activeProfile?.Name,
                primaryMenu = primaryMenuName,
                sidebarMenu = sidebarMenuName,
                productsMenu = productsMenuName,
                primary = primaryItems,
                sidebar = sidebarItems,
                products = productsItems,
                menus = menuSubset.Count > 0 ? menuSubset : null,
                footer = footerMenus.Count > 0 ? footerMenus : null,
                actions = MapMenuItems(effectiveActions),
                regions = MapRegions(effectiveRegions),
                footerModel = MapFooter(effectiveFooter)
            };
        }

        return result.Count == 0 ? null : result;
    }

    private static NavigationSurfaceSpec[] ResolveSurfaceSpecs(SiteSpec spec, MenuSpec[] menuSpecs)
    {
        var navSpec = spec.Navigation;
        if (navSpec is null)
            return Array.Empty<NavigationSurfaceSpec>();

        var map = new Dictionary<string, NavigationSurfaceSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var surface in navSpec.Surfaces ?? Array.Empty<NavigationSurfaceSpec>())
        {
            if (surface is null || string.IsNullOrWhiteSpace(surface.Name))
                continue;
            map[surface.Name.Trim()] = surface;
        }

        var enabledFeatures = NormalizeFeatures(spec.Features);
        var hasDocs = enabledFeatures.Contains("docs") ||
                      menuSpecs.Any(m => string.Equals(m.Name, "docs", StringComparison.OrdinalIgnoreCase)) ||
                      (spec.Collections is not null && spec.Collections.Any(c => c is not null &&
                          !string.IsNullOrWhiteSpace(c.Output) &&
                          c.Output.StartsWith("/docs", StringComparison.OrdinalIgnoreCase)));
        var hasApi = enabledFeatures.Contains("apidocs") || ContainsRouteBundleMatch(spec.AssetRegistry, "/api/**") ||
                     menuSpecs.Any(m => m.Items is not null && ContainsUrlPrefix(m.Items, "/api"));
        var hasProducts = menuSpecs.Any(m => string.Equals(m.Name, "products", StringComparison.OrdinalIgnoreCase));

        AddSurfaceIfMissing(map, new NavigationSurfaceSpec
        {
            Name = "main",
            Path = "/",
            PrimaryMenu = "main"
        });

        if (hasDocs)
        {
            var hasDocsMenu = menuSpecs.Any(m => string.Equals(m.Name, "docs", StringComparison.OrdinalIgnoreCase));
            AddSurfaceIfMissing(map, new NavigationSurfaceSpec
            {
                Name = "docs",
                Path = "/docs/",
                Collection = "docs",
                Layout = "docs",
                PrimaryMenu = "main",
                SidebarMenu = hasDocsMenu ? "docs" : null
            });
        }

        if (hasApi)
        {
            AddSurfaceIfMissing(map, new NavigationSurfaceSpec
            {
                Name = "apidocs",
                Path = "/api/",
                Layout = "apiDocs",
                PrimaryMenu = "main"
            });
        }

        if (hasProducts)
        {
            AddSurfaceIfMissing(map, new NavigationSurfaceSpec
            {
                Name = "products",
                Path = "/",
                PrimaryMenu = "main",
                ProductsMenu = "products"
            });
        }

        return map.Values.ToArray();
    }

    private static void AddSurfaceIfMissing(Dictionary<string, NavigationSurfaceSpec> map, NavigationSurfaceSpec surface)
    {
        if (map is null || surface is null || string.IsNullOrWhiteSpace(surface.Name))
            return;
        var key = surface.Name.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return;
        if (!map.ContainsKey(key))
            map[key] = surface;
    }

    private static HashSet<string> NormalizeFeatures(string[]? features)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (features is null || features.Length == 0)
            return set;

        foreach (var feature in features)
        {
            var normalized = NormalizeFeatureName(feature);
            if (!string.IsNullOrWhiteSpace(normalized))
                set.Add(normalized);
        }

        return set;
    }

    private static string NormalizeFeatureName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.Equals("apiDocs", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("apidocs", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("api", StringComparison.OrdinalIgnoreCase))
            return "apidocs";

        if (trimmed.Equals("notFound", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("404", StringComparison.OrdinalIgnoreCase))
            return "notfound";

        return trimmed.ToLowerInvariant();
    }

    private static bool ContainsRouteBundleMatch(AssetRegistrySpec? assets, string match)
    {
        if (assets?.RouteBundles is null || assets.RouteBundles.Length == 0)
            return false;

        foreach (var mapping in assets.RouteBundles)
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Match))
                continue;
            if (string.Equals(mapping.Match.Trim(), match, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsUrlPrefix(MenuItemSpec[] items, string prefix)
    {
        if (items is null || items.Length == 0) return false;
        foreach (var item in items)
        {
            if (item is null) continue;
            if (!string.IsNullOrWhiteSpace(item.Url) && item.Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
            if (ContainsUrlPrefix(item.Items, prefix))
                return true;
        }
        return false;
    }

    private static string? InferSurfaceCollection(SiteSpec spec, string name)
    {
        if (spec is null || string.IsNullOrWhiteSpace(name))
            return null;

        if (name.Equals("docs", StringComparison.OrdinalIgnoreCase))
            return "docs";
        if (name.Equals("blog", StringComparison.OrdinalIgnoreCase))
            return "blog";
        if (name.Equals("news", StringComparison.OrdinalIgnoreCase))
            return "news";
        return null;
    }

    private static string? InferSurfaceLayout(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (name.Equals("docs", StringComparison.OrdinalIgnoreCase))
            return "docs";
        if (name.Equals("apidocs", StringComparison.OrdinalIgnoreCase) || name.Equals("api", StringComparison.OrdinalIgnoreCase))
            return "apiDocs";
        return null;
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

    private static object[] MapMenus(MenuSpec[] menus)
    {
        if (menus is null || menus.Length == 0) return Array.Empty<object>();
        return menus
            .Where(menu => menu is not null && !string.IsNullOrWhiteSpace(menu.Name))
            .Select(menu => new
            {
                name = menu.Name,
                label = menu.Label,
                template = menu.Template,
                @class = menu.CssClass,
                visibility = MapVisibility(menu.Visibility),
                meta = menu.Meta,
                items = MapMenuItems(menu.Items)
            })
            .ToArray();
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
                template = region.Template,
                @class = region.CssClass,
                meta = region.Meta,
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
            template = footer.Template,
            @class = footer.CssClass,
            meta = footer.Meta,
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
                template = column.Template,
                @class = column.CssClass,
                meta = column.Meta,
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
                        template = menu.Template,
                        @class = menu.CssClass,
                        visibility = MapVisibility(menu.Visibility),
                        meta = menu.Meta,
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
}

