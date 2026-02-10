using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.Loader;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private static string LoadOptionalHtml(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return string.Empty;
        return File.ReadAllText(full);
    }

    private static string LoadEmbeddedRaw(string fileName)
    {
        var assembly = typeof(WebApiDocsGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"Assets.ApiDocs.{fileName}", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(resourceName)) return string.Empty;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string LoadTemplate(WebApiDocsOptions options, string fileName, string? explicitPath)
    {
        var content = LoadFileText(explicitPath);
        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(options.TemplateRootPath))
        {
            var candidate = Path.Combine(Path.GetFullPath(options.TemplateRootPath), fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        return LoadEmbeddedRaw(fileName);
    }

    private static string LoadAsset(WebApiDocsOptions options, string fileName, string? explicitPath)
    {
        var content = LoadFileText(explicitPath);
        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(options.TemplateRootPath))
        {
            var candidate = Path.Combine(Path.GetFullPath(options.TemplateRootPath), fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        return LoadEmbeddedRaw(fileName);
    }

    private static string LoadFileText(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return string.Empty;
        return File.ReadAllText(full);
    }

    private static NavConfig? LoadNavConfig(WebApiDocsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.NavJsonPath)) return null;
        var path = Path.GetFullPath(options.NavJsonPath);
        if (!File.Exists(path)) return null;

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var nav = new NavConfig
        {
            SiteName = options.SiteName ?? string.Empty,
            BrandUrl = string.IsNullOrWhiteSpace(options.BrandUrl) ? "/" : options.BrandUrl,
            BrandIcon = string.IsNullOrWhiteSpace(options.BrandIcon) ? "/codeglyphx-qr-icon.png" : options.BrandIcon
        };

        if (root.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            nav.SiteName = nameProp.GetString() ?? nav.SiteName;
        if (root.TryGetProperty("siteName", out var siteProp) && siteProp.ValueKind == JsonValueKind.String)
            nav.SiteName = siteProp.GetString() ?? nav.SiteName;

        if (TryGetProperty(root, "Head", out var headProp) && headProp.ValueKind == JsonValueKind.Object)
        {
            if (headProp.TryGetProperty("Links", out var linksProp) && linksProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in linksProp.EnumerateArray())
                {
                    if (!link.TryGetProperty("Rel", out var relProp) || relProp.ValueKind != JsonValueKind.String)
                        continue;
                    var rel = relProp.GetString() ?? string.Empty;
                    if (!rel.Equals("icon", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!link.TryGetProperty("Href", out var hrefProp) || hrefProp.ValueKind != JsonValueKind.String)
                        continue;
                    var href = hrefProp.GetString();
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        nav.BrandIcon = href;
                        break;
                    }
                }
            }
        }

        // Prefer the navigation export shape when present (site-nav.json). It contains merged auto menus and profile definitions.
        if (TryApplySiteNavExportWithProfiles(root, options, nav))
            return nav;

        if (TryGetProperty(root, "Navigation", out var navProp) && navProp.ValueKind == JsonValueKind.Object)
        {
            ParseSiteNavigation(navProp, options, nav);
            return nav;
        }

        if (root.TryGetProperty("primary", out var primaryProp) && primaryProp.ValueKind == JsonValueKind.Array)
        {
            nav.Primary = ParseNavItems(primaryProp);
        }

        if (root.TryGetProperty("footer", out var footerProp) && footerProp.ValueKind == JsonValueKind.Object)
        {
            if (footerProp.TryGetProperty("product", out var productProp) && productProp.ValueKind == JsonValueKind.Array)
                nav.FooterProduct = ParseNavItems(productProp);
            if (footerProp.TryGetProperty("resources", out var resourcesProp) && resourcesProp.ValueKind == JsonValueKind.Array)
                nav.FooterResources = ParseNavItems(resourcesProp);
            if (footerProp.TryGetProperty("company", out var companyProp) && companyProp.ValueKind == JsonValueKind.Array)
                nav.FooterCompany = ParseNavItems(companyProp);
        }

        if (root.TryGetProperty("actions", out var actionsProp) && actionsProp.ValueKind == JsonValueKind.Array)
            nav.Actions = ParseSiteNavActions(actionsProp);

        return nav;
    }

    private static void ParseSiteNavigation(JsonElement navElement, WebApiDocsOptions options, NavConfig nav)
    {
        var baseMenus = ReadMenuMap(navElement, "Menus");
        var baseActions = ReadActions(navElement, "Actions");

        var ctx = BuildNavContext(options);
        var profile = SelectBestProfileFromSiteNavigation(navElement, ctx);

        var effectiveMenus = MergeMenus(baseMenus, profile?.Menus, profile?.InheritMenus ?? true);
        var effectiveActions = MergeActions(baseActions, profile?.Actions, profile?.InheritActions ?? true);

        ApplyMenusToNav(nav, effectiveMenus);
        if (effectiveActions.Count > 0)
            nav.Actions = effectiveActions;
    }

    private sealed class NavContext
    {
        public NavContext(string path, string collection, string layout, string project, string[] layoutCandidates)
        {
            Path = path;
            Collection = collection;
            Layout = layout;
            Project = project;
            LayoutCandidates = layoutCandidates;
        }

        public string Path { get; }
        public string Collection { get; }
        public string Layout { get; }
        public string Project { get; }
        public string[] LayoutCandidates { get; }
    }

    private sealed class NavProfileResolved
    {
        public string? Name { get; set; }
        public int Priority { get; set; }
        public bool InheritMenus { get; set; } = true;
        public bool InheritActions { get; set; } = true;
        public Dictionary<string, List<NavItem>> Menus { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<NavAction> Actions { get; set; } = new();
    }

    private static bool TryApplySiteNavExportWithProfiles(JsonElement root, WebApiDocsOptions options, NavConfig nav)
    {
        if (!TryGetProperty(root, "menuModels", out var menuModelsProp) || menuModelsProp.ValueKind != JsonValueKind.Array)
            return false;

        var baseMenus = ReadMenuMap(root, "menuModels");
        var baseActions = ReadActions(root, "actions");

        var ctx = BuildNavContext(options);
        var profile = SelectBestProfileFromProfiles(root, ctx);

        var effectiveMenus = MergeMenus(baseMenus, profile?.Menus, profile?.InheritMenus ?? true);
        var effectiveActions = MergeActions(baseActions, profile?.Actions, profile?.InheritActions ?? true);

        ApplyMenusToNav(nav, effectiveMenus);
        if (effectiveActions.Count > 0)
            nav.Actions = effectiveActions;

        return true;
    }

    private static NavContext BuildNavContext(WebApiDocsOptions options)
    {
        // Profile selection should be explicit: default to "/" so API pages inherit the site's primary navigation.
        // Sites that want /api-specific profile overrides should set navContextPath on the apidocs step.
        var path = NormalizeContextPath(options.NavContextPath);
        var collection = options.NavContextCollection ?? string.Empty;
        var layout = options.NavContextLayout ?? string.Empty;
        var project = options.NavContextProject ?? string.Empty;

        var candidates = !string.IsNullOrWhiteSpace(layout)
            ? new[] { layout.Trim() }
            : path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
                ? new[] { string.Empty, "apiDocs", "api" }
                : new[] { string.Empty };

        return new NavContext(path, collection, layout, project, candidates);
    }

    private static string NormalizeContextPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/";

        var v = value.Trim();
        var hashIndex = v.IndexOf('#');
        if (hashIndex >= 0) v = v.Substring(0, hashIndex);
        var queryIndex = v.IndexOf('?');
        if (queryIndex >= 0) v = v.Substring(0, queryIndex);

        if (Uri.TryCreate(v, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            v = absolute.AbsolutePath;
        }

        v = v.Replace('\\', '/');
        if (!v.StartsWith("/", StringComparison.Ordinal))
            v = "/" + v;
        if (!v.EndsWith("/", StringComparison.Ordinal))
            v += "/";
        return v;
    }

    private static Dictionary<string, List<NavItem>> ReadMenuMap(JsonElement element, string menusPropertyName)
    {
        var map = new Dictionary<string, List<NavItem>>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(element, menusPropertyName, out var menusProp) || menusProp.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var menu in menusProp.EnumerateArray())
        {
            if (menu.ValueKind != JsonValueKind.Object)
                continue;

            var name = ReadString(menu, "Name", "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!TryGetProperty(menu, "Items", out var itemsProp) || itemsProp.ValueKind != JsonValueKind.Array)
                continue;

            var items = ParseAnyNavItems(itemsProp);
            map[name] = items;
        }

        return map;
    }

    private static List<NavAction> ReadActions(JsonElement element, string actionsPropertyName)
    {
        if (!TryGetProperty(element, actionsPropertyName, out var actionsProp) || actionsProp.ValueKind != JsonValueKind.Array)
            return new List<NavAction>();

        return ParseSiteNavActions(actionsProp);
    }

    private static List<NavItem> ParseAnyNavItems(JsonElement itemsProp)
    {
        var list = new List<NavItem>();
        foreach (var item in itemsProp.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var parsed = ParseAnyNavItem(item);
            if (parsed is not null)
                list.Add(parsed);
        }
        return list;
    }

    private static NavItem? ParseAnyNavItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var href = ReadString(item, "Url", "url", "Href", "href");
        var text = ReadString(item, "Text", "text", "Title", "title");
        if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text))
            return null;

        var target = ReadString(item, "Target", "target");
        var rel = ReadString(item, "Rel", "rel");
        var external = ReadBool(item, "External", "external");
        external |= IsExternal(href);

        var children = new List<NavItem>();
        if (TryGetProperty(item, "Items", out var childItems) && childItems.ValueKind == JsonValueKind.Array)
            children = ParseAnyNavItems(childItems);

        return new NavItem(href, text, external, target, rel, children);
    }

    private static Dictionary<string, List<NavItem>> MergeMenus(
        Dictionary<string, List<NavItem>> baseMenus,
        Dictionary<string, List<NavItem>>? profileMenus,
        bool inherit)
    {
        var map = new Dictionary<string, List<NavItem>>(StringComparer.OrdinalIgnoreCase);
        if (inherit)
        {
            foreach (var kvp in baseMenus)
                map[kvp.Key] = kvp.Value;
        }

        if (profileMenus is not null)
        {
            foreach (var kvp in profileMenus)
                map[kvp.Key] = kvp.Value;
        }

        return map;
    }

    private static List<NavAction> MergeActions(List<NavAction> baseActions, List<NavAction>? profileActions, bool inherit)
    {
        if (!inherit)
            return profileActions ?? new List<NavAction>();

        if (profileActions is null || profileActions.Count == 0)
            return baseActions;

        var merged = new List<NavAction>(baseActions.Count + profileActions.Count);
        merged.AddRange(baseActions);
        merged.AddRange(profileActions);
        return merged;
    }

    private static void ApplyMenusToNav(NavConfig nav, Dictionary<string, List<NavItem>> menus)
    {
        nav.Primary = menus.TryGetValue("main", out var main)
            ? main
            : menus.Count > 0
                ? menus.Values.First()
                : new List<NavItem>();

        var fallbackFooters = new List<(string Name, List<NavItem> Items)>();
        foreach (var kvp in menus)
        {
            var name = kvp.Key ?? string.Empty;
            var items = kvp.Value ?? new List<NavItem>();
            if (name.Equals("footer-product", StringComparison.OrdinalIgnoreCase))
                nav.FooterProduct = items;
            else if (name.Equals("footer-resources", StringComparison.OrdinalIgnoreCase))
                nav.FooterResources = items;
            else if (name.Equals("footer-company", StringComparison.OrdinalIgnoreCase))
                nav.FooterCompany = items;
            else if (name.StartsWith("footer-", StringComparison.OrdinalIgnoreCase))
                fallbackFooters.Add((name, items));
        }

        foreach (var tuple in fallbackFooters)
        {
            if (nav.FooterProduct.Count == 0)
            {
                nav.FooterProduct = tuple.Items;
                continue;
            }
            if (nav.FooterCompany.Count == 0)
            {
                nav.FooterCompany = tuple.Items;
                continue;
            }
            if (nav.FooterResources.Count == 0)
            {
                nav.FooterResources = tuple.Items;
            }
        }
    }

    private static NavProfileResolved? SelectBestProfileFromSiteNavigation(JsonElement navElement, NavContext ctx)
    {
        if (!TryGetProperty(navElement, "Profiles", out var profilesProp) || profilesProp.ValueKind != JsonValueKind.Array)
            return null;
        return SelectBestProfileFromProfilesArray(profilesProp, ctx);
    }

    private static NavProfileResolved? SelectBestProfileFromProfiles(JsonElement root, NavContext ctx)
    {
        if (!TryGetProperty(root, "profiles", out var profilesProp) || profilesProp.ValueKind != JsonValueKind.Array)
            return null;
        return SelectBestProfileFromProfilesArray(profilesProp, ctx);
    }

    private static NavProfileResolved? SelectBestProfileFromProfilesArray(JsonElement profilesProp, NavContext ctx)
    {
        NavProfileResolved? best = null;
        var bestScore = int.MinValue;

        foreach (var profile in profilesProp.EnumerateArray())
        {
            if (profile.ValueKind != JsonValueKind.Object)
                continue;

            if (!TryBuildProfile(profile, ctx, out var candidate, out var score))
                continue;

            if (best is null || score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool TryBuildProfile(JsonElement profile, NavContext ctx, out NavProfileResolved resolved, out int score)
    {
        resolved = new NavProfileResolved();
        score = int.MinValue;

        var paths = ReadStringArray(profile, "Paths", "paths");
        var collections = ReadStringArray(profile, "Collections", "collections");
        var layouts = ReadStringArray(profile, "Layouts", "layouts");
        var projects = ReadStringArray(profile, "Projects", "projects");

        if (paths.Length > 0 && !paths.Any(p => GlobMatch(p, ctx.Path)))
            return false;

        if (collections.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(ctx.Collection) ||
                !collections.Any(c => c.Equals(ctx.Collection, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (layouts.Length > 0)
        {
            if (!ctx.LayoutCandidates.Any(candidate =>
                    !string.IsNullOrWhiteSpace(candidate) &&
                    layouts.Any(l => l.Equals(candidate, StringComparison.OrdinalIgnoreCase))))
                return false;
        }

        if (projects.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(ctx.Project) ||
                !projects.Any(p => p.Equals(ctx.Project, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        resolved.Name = ReadString(profile, "Name", "name");
        resolved.Priority = ReadInt(profile, "Priority", "priority");
        resolved.InheritMenus = ReadBoolOrDefault(profile, "InheritMenus", "inheritMenus", defaultValue: true);
        resolved.InheritActions = ReadBoolOrDefault(profile, "InheritActions", "inheritActions", defaultValue: true);
        resolved.Actions = ReadActions(profile, "Actions");

        var profileMenus = ReadMenuMap(profile, "Menus");
        foreach (var kvp in profileMenus)
            resolved.Menus[kvp.Key] = kvp.Value;

        score = (resolved.Priority * 100) + paths.Length + collections.Length + layouts.Length + projects.Length;
        return true;
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(value))
            return false;

        var normalizedPattern = pattern.Trim().Replace('\\', '/');
        var normalizedValue = value.Trim().Replace('\\', '/');

        var regex = "^" + Regex.Escape(normalizedPattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*") + "$";
        return Regex.IsMatch(normalizedValue, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement prop)
    {
        prop = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (element.TryGetProperty(name, out prop))
            return true;

        // Try a basic alternative casing form (PascalCase <-> camelCase).
        if (string.IsNullOrWhiteSpace(name) || name.Length < 2)
            return false;

        var alt = char.IsUpper(name[0])
            ? char.ToLowerInvariant(name[0]) + name.Substring(1)
            : char.ToUpperInvariant(name[0]) + name.Substring(1);

        return element.TryGetProperty(alt, out prop);
    }

    private static string[] ReadStringArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var prop) || prop.ValueKind != JsonValueKind.Array)
                continue;

            var list = new List<string>();
            foreach (var item in prop.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        list.Add(value!.Trim());
                }
            }

            return list.ToArray();
        }

        return Array.Empty<string>();
    }

    private static int ReadInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var prop))
                continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n))
                return n;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
                return parsed;
        }
        return 0;
    }

    private static bool ReadBoolOrDefault(JsonElement element, string name1, string name2, bool defaultValue = false)
    {
        if (TryGetProperty(element, name1, out var prop) || TryGetProperty(element, name2, out prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }

    private static List<NavItem> ParseNavItems(JsonElement itemsProp)
        => ParseAnyNavItems(itemsProp);

    private static List<NavItem> ParseSiteNavItems(JsonElement itemsProp)
        => ParseAnyNavItems(itemsProp);

    private static List<NavAction> ParseSiteNavActions(JsonElement itemsProp)
    {
        var list = new List<NavAction>();
        foreach (var item in itemsProp.EnumerateArray())
        {
            var href = ReadString(item, "Url", "href");
            var title = ReadString(item, "Title", "title");
            var text = ReadString(item, "Text", "text");
            var iconHtml = ReadString(item, "IconHtml", "iconHtml", "Icon", "icon");
            var cssClass = ReadString(item, "CssClass", "class");
            var kind = ReadString(item, "Kind", "kind");
            var ariaLabel = ReadString(item, "AriaLabel", "ariaLabel", "aria");
            var target = ReadString(item, "Target", "target");
            var rel = ReadString(item, "Rel", "rel");
            var external = ReadBool(item, "External", "external");
            if (!string.IsNullOrWhiteSpace(href))
                external |= IsExternal(href);

            list.Add(new NavAction(href, text, title, ariaLabel, iconHtml, cssClass, kind, external, target, rel));
        }
        return list;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static bool ReadBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.True)
                return true;
        }
        return false;
    }

    private static bool IsExternal(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;
        return Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string WrapStyle(string content)
        => string.IsNullOrWhiteSpace(content) ? string.Empty : $"<style>{content}</style>";

    private static string WrapScript(string content)
        => string.IsNullOrWhiteSpace(content) ? string.Empty : $"<script>{content}</script>";

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string?> replacements)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;
        var result = template;
        foreach (var kvp in replacements)
        {
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value ?? string.Empty);
        }
        return result;
    }
}
