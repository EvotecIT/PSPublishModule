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
            menuModels = MapMenus(menuSpecs),
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

