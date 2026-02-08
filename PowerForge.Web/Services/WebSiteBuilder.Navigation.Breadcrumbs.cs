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

