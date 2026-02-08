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
}

