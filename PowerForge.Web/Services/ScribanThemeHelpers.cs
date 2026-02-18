using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>
/// Helper methods exposed to Scriban templates as <c>pf</c>.
/// These helpers exist to keep theme navigation rendering consistent across sites.
/// </summary>
internal sealed class ScribanThemeHelpers
{
    private readonly ThemeRenderContext _context;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    public ScribanThemeHelpers(ThemeRenderContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    internal static int ParseInt(object? value, int defaultValue)
    {
        if (value is null)
            return defaultValue;

        if (value is int i)
            return i;

        if (value is long l)
            return unchecked((int)l);

        if (value is double d)
            return (int)Math.Round(d);

        if (value is string s && int.TryParse(s, out var parsed))
            return parsed;

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return defaultValue;
        }
    }

    internal static bool ParseBool(object? value, bool defaultValue)
    {
        if (value is null)
            return defaultValue;

        if (value is bool b)
            return b;

        if (value is int i)
            return i != 0;

        if (value is long l)
            return l != 0;

        if (value is double d)
            return Math.Abs(d) > 0.0001d;

        if (value is string s)
        {
            if (bool.TryParse(s, out var parsedBool))
                return parsedBool;
            if (int.TryParse(s, out var parsedInt))
                return parsedInt != 0;
        }

        return defaultValue;
    }

