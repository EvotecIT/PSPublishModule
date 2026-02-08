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
}

