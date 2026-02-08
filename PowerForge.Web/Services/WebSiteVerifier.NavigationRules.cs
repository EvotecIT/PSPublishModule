using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Navigation lint and route matching verification rules.</summary>
public static partial class WebSiteVerifier
{
    private static void ValidateNavigationDefaults(SiteSpec spec, List<string> warnings)
    {
        if (spec is null || warnings is null) return;

        var nav = spec.Navigation;
        if (nav is null) return;

        var hasMenus = nav.Menus is not null && nav.Menus.Length > 0;
        var hasAuto = nav.Auto is not null && nav.Auto.Length > 0;
        if (!hasMenus && !hasAuto && !nav.AutoDefaults)
        {
            warnings.Add("Navigation.AutoDefaults is disabled and no menus/auto navigation are defined.");
        }

        if (hasMenus)
        {
            var menus = nav.Menus!;
            var mainMenu = menus.FirstOrDefault(menu => string.Equals(menu.Name, "main", StringComparison.OrdinalIgnoreCase));
            if (mainMenu?.Items is { Length: > 0 })
            {
                var hasHome = mainMenu.Items.Any(item =>
                    string.Equals(item.Url, "/", StringComparison.OrdinalIgnoreCase));
                if (!hasHome)
                    warnings.Add("Navigation main menu does not contain '/'. Add a Home link to keep global navigation consistent.");
            }
        }
    }


    private static void ValidateVersioning(SiteSpec spec, List<string> warnings)
    {
        if (spec is null || warnings is null)
            return;

        var versioning = spec.Versioning;
        if (versioning is null || !versioning.Enabled)
            return;

        if (versioning.Versions is null || versioning.Versions.Length == 0)
        {
            warnings.Add("Versioning is enabled but no versions are configured.");
            return;
        }

        var normalizedBasePath = string.IsNullOrWhiteSpace(versioning.BasePath)
            ? string.Empty
            : NormalizeRouteForNavigationMatch(versioning.BasePath);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultCount = 0;
        var latestCount = 0;

        foreach (var version in versioning.Versions)
        {
            if (version is null || string.IsNullOrWhiteSpace(version.Name))
            {
                warnings.Add("Versioning contains an entry with missing Name.");
                continue;
            }

            var versionName = version.Name.Trim();
            if (!names.Add(versionName))
            {
                warnings.Add($"Versioning defines duplicate version '{versionName}'.");
                continue;
            }

            if (version.Default) defaultCount++;
            if (version.Latest) latestCount++;

            if (string.IsNullOrWhiteSpace(version.Url))
                continue;

            var url = version.Url.Trim();
            if (!url.StartsWith("/", StringComparison.Ordinal) && !IsExternalNavigationUrl(url))
            {
                warnings.Add($"Versioning version '{versionName}' url '{version.Url}' should be root-relative ('/docs/v2/') or absolute ('https://...').");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalizedBasePath) &&
                normalizedBasePath != "/" &&
                !IsExternalNavigationUrl(url))
            {
                var normalizedUrl = NormalizeRouteForNavigationMatch(url);
                if (!normalizedUrl.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"Versioning version '{versionName}' url '{version.Url}' is outside Versioning.BasePath '{versioning.BasePath}'.");
            }
        }

        if (!string.IsNullOrWhiteSpace(versioning.Current) &&
            !names.Contains(versioning.Current.Trim()))
        {
            warnings.Add($"Versioning.Current '{versioning.Current}' does not match any configured version name.");
        }

