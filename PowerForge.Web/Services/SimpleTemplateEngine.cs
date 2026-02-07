using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal sealed class SimpleTemplateEngine : ITemplateEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex TokenRegex = new Regex(@"\{\{\s*(?<key>[A-Za-z0-9_\-]+)\s*\}\}", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex PartialRegex = new Regex(@"\{\{>\s*(?<name>[^\s}]+)\s*\}\}", RegexOptions.Compiled, RegexTimeout);

    public string Render(string template, ThemeRenderContext context, Func<string, string?> partialResolver)
    {
        var tokens = BuildTokens(context);
        return RenderTemplate(template, tokens, partialResolver);
    }

    private static Dictionary<string, string> BuildTokens(ThemeRenderContext context)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(context.Page.Title),
            ["DESCRIPTION"] = System.Web.HttpUtility.HtmlEncode(context.Page.Description ?? string.Empty),
            ["CONTENT"] = context.Page.HtmlContent,
            ["EDIT_URL"] = context.Page.EditUrl ?? string.Empty,
            ["ASSET_CSS"] = context.CssHtml,
            ["ASSET_JS"] = context.JsHtml,
            ["PRELOADS"] = context.PreloadsHtml,
            ["CRITICAL_CSS"] = context.CriticalCssHtml,
            ["CANONICAL"] = context.CanonicalHtml,
            ["DESCRIPTION_META"] = context.DescriptionMetaHtml,
            ["HEAD_HTML"] = context.HeadHtml,
            ["OPENGRAPH"] = context.OpenGraphHtml,
            ["STRUCTURED_DATA"] = context.StructuredDataHtml,
            ["EXTRA_CSS"] = context.ExtraCssHtml,
            ["EXTRA_SCRIPTS"] = context.ExtraScriptsHtml,
            ["BODY_CLASS"] = string.IsNullOrWhiteSpace(context.BodyClass) ? string.Empty : $" class=\"{context.BodyClass}\"",
            ["SITE_NAME"] = context.Site.Name ?? string.Empty,
            ["BASE_URL"] = context.Site.BaseUrl ?? string.Empty,
            ["VERSION_CURRENT"] = context.Versioning.Current?.Name ?? string.Empty,
            ["VERSION_CURRENT_LABEL"] = context.Versioning.Current?.Label ?? string.Empty,
            ["VERSION_CURRENT_URL"] = context.Versioning.Current?.Url ?? string.Empty,
            ["VERSION_LATEST"] = context.Versioning.Latest?.Name ?? string.Empty,
            ["VERSION_LATEST_LABEL"] = context.Versioning.Latest?.Label ?? string.Empty,
            ["VERSION_LATEST_URL"] = context.Versioning.Latest?.Url ?? string.Empty
        };
    }

    private static string RenderTemplate(string template, IReadOnlyDictionary<string, string> tokens, Func<string, string?> partialResolver)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var withPartials = PartialRegex.Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var partial = partialResolver(name);
            return partial ?? string.Empty;
        });

        return TokenRegex.Replace(withPartials, match =>
        {
            var key = match.Groups["key"].Value;
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            return tokens.TryGetValue(key, out var value) ? value : string.Empty;
        });
    }
}
