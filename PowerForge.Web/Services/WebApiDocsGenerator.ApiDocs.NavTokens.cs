using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static void ApplyNavTokens(WebApiDocsOptions options, List<string> warnings, ref string header, ref string footer)
    {
        // Fail-fast (in CI via apidocs failOnWarnings): if fragments expect nav injection but the step
        // forgot to provide navJsonPath, we should emit a deterministic warning instead of silently
        // producing API pages without navigation.
        var needsNavTokens =
            ContainsTemplateToken(header, "NAV_LINKS") ||
            ContainsTemplateToken(header, "NAV_ACTIONS") ||
            ContainsTemplateToken(footer, "NAV_LINKS") ||
            ContainsTemplateToken(footer, "NAV_ACTIONS");
        if (needsNavTokens && string.IsNullOrWhiteSpace(options.NavJsonPath))
        {
            warnings?.Add("API docs nav required: header/footer fragments contain {{NAV_*}} placeholders but NavJsonPath is not set. Set apidocs.nav (or apidocs.config) so navigation can be injected.");
            return;
        }

        if (string.IsNullOrWhiteSpace(options.NavJsonPath)) return;

        var navPath = Path.GetFullPath(options.NavJsonPath);
        if (!File.Exists(navPath))
        {
            warnings?.Add($"API docs nav: nav json not found: {options.NavJsonPath}.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.HeaderHtmlPath) || !string.IsNullOrWhiteSpace(options.FooterHtmlPath))
        {
            // If the site provides custom header/footer fragments but forgets to include navigation placeholders,
            // nav injection will be a no-op and API pages may render without expected site navigation.
            if (!needsNavTokens)
            {
                warnings?.Add("API docs nav: header/footer fragments do not contain {{NAV_LINKS}} or {{NAV_ACTIONS}} placeholders; nav injection may be empty.");
            }
        }

        var nav = LoadNavConfig(options);
        if (nav is null) return;

        var tokens = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SITE_NAME"] = nav.SiteName,
            ["BRAND_NAME"] = nav.SiteName,
            ["BRAND_URL"] = nav.BrandUrl,
            ["BRAND_ICON"] = nav.BrandIcon,
            ["NAV_LINKS"] = BuildLinkHtml(nav.Primary),
            ["NAV_ACTIONS"] = BuildActionHtml(nav.Actions),
            ["FOOTER_PRODUCT"] = BuildLinkHtml(nav.FooterProduct),
            ["FOOTER_RESOURCES"] = BuildLinkHtml(nav.FooterResources),
            ["FOOTER_COMPANY"] = BuildLinkHtml(nav.FooterCompany),
            ["YEAR"] = DateTime.UtcNow.Year.ToString()
        };

        if (!string.IsNullOrWhiteSpace(header))
            header = ApplyTemplate(header, tokens);
        if (!string.IsNullOrWhiteSpace(footer))
            footer = ApplyTemplate(footer, tokens);
    }

    private static bool ContainsTemplateToken(string html, string token)
    {
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(token))
            return false;
        // Tokens are case-insensitive and appear as {{TOKEN}} in fragments.
        return html.IndexOf("{{" + token + "}}", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildLinkHtml(IReadOnlyList<NavItem> items)
    {
        if (items.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            AppendNavItemHtml(sb, item);
        }
        return sb.ToString();
    }

    private static void AppendNavItemHtml(StringBuilder sb, NavItem item)
    {
        if (sb is null || item is null)
            return;
        if (string.IsNullOrWhiteSpace(item.Text))
            return;

        if (item.Items.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(item.Href))
                return;
            AppendAnchorHtml(sb, item, cssClass: null);
            return;
        }

        // Keep API docs header behavior consistent with normal site headers:
        // nested menu items are rendered as dropdowns instead of flattened links.
        sb.Append("<div class=\"nav-dropdown\">");
        AppendDropdownTriggerHtml(sb, item);
        sb.Append("<div class=\"nav-dropdown-menu\">");
        AppendDropdownItemsHtml(sb, item.Items);
        sb.Append("</div></div>");
    }

    private static void AppendDropdownItemsHtml(StringBuilder sb, IReadOnlyList<NavItem> items)
    {
        if (sb is null || items is null || items.Count == 0)
            return;

        foreach (var child in items)
        {
            if (child is null || string.IsNullOrWhiteSpace(child.Text))
                continue;

            if (!string.IsNullOrWhiteSpace(child.Href))
            {
                AppendAnchorHtml(sb, child, cssClass: null);
            }
            else if (child.Items.Count > 0)
            {
                // Group label for nested menu sections without direct link.
                sb.Append("<div class=\"nav-dropdown-group\">")
                  .Append("<span class=\"nav-dropdown-label\">")
                  .Append(System.Web.HttpUtility.HtmlEncode(child.Text))
                  .Append("</span></div>");
            }

            if (child.Items.Count > 0)
                AppendDropdownItemsHtml(sb, child.Items);
        }
    }

    private static void AppendDropdownTriggerHtml(StringBuilder sb, NavItem item)
    {
        if (sb is null || item is null || string.IsNullOrWhiteSpace(item.Text))
            return;

        if (!string.IsNullOrWhiteSpace(item.Href))
        {
            AppendAnchorHtml(sb, item, cssClass: "nav-dropdown-trigger", includeArrow: true);
            return;
        }

        sb.Append("<button type=\"button\" class=\"nav-dropdown-trigger nav-dropdown-trigger-button\">")
          .Append(System.Web.HttpUtility.HtmlEncode(item.Text))
          .Append("<svg class=\"nav-dropdown-arrow\" viewBox=\"0 0 12 12\" width=\"10\" height=\"10\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" aria-hidden=\"true\"><path d=\"M3 5l3 3 3-3\"/></svg>")
          .Append("</button>");
    }

    private static void AppendAnchorHtml(StringBuilder sb, NavItem item, string? cssClass, bool includeArrow = false)
    {
        if (string.IsNullOrWhiteSpace(item.Href))
            return;

        var href = System.Web.HttpUtility.HtmlEncode(item.Href);
        var text = System.Web.HttpUtility.HtmlEncode(item.Text);
        var target = item.Target;
        var rel = item.Rel;
        if (string.IsNullOrWhiteSpace(target) && item.External)
            target = "_blank";
        if (string.IsNullOrWhiteSpace(rel) && item.External)
            rel = "noopener";

        sb.Append("<a href=\"").Append(href).Append("\"");
        if (!string.IsNullOrWhiteSpace(cssClass))
            sb.Append(" class=\"").Append(System.Web.HttpUtility.HtmlEncode(cssClass)).Append("\"");
        if (!string.IsNullOrWhiteSpace(target))
            sb.Append(" target=\"").Append(System.Web.HttpUtility.HtmlEncode(target)).Append("\"");
        if (!string.IsNullOrWhiteSpace(rel))
            sb.Append(" rel=\"").Append(System.Web.HttpUtility.HtmlEncode(rel)).Append("\"");
        sb.Append(">").Append(text);
        if (includeArrow)
        {
            sb.Append("<svg class=\"nav-dropdown-arrow\" viewBox=\"0 0 12 12\" width=\"10\" height=\"10\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" aria-hidden=\"true\"><path d=\"M3 5l3 3 3-3\"/></svg>");
        }
        sb.Append("</a>");
    }

    private static string BuildActionHtml(IReadOnlyList<NavAction> actions)
    {
        if (actions.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var action in actions)
        {
            var isButton = string.Equals(action.Kind, "button", StringComparison.OrdinalIgnoreCase);
            if (!isButton && string.IsNullOrWhiteSpace(action.Href))
                continue;

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
                    sb.Append(" class=\"").Append(System.Web.HttpUtility.HtmlEncode(action.CssClass)).Append("\"");
                if (!string.IsNullOrWhiteSpace(title))
                    sb.Append(" title=\"").Append(System.Web.HttpUtility.HtmlEncode(title)).Append("\"");
                if (!string.IsNullOrWhiteSpace(ariaLabel))
                    sb.Append(" aria-label=\"").Append(System.Web.HttpUtility.HtmlEncode(ariaLabel)).Append("\"");
                sb.Append(">");
                if (hasIcon)
                    sb.Append(iconHtml);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (hasIcon) sb.Append(" ");
                    sb.Append(System.Web.HttpUtility.HtmlEncode(text));
                }
                sb.Append("</button>");
                continue;
            }

            var href = System.Web.HttpUtility.HtmlEncode(action.Href ?? string.Empty);
            var external = action.External || IsExternal(action.Href ?? string.Empty);
            var target = action.Target;
            var rel = action.Rel;
            if (external && string.IsNullOrWhiteSpace(target))
                target = "_blank";
            if (external && string.IsNullOrWhiteSpace(rel))
                rel = "noopener";

            sb.Append("<a href=\"").Append(href).Append("\"");
            if (!string.IsNullOrWhiteSpace(action.CssClass))
                sb.Append(" class=\"").Append(System.Web.HttpUtility.HtmlEncode(action.CssClass)).Append("\"");
            if (!string.IsNullOrWhiteSpace(target))
                sb.Append(" target=\"").Append(System.Web.HttpUtility.HtmlEncode(target)).Append("\"");
            if (!string.IsNullOrWhiteSpace(rel))
                sb.Append(" rel=\"").Append(System.Web.HttpUtility.HtmlEncode(rel)).Append("\"");
            if (!string.IsNullOrWhiteSpace(title))
                sb.Append(" title=\"").Append(System.Web.HttpUtility.HtmlEncode(title)).Append("\"");
            if (!string.IsNullOrWhiteSpace(ariaLabel))
                sb.Append(" aria-label=\"").Append(System.Web.HttpUtility.HtmlEncode(ariaLabel)).Append("\"");
            sb.Append(">");
            if (hasIcon)
                sb.Append(iconHtml);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (hasIcon) sb.Append(" ");
                sb.Append(System.Web.HttpUtility.HtmlEncode(text));
            }
            sb.Append("</a>");
        }
        return sb.ToString();
    }
}