        if (defaultCount > 1)
            warnings.Add($"Versioning marks {defaultCount} entries as Default. Use only one.");
        if (latestCount > 1)
            warnings.Add($"Versioning marks {latestCount} entries as Latest. Use only one.");
        if (defaultCount == 0)
            warnings.Add("Versioning does not mark any version as Default. The first version will be used.");
        if (latestCount == 0)
            warnings.Add("Versioning does not mark any version as Latest. The current/default version will be used.");
    }


    private static void ValidateNavigationLint(SiteSpec spec, WebSitePlan plan, IEnumerable<string> routes, List<string> warnings)
    {
        if (spec is null || plan is null || routes is null || warnings is null) return;
        var nav = spec.Navigation;
        if (nav is null) return;

        var knownRoutes = routes
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .Select(NormalizeRouteForNavigationMatch)
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var knownCollections = (spec.Collections ?? Array.Empty<CollectionSpec>())
            .Where(collection => !string.IsNullOrWhiteSpace(collection?.Name))
            .Select(collection => collection!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var routeScopedPrefixes = (spec.Collections ?? Array.Empty<CollectionSpec>())
            .Select(collection => NormalizeRouteForNavigationMatch(collection?.Output))
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix) && !string.Equals(prefix, "/", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var knownProjects = (plan.Projects ?? Array.Empty<WebProjectPlan>())
            .Where(project => !string.IsNullOrWhiteSpace(project?.Slug))
            .Select(project => project!.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var itemIdLocations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var baseMenus = nav.Menus ?? Array.Empty<MenuSpec>();
        var baseMenuNames = CollectMenuNames(baseMenus, "Navigation.Menus", warnings);

        foreach (var menu in baseMenus)
        {
            if (menu is null || string.IsNullOrWhiteSpace(menu.Name))
                continue;

            var menuContext = $"Navigation.Menus['{menu.Name}']";
            ValidateVisibilityPatterns(menu.Visibility, menuContext + ".Visibility", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, warnings);
            ValidateMenuItemsForLint(menu.Items, menuContext + ".Items", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
        }

        ValidateMenuItemsForLint(nav.Actions ?? Array.Empty<MenuItemSpec>(), "Navigation.Actions", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
        ValidateNavigationRegions(nav.Regions ?? Array.Empty<NavigationRegionSpec>(), baseMenuNames, "Navigation.Regions", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
        ValidateNavigationFooter(nav.Footer, baseMenuNames, "Navigation.Footer", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);

        var profiles = nav.Profiles ?? Array.Empty<NavigationProfileSpec>();
        var profileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < profiles.Length; i++)
        {
            var profile = profiles[i];
            if (profile is null)
                continue;

            var profileName = string.IsNullOrWhiteSpace(profile.Name)
                ? $"profile#{i + 1}"
                : profile.Name;
            var profileContext = $"Navigation.Profiles['{profileName}']";

            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                warnings.Add($"Navigation lint: Navigation.Profiles[{i}] should set 'Name' for diagnostics and maintainability.");
            }
            else if (!profileNames.Add(profile.Name))
            {
                warnings.Add($"Navigation lint: duplicate profile name '{profile.Name}' in Navigation.Profiles.");
            }

            var hasFilter = (profile.Paths?.Length ?? 0) > 0 ||
                            (profile.Collections?.Length ?? 0) > 0 ||
                            (profile.Layouts?.Length ?? 0) > 0 ||
                            (profile.Projects?.Length ?? 0) > 0;
            if (!hasFilter)
            {
                warnings.Add($"Navigation lint: {profileContext} has no selectors (paths/collections/layouts/projects). It will apply globally.");
            }

            if (profile.Paths is { Length: > 0 } && knownRoutes.Length > 0)
            {
                var hasRouteHit = profile.Paths.Any(path => ShouldValidateRouteCoverage(path, routeScopedPrefixes) && PatternMatchesAnyRoute(path, knownRoutes));
                if (!hasRouteHit)
                    warnings.Add($"Navigation lint: {profileContext}.Paths do not match any generated routes.");
            }

            if (profile.Collections is { Length: > 0 })
            {
                foreach (var collectionName in profile.Collections.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    if (!knownCollections.Contains(collectionName))
                        warnings.Add($"Navigation lint: {profileContext}.Collections references unknown collection '{collectionName}'.");
                }
            }

            if (profile.Projects is { Length: > 0 } && knownProjects.Count > 0)
            {
                foreach (var projectName in profile.Projects.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    if (!knownProjects.Contains(projectName))
                        warnings.Add($"Navigation lint: {profileContext}.Projects references unknown project '{projectName}'.");
                }
            }

            var profileMenus = profile.Menus ?? Array.Empty<MenuSpec>();
            var profileMenuNames = CollectMenuNames(profileMenus, profileContext + ".Menus", warnings);
            var visibleMenus = new HashSet<string>(profile.InheritMenus ? baseMenuNames : Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            visibleMenus.UnionWith(profileMenuNames);

            foreach (var menu in profileMenus)
            {
                if (menu is null || string.IsNullOrWhiteSpace(menu.Name))
                    continue;

                var menuContext = $"{profileContext}.Menus['{menu.Name}']";
                ValidateVisibilityPatterns(menu.Visibility, menuContext + ".Visibility", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, warnings);
                ValidateMenuItemsForLint(menu.Items, menuContext + ".Items", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
            }

            ValidateMenuItemsForLint(profile.Actions ?? Array.Empty<MenuItemSpec>(), profileContext + ".Actions", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
            ValidateNavigationRegions(profile.Regions ?? Array.Empty<NavigationRegionSpec>(), visibleMenus, profileContext + ".Regions", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
            ValidateNavigationFooter(profile.Footer, visibleMenus, profileContext + ".Footer", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
        }
    }


    private static HashSet<string> CollectMenuNames(IEnumerable<MenuSpec> menus, string context, List<string> warnings)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var menu in menus)
        {
            if (menu is null)
            {
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(menu.Name))
            {
                warnings.Add($"Navigation lint: {context}[{index}] has an empty menu name.");
                index++;
                continue;
            }

            if (!names.Add(menu.Name))
                warnings.Add($"Navigation lint: duplicate menu name '{menu.Name}' in {context}.");

            index++;
        }
        return names;
    }


    private static void ValidateNavigationRegions(
        IEnumerable<NavigationRegionSpec> regions,
        HashSet<string> knownMenuNames,
        string context,
        string[] knownRoutes,
        string[] routeScopedPrefixes,
        HashSet<string> knownCollections,
        HashSet<string> knownProjects,
        Dictionary<string, string> itemIdLocations,
        List<string> warnings)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var region in regions)
        {
            if (region is null)
            {
                index++;
                continue;
            }

            var regionName = string.IsNullOrWhiteSpace(region.Name) ? $"region#{index + 1}" : region.Name;
            var regionContext = $"{context}['{regionName}']";

            if (string.IsNullOrWhiteSpace(region.Name))
            {
                warnings.Add($"Navigation lint: {context}[{index}] has an empty region name.");
            }
            else if (!seenNames.Add(region.Name))
            {
                warnings.Add($"Navigation lint: duplicate region name '{region.Name}' in {context}.");
            }

            foreach (var menuName in region.Menus ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(menuName))
                    continue;
                if (!knownMenuNames.Contains(menuName))
                    warnings.Add($"Navigation lint: {regionContext} references unknown menu '{menuName}'.");
            }

            if ((region.Menus?.Length ?? 0) == 0 &&
                (region.Items?.Length ?? 0) == 0 &&
                !region.IncludeActions)
            {
                warnings.Add($"Navigation lint: {regionContext} is empty (no menus, no items, IncludeActions=false).");
            }

            ValidateMenuItemsForLint(region.Items ?? Array.Empty<MenuItemSpec>(), regionContext + ".Items", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
            index++;
        }
    }


    private static void ValidateNavigationFooter(
        NavigationFooterSpec? footer,
        HashSet<string> knownMenuNames,
        string context,
        string[] knownRoutes,
        string[] routeScopedPrefixes,
        HashSet<string> knownCollections,
        HashSet<string> knownProjects,
        Dictionary<string, string> itemIdLocations,
        List<string> warnings)
    {
        if (footer is null)
            return;

        foreach (var menuName in footer.Menus ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(menuName))
                continue;
            if (!knownMenuNames.Contains(menuName))
                warnings.Add($"Navigation lint: {context} references unknown menu '{menuName}'.");
        }

        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columns = footer.Columns ?? Array.Empty<NavigationFooterColumnSpec>();
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i];
            if (column is null)
                continue;

            if (string.IsNullOrWhiteSpace(column.Name))
            {
                warnings.Add($"Navigation lint: {context}.Columns[{i}] has an empty name.");
            }
            else if (!columnNames.Add(column.Name))
            {
                warnings.Add($"Navigation lint: duplicate footer column name '{column.Name}' in {context}.");
            }

            var columnName = string.IsNullOrWhiteSpace(column.Name) ? $"column#{i + 1}" : column.Name;
            ValidateMenuItemsForLint(column.Items ?? Array.Empty<MenuItemSpec>(), $"{context}.Columns['{columnName}'].Items", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
        }

        ValidateMenuItemsForLint(footer.Legal ?? Array.Empty<MenuItemSpec>(), context + ".Legal", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
    }


    private static void ValidateMenuItemsForLint(
        IEnumerable<MenuItemSpec> items,
        string context,
        string[] knownRoutes,
        string[] routeScopedPrefixes,
        HashSet<string> knownCollections,
        HashSet<string> knownProjects,
        Dictionary<string, string> itemIdLocations,
        List<string> warnings)
    {
        var index = 0;
        foreach (var item in items)
        {
            if (item is null)
            {
                index++;
                continue;
            }

            var itemLabel = !string.IsNullOrWhiteSpace(item.Title) ? item.Title : $"item#{index + 1}";
            var itemContext = $"{context}['{itemLabel}']";
            ValidateMenuItemForLint(item, itemContext, knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
            index++;
        }
    }


    private static void ValidateMenuItemForLint(
        MenuItemSpec item,
        string context,
        string[] knownRoutes,
        string[] routeScopedPrefixes,
        HashSet<string> knownCollections,
        HashSet<string> knownProjects,
        Dictionary<string, string> itemIdLocations,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
            warnings.Add($"Navigation lint: {context} is missing 'Title'.");

        if (!string.IsNullOrWhiteSpace(item.Id))
        {
            if (itemIdLocations.TryGetValue(item.Id, out var existing))
            {
                warnings.Add($"Navigation lint: duplicate item id '{item.Id}' found in {context} (already used in {existing}).");
            }
            else
            {
                itemIdLocations[item.Id] = context;
            }
        }

        ValidateVisibilityPatterns(item.Visibility, context + ".Visibility", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, warnings);

        if (!string.IsNullOrWhiteSpace(item.Url))
        {
            var trimmedUrl = item.Url.Trim();
            if (!IsExternalNavigationUrl(trimmedUrl) && !trimmedUrl.StartsWith("#", StringComparison.Ordinal))
            {
                if (!trimmedUrl.StartsWith("/", StringComparison.Ordinal))
                {
                    warnings.Add($"Navigation lint: {context} uses relative url '{item.Url}'. Prefer root-relative links (for example '/docs/').");
                }
                else if (knownRoutes.Length > 0 &&
                         ShouldValidateRouteCoverage(trimmedUrl, routeScopedPrefixes) &&
                         !trimmedUrl.Contains('{', StringComparison.Ordinal) &&
                         !trimmedUrl.Contains('}', StringComparison.Ordinal) &&
                         string.IsNullOrWhiteSpace(item.Match) &&
                         (item.Items?.Length ?? 0) == 0 &&
                         (item.Sections?.Length ?? 0) == 0 &&
                         !PatternMatchesAnyRoute(trimmedUrl, knownRoutes))
                {
                    warnings.Add($"Navigation lint: {context} points to '{item.Url}' which does not match any generated route.");
                }
            }
        }
        else if (!string.Equals(item.Kind, "button", StringComparison.OrdinalIgnoreCase) &&
                 (item.Items?.Length ?? 0) == 0 &&
                 (item.Sections?.Length ?? 0) == 0)
        {
            warnings.Add($"Navigation lint: {context} has no 'Url' and no child items/sections.");
        }

        if (!string.IsNullOrWhiteSpace(item.Match) &&
            knownRoutes.Length > 0 &&
            ShouldValidateRouteCoverage(item.Match, routeScopedPrefixes) &&
            !PatternMatchesAnyRoute(item.Match, knownRoutes))
        {
            warnings.Add($"Navigation lint: {context}.Match '{item.Match}' does not match any generated route.");
        }

        ValidateMenuItemsForLint(item.Items ?? Array.Empty<MenuItemSpec>(), context + ".Items", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);

        var sections = item.Sections ?? Array.Empty<MenuSectionSpec>();
        for (var i = 0; i < sections.Length; i++)
        {
            var section = sections[i];
            if (section is null)
                continue;

            var sectionLabel = !string.IsNullOrWhiteSpace(section.Title) ? section.Title : $"section#{i + 1}";
            var sectionContext = $"{context}.Sections['{sectionLabel}']";
            ValidateMenuItemsForLint(section.Items ?? Array.Empty<MenuItemSpec>(), sectionContext + ".Items", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);

            var columns = section.Columns ?? Array.Empty<MenuColumnSpec>();
            for (var j = 0; j < columns.Length; j++)
            {
                var column = columns[j];
                if (column is null)
                    continue;
                var columnLabel = !string.IsNullOrWhiteSpace(column.Name) ? column.Name : $"column#{j + 1}";
                ValidateMenuItemsForLint(column.Items ?? Array.Empty<MenuItemSpec>(), $"{sectionContext}.Columns['{columnLabel}'].Items", knownRoutes, routeScopedPrefixes, knownCollections, knownProjects, itemIdLocations, warnings);
            }
        }
    }


    private static void ValidateVisibilityPatterns(
        NavigationVisibilitySpec? visibility,
        string context,
        string[] knownRoutes,
        string[] routeScopedPrefixes,
        HashSet<string> knownCollections,
        HashSet<string> knownProjects,
        List<string> warnings)
    {
        if (visibility is null)
            return;

        if (visibility.Paths is { Length: > 0 } && knownRoutes.Length > 0)
        {
            var hasScopedPaths = visibility.Paths.Any(path => ShouldValidateRouteCoverage(path, routeScopedPrefixes));
            if (hasScopedPaths && !visibility.Paths.Any(path => PatternMatchesAnyRoute(path, knownRoutes)))
                warnings.Add($"Navigation lint: {context}.Paths do not match any generated route.");
        }

        if (visibility.ExcludePaths is { Length: > 0 } && knownRoutes.Length > 0)
        {
            var hasScopedExcludes = visibility.ExcludePaths.Any(path => ShouldValidateRouteCoverage(path, routeScopedPrefixes));
            if (hasScopedExcludes && !visibility.ExcludePaths.Any(path => PatternMatchesAnyRoute(path, knownRoutes)))
                warnings.Add($"Navigation lint: {context}.ExcludePaths do not match any generated route.");
        }

        if (visibility.Collections is { Length: > 0 })
        {
            foreach (var collectionName in visibility.Collections.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!knownCollections.Contains(collectionName))
                    warnings.Add($"Navigation lint: {context}.Collections references unknown collection '{collectionName}'.");
            }
        }

        if (visibility.Projects is { Length: > 0 } && knownProjects.Count > 0)
        {
            foreach (var projectName in visibility.Projects.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!knownProjects.Contains(projectName))
                    warnings.Add($"Navigation lint: {context}.Projects references unknown project '{projectName}'.");
            }
        }
    }


    private static bool ShouldValidateRouteCoverage(string patternOrPath, string[] scopedPrefixes)
    {
        if (string.IsNullOrWhiteSpace(patternOrPath))
            return false;

        var normalized = NormalizePatternForNavigationMatch(patternOrPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;
        if (string.Equals(normalized, "/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (scopedPrefixes is null || scopedPrefixes.Length == 0)
            return false;

        return scopedPrefixes.Any(prefix =>
            normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }


    private static bool PatternMatchesAnyRoute(string pattern, IEnumerable<string> knownRoutes)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var normalizedPattern = NormalizePatternForNavigationMatch(pattern);
        if (string.IsNullOrWhiteSpace(normalizedPattern))
            return false;

        if (normalizedPattern.Contains('{', StringComparison.Ordinal) ||
            normalizedPattern.Contains('}', StringComparison.Ordinal))
            return true;

        var hasWildcard = normalizedPattern.Contains('*', StringComparison.Ordinal);
        foreach (var knownRoute in knownRoutes)
        {
            var route = NormalizeRouteForNavigationMatch(knownRoute);
            if (hasWildcard)
            {
                if (GlobMatch(normalizedPattern, route))
                    return true;
            }
            else if (string.Equals(normalizedPattern, route, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }


    private static string NormalizeRouteForNavigationMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/";

        var trimmed = value.Trim();
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

        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            trimmed = "/" + trimmed.TrimStart('/');
        if (!trimmed.EndsWith("/", StringComparison.Ordinal))
            trimmed += "/";

        return trimmed;
    }


    private static string NormalizePatternForNavigationMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal) &&
            !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "/" + trimmed;
        }

        var normalized = NormalizeRouteForNavigationMatch(trimmed);
        if (trimmed.Contains('*', StringComparison.Ordinal) && normalized.EndsWith("/", StringComparison.Ordinal))
            return normalized.TrimEnd('/');
        return normalized;
    }


    private static bool IsExternalNavigationUrl(string value)
    {
        if (IsExternalPath(value))
            return true;

        var trimmed = value.Trim();
        var colonIndex = trimmed.IndexOf(':');
        return colonIndex > 1 && LooksLikeUriScheme(trimmed, colonIndex);
    }


    private static void ValidateSiteNavExport(SiteSpec spec, WebSitePlan plan, List<string> warnings)
    {
        if (spec is null || plan is null || warnings is null) return;

        var nav = spec.Navigation;
        if (nav is null || nav.Menus is null || nav.Menus.Length == 0) return;
        if (nav.Auto is not null && nav.Auto.Length > 0) return;

        var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
        var dataPath = Path.IsPathRooted(dataRoot)
            ? Path.Combine(dataRoot, "site-nav.json")
            : Path.Combine(plan.RootPath, dataRoot, "site-nav.json");
        var staticPath = Path.Combine(plan.RootPath, "static", "data", "site-nav.json");

        var navPath = File.Exists(dataPath) ? dataPath : (File.Exists(staticPath) ? staticPath : null);
        if (string.IsNullOrWhiteSpace(navPath))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(navPath));
            if (!doc.RootElement.TryGetProperty("primary", out var primary) ||
                primary.ValueKind != JsonValueKind.Array)
                return;

            if (!doc.RootElement.TryGetProperty("menuModels", out var menuModels) ||
                menuModels.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("Navigation lint: site-nav.json does not contain 'menuModels'. This looks like an older nav export shape; regenerate it to avoid navigation/profile drift.");
            }

            if (!doc.RootElement.TryGetProperty("profiles", out var profiles) ||
                profiles.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("Navigation lint: site-nav.json does not contain 'profiles'. Navigation.Profiles cannot be applied by downstream tools (e.g., API docs nav injection). Regenerate the nav export.");
            }

            if (!doc.RootElement.TryGetProperty("surfaces", out var surfaces) ||
                surfaces.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Navigation lint: site-nav.json does not contain 'surfaces'. Regenerate the nav export so themes and tools can rely on stable nav surfaces (main/docs/apidocs/products).");
            }

            var menu = nav.Menus.FirstOrDefault(m => string.Equals(m.Name, "main", StringComparison.OrdinalIgnoreCase));
            if (menu is null || menu.Items is null || menu.Items.Length == 0) return;

            var expected = menu.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Url))
                .Select(i => (Href: i.Url ?? string.Empty, Text: i.Text ?? i.Title))
                .ToArray();

            var actual = primary.EnumerateArray()
                .Select(item =>
                {
                    var href = item.TryGetProperty("href", out var h) ? h.GetString() : null;
                    var text = item.TryGetProperty("text", out var t) ? t.GetString() : null;
                    return (Href: href ?? string.Empty, Text: text ?? string.Empty);
                })
                .Where(i => !string.IsNullOrWhiteSpace(i.Href))
                .ToArray();

            if (expected.Length != actual.Length)
            {
                warnings.Add($"Navigation lint: site-nav.json primary count ({actual.Length}) differs from Navigation main menu ({expected.Length}).");
                return;
            }

            for (var i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(expected[i].Href, actual[i].Href, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(expected[i].Text, actual[i].Text, StringComparison.Ordinal))
                {
                    warnings.Add("Navigation lint: site-nav.json primary entries differ from Navigation main menu. " +
                                 "Regenerate the nav export or update the custom data file.");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Navigation lint: failed to read site-nav.json for navigation consistency: {ex.Message}");
        }
    }

}