    public NavigationMenu? Menu(string? name)
    {
        var key = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return (_context.Navigation.Menus ?? Array.Empty<NavigationMenu>())
            .FirstOrDefault(m => m is not null && string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    public NavigationSurfaceRuntime? Surface(string? name)
    {
        var key = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return (_context.Navigation.Surfaces ?? Array.Empty<NavigationSurfaceRuntime>())
            .FirstOrDefault(s => s is not null && string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    public string NavLinks(string? menuName = "main", int maxDepth = 1)
    {
        var menu = Menu(menuName);
        if (menu?.Items is null || menu.Items.Length == 0)
            return string.Empty;

        var depth = Math.Clamp(maxDepth, 1, 6);
        var sb = new StringBuilder();
        foreach (var item in menu.Items)
        {
            RenderNavItem(sb, item, depth, 1);
        }
        return sb.ToString();
    }

    public string NavActions()
    {
        var actions = _context.Navigation.Actions ?? Array.Empty<NavigationItem>();
        if (actions.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var action in actions)
        {
            RenderAction(sb, action);
        }
        return sb.ToString();
    }

    public string MenuTree(string? menuName = "main", int maxDepth = 3)
    {
        var menu = Menu(menuName);
        if (menu?.Items is null || menu.Items.Length == 0)
            return string.Empty;

        var depth = Math.Clamp(maxDepth, 1, 10);
        var sb = new StringBuilder();
        sb.Append("<ul data-pf-menu=\"").Append(Html(menu.Name)).Append("\">");
        foreach (var item in menu.Items)
        {
            RenderMenuTreeItem(sb, item, depth, 1);
        }
        sb.Append("</ul>");
        return sb.ToString();
    }

    public string EditorialCards(int maxItems = 0, int excerptLength = 160, bool showCollection = true, bool showDate = true, bool showTags = true, bool showImage = true)
    {
        var items = _context.Items ?? Array.Empty<ContentItem>();
        if (items.Count == 0)
            return string.Empty;

        var take = maxItems > 0 ? maxItems : int.MaxValue;
        var maxExcerptLength = Math.Clamp(excerptLength, 40, 600);

        var selected = items
            .Where(static item => item is not null && !item.Draft && !string.IsNullOrWhiteSpace(item.OutputPath))
            .Take(take)
            .ToArray();
        if (selected.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("<div class=\"pf-editorial-grid\">");
        foreach (var item in selected)
        {
            var title = string.IsNullOrWhiteSpace(item.Title) ? item.OutputPath : item.Title;
            var summary = ResolveSummary(item, maxExcerptLength);
            var image = showImage ? ResolveCardImage(item.Meta) : string.Empty;

            sb.Append("<a class=\"pf-editorial-card\" href=\"").Append(Html(item.OutputPath)).Append("\">");
            if (!string.IsNullOrWhiteSpace(image))
                sb.Append("<img class=\"pf-editorial-card-image\" src=\"").Append(Html(image)).Append("\" alt=\"\" loading=\"lazy\" decoding=\"async\" />");

            if (showCollection || showDate)
            {
                sb.Append("<p class=\"pf-editorial-meta\">");
                if (showCollection && !string.IsNullOrWhiteSpace(item.Collection))
                    sb.Append("<span>").Append(Html(item.Collection)).Append("</span>");
                if (showDate && item.Date.HasValue)
                    sb.Append("<time datetime=\"").Append(item.Date.Value.ToString("yyyy-MM-dd")).Append("\">")
                      .Append(item.Date.Value.ToString("yyyy-MM-dd"))
                      .Append("</time>");
                sb.Append("</p>");
            }

            sb.Append("<h3>").Append(Html(title)).Append("</h3>");
            if (!string.IsNullOrWhiteSpace(summary))
                sb.Append("<p class=\"pf-editorial-summary\">").Append(Html(summary)).Append("</p>");

            if (showTags && item.Tags is { Length: > 0 })
            {
                sb.Append("<div class=\"pf-editorial-tags\">");
                foreach (var tag in item.Tags.Where(static tag => !string.IsNullOrWhiteSpace(tag)).Take(6))
                    sb.Append("<span class=\"pf-chip\">").Append(Html(tag)).Append("</span>");
                sb.Append("</div>");
            }

            sb.Append("</a>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static void RenderNavItem(StringBuilder sb, NavigationItem item, int maxDepth, int depth)
    {
        if (item is null)
            return;

        var text = string.IsNullOrWhiteSpace(item.Text) ? item.Title : item.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        var hasUrl = !string.IsNullOrWhiteSpace(item.Url);
        var hasChildren = item.Items is { Length: > 0 } && depth < maxDepth;

        if (hasChildren)
        {
            // Minimal dropdown structure that themes can style/replace.
            sb.Append("<details class=\"pf-nav-group\"");
            if (item.IsActive || item.IsAncestor) sb.Append(" open");
            sb.Append(">");
            sb.Append("<summary class=\"pf-nav-group__summary\">");
            if (!string.IsNullOrWhiteSpace(item.IconHtml))
                sb.Append(item.IconHtml);
            sb.Append(Html(text));
            sb.Append("</summary>");
            sb.Append("<div class=\"pf-nav-group__items\">");
            foreach (var child in item.Items ?? Array.Empty<NavigationItem>())
                RenderNavItem(sb, child, maxDepth, depth + 1);
            sb.Append("</div>");
            sb.Append("</details>");
            return;
        }

        if (!hasUrl)
            return;

        sb.Append("<a href=\"").Append(Html(item.Url!)).Append("\"");

        var cls = BuildClass(item, null);
        if (!string.IsNullOrWhiteSpace(cls))
            sb.Append(" class=\"").Append(Html(cls)).Append("\"");

        if (!string.IsNullOrWhiteSpace(item.Target))
            sb.Append(" target=\"").Append(Html(item.Target!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(item.Rel))
            sb.Append(" rel=\"").Append(Html(item.Rel!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(item.AriaLabel))
            sb.Append(" aria-label=\"").Append(Html(item.AriaLabel!)).Append("\"");

        sb.Append(">");
        if (!string.IsNullOrWhiteSpace(item.IconHtml))
            sb.Append(item.IconHtml);
        sb.Append(Html(text));
        sb.Append("</a>");
    }

    private static void RenderMenuTreeItem(StringBuilder sb, NavigationItem item, int maxDepth, int depth)
    {
        if (item is null)
            return;

        var text = string.IsNullOrWhiteSpace(item.Text) ? item.Title : item.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        var hasUrl = !string.IsNullOrWhiteSpace(item.Url);
        var children = item.Items ?? Array.Empty<NavigationItem>();
        var hasChildren = children.Length > 0 && depth < maxDepth;

        sb.Append("<li");
        var cls = BuildClass(item, "pf-menu__item");
        if (!string.IsNullOrWhiteSpace(cls))
            sb.Append(" class=\"").Append(Html(cls)).Append("\"");
        sb.Append(">");

        if (hasUrl)
        {
            sb.Append("<a href=\"").Append(Html(item.Url!)).Append("\">");
            sb.Append(Html(text));
            sb.Append("</a>");
        }
        else
        {
            sb.Append("<span>").Append(Html(text)).Append("</span>");
        }

        if (hasChildren)
        {
            sb.Append("<ul>");
            foreach (var child in children)
                RenderMenuTreeItem(sb, child, maxDepth, depth + 1);
            sb.Append("</ul>");
        }

        sb.Append("</li>");
    }

    private static void RenderAction(StringBuilder sb, NavigationItem action)
    {
        if (action is null)
            return;

        var isButton = string.Equals(action.Kind, "button", StringComparison.OrdinalIgnoreCase);
        var hasUrl = !string.IsNullOrWhiteSpace(action.Url);

        var title = action.Title;
        var ariaLabel = string.IsNullOrWhiteSpace(action.AriaLabel) ? title : action.AriaLabel;
        var iconHtml = string.IsNullOrWhiteSpace(action.IconHtml) ? null : action.IconHtml;
        var text = string.IsNullOrWhiteSpace(action.Text) ? null : action.Text;
        var hasIcon = !string.IsNullOrWhiteSpace(iconHtml);
        if (text is null && !hasIcon && !string.IsNullOrWhiteSpace(title))
            text = title;

        if (isButton)
        {
            sb.Append("<button type=\"button\"");
            if (!string.IsNullOrWhiteSpace(action.CssClass))
                sb.Append(" class=\"").Append(Html(action.CssClass!)).Append("\"");
            if (!string.IsNullOrWhiteSpace(title))
                sb.Append(" title=\"").Append(Html(title)).Append("\"");
            if (!string.IsNullOrWhiteSpace(ariaLabel))
                sb.Append(" aria-label=\"").Append(Html(ariaLabel!)).Append("\"");
            sb.Append(">");
            if (hasIcon)
                sb.Append(iconHtml);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (hasIcon) sb.Append(" ");
                sb.Append(Html(text));
            }
            sb.Append("</button>");
            return;
        }

        if (!hasUrl)
            return;

        sb.Append("<a href=\"").Append(Html(action.Url!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(action.CssClass))
            sb.Append(" class=\"").Append(Html(action.CssClass!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(action.Target))
            sb.Append(" target=\"").Append(Html(action.Target!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(action.Rel))
            sb.Append(" rel=\"").Append(Html(action.Rel!)).Append("\"");
        if (!string.IsNullOrWhiteSpace(title))
            sb.Append(" title=\"").Append(Html(title)).Append("\"");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
            sb.Append(" aria-label=\"").Append(Html(ariaLabel!)).Append("\"");
        sb.Append(">");
        if (hasIcon)
            sb.Append(iconHtml);
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (hasIcon) sb.Append(" ");
            sb.Append(Html(text));
        }
        sb.Append("</a>");
    }

    private static string ResolveSummary(ContentItem item, int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(item.Description))
            return Truncate(item.Description.Trim(), maxLength);

        if (string.IsNullOrWhiteSpace(item.HtmlContent))
            return string.Empty;

        var plain = HtmlTagRegex.Replace(item.HtmlContent, " ");
        plain = Regex.Replace(plain, "\\s+", " ", RegexOptions.CultureInvariant, RegexTimeout).Trim();
        return Truncate(plain, maxLength);
    }

    private static string ResolveCardImage(IReadOnlyDictionary<string, object?>? meta)
    {
        if (meta is null || meta.Count == 0)
            return string.Empty;

        var candidates = new[]
        {
            "social_image",
            "social.image",
            "image",
            "cover",
            "thumbnail",
            "card_image",
            "card.image"
        };

        foreach (var key in candidates)
        {
            var value = TryGetMetaString(meta, key);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static string? TryGetMetaString(IReadOnlyDictionary<string, object?> meta, string key)
    {
        if (meta is null || string.IsNullOrWhiteSpace(key))
            return null;

        if (!key.Contains('.', StringComparison.Ordinal))
        {
            if (meta.TryGetValue(key, out var value))
                return NormalizeMetaValue(value);
            return null;
        }

        var current = (object?)meta;
        var parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (current is IReadOnlyDictionary<string, object?> readOnlyMap)
            {
                if (!readOnlyMap.TryGetValue(part, out current))
                    return null;
                continue;
            }

            if (current is Dictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out current))
                    return null;
                continue;
            }

            return null;
        }

        return NormalizeMetaValue(current);
    }

    private static string? NormalizeMetaValue(object? value)
    {
        if (value is null)
            return null;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || maxLength <= 0 || text.Length <= maxLength)
            return text;

        var safe = Math.Max(8, maxLength - 1);
        return text.Substring(0, safe).TrimEnd() + "…";
    }

    private static string BuildClass(NavigationItem item, string? baseClass)
    {
        var cls = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseClass))
            cls.Add(baseClass);
        if (!string.IsNullOrWhiteSpace(item.CssClass))
            cls.Add(item.CssClass!.Trim());
        if (item.IsActive)
            cls.Add("is-active");
        else if (item.IsAncestor)
            cls.Add("is-ancestor");
        return cls.Count == 0 ? string.Empty : string.Join(" ", cls);
    }

    private static string Html(string value) => System.Web.HttpUtility.HtmlEncode(value ?? string.Empty);
}
