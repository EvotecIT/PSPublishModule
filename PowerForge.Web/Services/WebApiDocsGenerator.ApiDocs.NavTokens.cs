using System;
using System.Collections.Generic;
using System.IO;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static void ApplyNavTokens(WebApiDocsOptions options, List<string> warnings, ref string header, ref string footer)
    {
        var tokens = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["YEAR"] = DateTime.UtcNow.Year.ToString(),
            ["LANGUAGE"] = "en",
            ["LANGUAGE_UPPER"] = "EN",
            ["LABEL_SEARCH"] = "Search",
            ["LABEL_SEARCH_PLACEHOLDER"] = "Search pages, projects, docs...",
            ["LABEL_FUZZY_SEARCH"] = "Fuzzy search",
            ["LABEL_LANGUAGE_SWITCH"] = "Language switch",
            ["LABEL_SWITCH_THEME"] = "Switch theme"
        };
        foreach (var pair in options.TemplateTokens)
            tokens[pair.Key] = pair.Value;

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
            if (!string.IsNullOrWhiteSpace(header))
                header = ApplyTemplate(header, tokens);
            if (!string.IsNullOrWhiteSpace(footer))
                footer = ApplyTemplate(footer, tokens);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.NavJsonPath))
        {
            if (!string.IsNullOrWhiteSpace(header))
                header = ApplyTemplate(header, tokens);
            if (!string.IsNullOrWhiteSpace(footer))
                footer = ApplyTemplate(footer, tokens);
            return;
        }

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
        if (nav is null)
        {
            if (!string.IsNullOrWhiteSpace(header))
                header = ApplyTemplate(header, tokens);
            if (!string.IsNullOrWhiteSpace(footer))
                footer = ApplyTemplate(footer, tokens);
            return;
        }

        tokens["SITE_NAME"] = nav.SiteName;
        tokens["BRAND_NAME"] = nav.SiteName;
        tokens["BRAND_URL"] = nav.BrandUrl;
        tokens["BRAND_ICON"] = nav.BrandIcon;
        tokens["NAV_LINKS"] = BuildLinkHtml(nav.Primary);
        tokens["NAV_ACTIONS"] = BuildActionHtml(nav.Actions);
        tokens["FOOTER_PRODUCT"] = BuildLinkHtml(nav.FooterProduct);
        tokens["FOOTER_RESOURCES"] = BuildLinkHtml(nav.FooterResources);
        tokens["FOOTER_COMPANY"] = BuildLinkHtml(nav.FooterCompany);

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
        if (items.Count == 0)
            return string.Empty;

        return string.Concat(items.Select(BuildNavItemHtml));
    }

    private static string BuildNavItemHtml(NavItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Text))
            return string.Empty;

        if (item.Items.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(item.Href))
                return string.Empty;

            return BuildAnchorHtml(item, cssClass: null);
        }

        // Keep API docs header behavior consistent with normal site headers:
        // nested menu items are rendered as dropdowns instead of flattened links.
        return JoinHtmlFragments(
            "<div class=\"nav-dropdown\">",
            BuildDropdownTriggerHtml(item),
            "<div class=\"nav-dropdown-menu\">",
            BuildDropdownItemsHtml(item.Items),
            "</div></div>");
    }

    private static string BuildDropdownItemsHtml(IReadOnlyList<NavItem> items)
    {
        if (items is null || items.Count == 0)
            return string.Empty;

        var fragments = new List<string>(items.Count);
        foreach (var child in items)
        {
            if (child is null || string.IsNullOrWhiteSpace(child.Text))
                continue;

            if (!string.IsNullOrWhiteSpace(child.Href))
            {
                fragments.Add(BuildAnchorHtml(child, cssClass: null));
            }
            else if (child.Items.Count > 0)
            {
                // Group label for nested menu sections without direct link.
                fragments.Add(
                    $"<div class=\"nav-dropdown-group\"><span class=\"nav-dropdown-label\">{System.Web.HttpUtility.HtmlEncode(child.Text)}</span></div>");
            }

            if (child.Items.Count > 0)
                fragments.Add(BuildDropdownItemsHtml(child.Items));
        }

        return string.Concat(fragments);
    }

    private static string BuildDropdownTriggerHtml(NavItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Text))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(item.Href))
        {
            return BuildAnchorHtml(item, cssClass: "nav-dropdown-trigger", includeArrow: true);
        }

        return $"<button type=\"button\" class=\"nav-dropdown-trigger nav-dropdown-trigger-button\">{System.Web.HttpUtility.HtmlEncode(item.Text)}<svg class=\"nav-dropdown-arrow\" viewBox=\"0 0 12 12\" width=\"10\" height=\"10\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" aria-hidden=\"true\"><path d=\"M3 5l3 3 3-3\"/></svg></button>";
    }

    private static string BuildAnchorHtml(NavItem item, string? cssClass, bool includeArrow = false)
    {
        if (string.IsNullOrWhiteSpace(item.Href))
            return string.Empty;

        var href = System.Web.HttpUtility.HtmlEncode(item.Href);
        var text = System.Web.HttpUtility.HtmlEncode(item.Text);
        var target = item.Target;
        var rel = item.Rel;
        if (string.IsNullOrWhiteSpace(target) && item.External)
            target = "_blank";
        if (string.IsNullOrWhiteSpace(rel) && item.External)
            rel = "noopener";

        var attributes = new List<string> { $"href=\"{href}\"" };
        if (!string.IsNullOrWhiteSpace(cssClass))
            attributes.Add($"class=\"{System.Web.HttpUtility.HtmlEncode(cssClass)}\"");
        if (!string.IsNullOrWhiteSpace(target))
            attributes.Add($"target=\"{System.Web.HttpUtility.HtmlEncode(target)}\"");
        if (!string.IsNullOrWhiteSpace(rel))
            attributes.Add($"rel=\"{System.Web.HttpUtility.HtmlEncode(rel)}\"");

        var arrow = includeArrow
            ? "<svg class=\"nav-dropdown-arrow\" viewBox=\"0 0 12 12\" width=\"10\" height=\"10\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" aria-hidden=\"true\"><path d=\"M3 5l3 3 3-3\"/></svg>"
            : string.Empty;
        return $"<a {string.Join(" ", attributes)}>{text}{arrow}</a>";
    }

    private static string BuildActionHtml(IReadOnlyList<NavAction> actions)
    {
        if (actions.Count == 0)
            return string.Empty;

        return string.Concat(actions.Select(BuildActionHtml));
    }

    private static string BuildActionHtml(NavAction action)
    {
        if (action is null)
            return string.Empty;

        var isButton = string.Equals(action.Kind, "button", StringComparison.OrdinalIgnoreCase);
        if (!isButton && string.IsNullOrWhiteSpace(action.Href))
            return string.Empty;

        var title = action.Title;
        var ariaLabel = string.IsNullOrWhiteSpace(action.AriaLabel) ? title : action.AriaLabel;
        var iconHtml = string.IsNullOrWhiteSpace(action.IconHtml) ? null : action.IconHtml;
        var text = string.IsNullOrWhiteSpace(action.Text) ? null : action.Text;
        var hasIcon = !string.IsNullOrWhiteSpace(iconHtml);
        if (text is null && !hasIcon && !string.IsNullOrWhiteSpace(title))
            text = title;

        var content = hasIcon && !string.IsNullOrWhiteSpace(text)
            ? $"{iconHtml} {System.Web.HttpUtility.HtmlEncode(text)}"
            : hasIcon
                ? iconHtml!
                : !string.IsNullOrWhiteSpace(text)
                    ? System.Web.HttpUtility.HtmlEncode(text)
                    : string.Empty;

        if (isButton)
        {
            var attributes = new List<string> { "type=\"button\"" };
            if (!string.IsNullOrWhiteSpace(action.CssClass))
                attributes.Add($"class=\"{System.Web.HttpUtility.HtmlEncode(action.CssClass)}\"");
            if (!string.IsNullOrWhiteSpace(title))
                attributes.Add($"title=\"{System.Web.HttpUtility.HtmlEncode(title)}\"");
            if (!string.IsNullOrWhiteSpace(ariaLabel))
                attributes.Add($"aria-label=\"{System.Web.HttpUtility.HtmlEncode(ariaLabel)}\"");
            return $"<button {string.Join(" ", attributes)}>{content}</button>";
        }

        var href = System.Web.HttpUtility.HtmlEncode(action.Href ?? string.Empty);
        var external = action.External || IsExternal(action.Href ?? string.Empty);
        var target = action.Target;
        var rel = action.Rel;
        if (external && string.IsNullOrWhiteSpace(target))
            target = "_blank";
        if (external && string.IsNullOrWhiteSpace(rel))
            rel = "noopener";

        var linkAttributes = new List<string> { $"href=\"{href}\"" };
        if (!string.IsNullOrWhiteSpace(action.CssClass))
            linkAttributes.Add($"class=\"{System.Web.HttpUtility.HtmlEncode(action.CssClass)}\"");
        if (!string.IsNullOrWhiteSpace(target))
            linkAttributes.Add($"target=\"{System.Web.HttpUtility.HtmlEncode(target)}\"");
        if (!string.IsNullOrWhiteSpace(rel))
            linkAttributes.Add($"rel=\"{System.Web.HttpUtility.HtmlEncode(rel)}\"");
        if (!string.IsNullOrWhiteSpace(title))
            linkAttributes.Add($"title=\"{System.Web.HttpUtility.HtmlEncode(title)}\"");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
            linkAttributes.Add($"aria-label=\"{System.Web.HttpUtility.HtmlEncode(ariaLabel)}\"");

        return $"<a {string.Join(" ", linkAttributes)}>{content}</a>";
    }
}
