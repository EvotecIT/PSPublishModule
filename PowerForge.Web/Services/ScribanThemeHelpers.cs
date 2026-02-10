using System.Text;

namespace PowerForge.Web;

/// <summary>
/// Helper methods exposed to Scriban templates as <c>pf</c>.
/// These helpers exist to keep theme navigation rendering consistent across sites.
/// </summary>
internal sealed class ScribanThemeHelpers
{
    private readonly ThemeRenderContext _context;

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
