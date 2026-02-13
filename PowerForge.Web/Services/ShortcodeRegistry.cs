using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Delegate signature for shortcode handlers.</summary>
/// <param name="context">Render context.</param>
/// <param name="attrs">Shortcode attributes.</param>
/// <returns>Rendered HTML.</returns>
public delegate string ShortcodeHandler(ShortcodeRenderContext context, Dictionary<string, string> attrs);

/// <summary>Registers and resolves shortcode handlers.</summary>
public static class ShortcodeRegistry
{
    private static readonly ConcurrentDictionary<string, ShortcodeHandler> Handlers =
        new(StringComparer.OrdinalIgnoreCase);

    static ShortcodeRegistry()
    {
        Register("cards", ShortcodeDefaults.RenderCards);
        Register("metrics", ShortcodeDefaults.RenderMetrics);
        Register("showcase", ShortcodeDefaults.RenderShowcase);
        Register("faq", ShortcodeDefaults.RenderFaq);
        Register("benchmarks", ShortcodeDefaults.RenderBenchmarks);
        Register("pricing", ShortcodeDefaults.RenderPricing);
        Register("edit-link", ShortcodeDefaults.RenderEditLink);
        Register("media", ShortcodeDefaults.RenderMedia);
        Register("youtube", ShortcodeDefaults.RenderYouTube);
        Register("x", ShortcodeDefaults.RenderXPost);
        Register("tweet", ShortcodeDefaults.RenderXPost);
        Register("screenshot", ShortcodeDefaults.RenderScreenshot);
        Register("screenshots", ShortcodeDefaults.RenderScreenshots);
    }

    /// <summary>Registers a shortcode handler.</summary>
    /// <param name="name">Shortcode name.</param>
    /// <param name="handler">Handler implementation.</param>
    public static void Register(string name, ShortcodeHandler handler)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Shortcode name is required.", nameof(name));
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));
        Handlers[name.Trim()] = handler;
    }

    internal static bool TryGet(string name, out ShortcodeHandler handler)
        => Handlers.TryGetValue(name, out handler!);
}

internal static partial class ShortcodeDefaults
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex HtmlTagRegex = new("<\\s*/?\\s*[a-zA-Z][^>]*>", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex MarkdownLinkRegex = new("\\[[^\\]]+\\]\\([^\\)]+\\)", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static string HtmlAny(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ShortcodeProcessor.Html(map, key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return string.Empty;
    }

    private static string RawAny(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;

            var raw = value.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
                return raw.Trim();
        }

        return string.Empty;
    }

    internal static string RenderCards(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var list = ShortcodeProcessor.ResolveList(context.Data, attrs);
        if (list is null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<div class=\"pf-grid\">");
        foreach (var item in list)
        {
            if (item is not IReadOnlyDictionary<string, object?> map)
                continue;

            var title = ShortcodeProcessor.Html(map, "title");
            var text = ShortcodeProcessor.Html(map, "text");
            var tag = ShortcodeProcessor.Html(map, "tag");

            sb.AppendLine("  <div class=\"pf-card\">");
            if (!string.IsNullOrWhiteSpace(tag))
                sb.AppendLine($"    <div class=\"pf-card-tag\">{tag}</div>");
            if (!string.IsNullOrWhiteSpace(title))
                sb.AppendLine($"    <h3>{title}</h3>");
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine($"    <p>{text}</p>");
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    internal static string RenderMetrics(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var list = ShortcodeProcessor.ResolveList(context.Data, attrs);
        if (list is null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<div class=\"pf-metrics\">");
        foreach (var item in list)
        {
            if (item is not IReadOnlyDictionary<string, object?> map)
                continue;

            var value = ShortcodeProcessor.Html(map, "value");
            var label = ShortcodeProcessor.Html(map, "label");

            sb.AppendLine("  <div class=\"pf-metric\">");
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"    <strong>{value}</strong>");
            if (!string.IsNullOrWhiteSpace(label))
                sb.AppendLine($"    <span>{label}</span>");
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    internal static string RenderShowcase(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var list = ShortcodeProcessor.ResolveList(context.Data, attrs);
        if (list is null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<div class=\"pf-showcase\">");
        foreach (var item in list)
        {
            if (item is not IReadOnlyDictionary<string, object?> map)
                continue;

            var title = ShortcodeProcessor.Html(map, "title");
            var summary = ShortcodeProcessor.Html(map, "summary");
            var link = ShortcodeProcessor.Html(map, "link");

            sb.AppendLine($"  <a class=\"pf-showcase-card\" href=\"{link}\">");
            if (!string.IsNullOrWhiteSpace(title))
                sb.AppendLine($"    <h3>{title}</h3>");
            if (!string.IsNullOrWhiteSpace(summary))
                sb.AppendLine($"    <p>{summary}</p>");

            if (map.TryGetValue("chips", out var chipsObj) && chipsObj is IEnumerable<object?> chips)
            {
                sb.AppendLine("    <div class=\"pf-showcase-chips\">");
                foreach (var chip in chips)
                {
                    var label = ShortcodeProcessor.HtmlValue(chip);
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    sb.AppendLine($"      <span class=\"pf-chip\">{label}</span>");
                }
                sb.AppendLine("    </div>");
            }

            sb.AppendLine("  </a>");
        }
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    internal static string RenderFaq(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var list = ShortcodeProcessor.ResolveList(context.Data, attrs);
        if (list is null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<div class=\"pf-faq\">");
        foreach (var sectionObj in list)
        {
            if (sectionObj is not IReadOnlyDictionary<string, object?> section)
                continue;

            var sectionTitle = HtmlAny(section, "title", "label", "name");
            sb.AppendLine("  <section class=\"pf-faq-section\">");
            if (!string.IsNullOrWhiteSpace(sectionTitle))
                sb.AppendLine($"    <h2>{sectionTitle}</h2>");

            if (section.TryGetValue("items", out var itemsObj) && itemsObj is IEnumerable<object?> items)
            {
                foreach (var itemObj in items)
                {
                    if (itemObj is not IReadOnlyDictionary<string, object?> item)
                        continue;

                    var id = HtmlAny(item, "id");
                    var question = ResolveFaqInline(item, "question", "q", "title");
                    var answer = ResolveFaqBlock(item, "answer", "a", "text", "summary");

                    sb.Append($"    <div class=\"pf-faq-item\"");
                    if (!string.IsNullOrWhiteSpace(id))
                        sb.Append($" id=\"{id}\"");
                    sb.AppendLine(">");
                    if (!string.IsNullOrWhiteSpace(question))
                        sb.AppendLine($"      <h3>{question}</h3>");
                    if (!string.IsNullOrWhiteSpace(answer))
                        sb.AppendLine($"      <div class=\"pf-faq-answer\">{answer}</div>");
                    sb.AppendLine("    </div>");
                }
            }

            sb.AppendLine("  </section>");
        }
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string ResolveFaqInline(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        var html = ResolveFaqContent(map, keys);
        return StripSingleParagraphWrapper(html);
    }

    private static string ResolveFaqBlock(IReadOnlyDictionary<string, object?> map, params string[] keys)
        => ResolveFaqContent(map, keys);

    private static string ResolveFaqContent(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        var htmlKeys = keys.Select(k => $"{k}_html").ToArray();
        var markdownKeys = keys
            .SelectMany(k => new[] { $"{k}_md", $"{k}_markdown" })
            .ToArray();

        var explicitHtml = RawAny(map, htmlKeys);
        if (!string.IsNullOrWhiteSpace(explicitHtml))
            return explicitHtml;

        var explicitMarkdown = RawAny(map, markdownKeys);
        if (!string.IsNullOrWhiteSpace(explicitMarkdown))
            return MarkdownRenderer.RenderToHtml(explicitMarkdown);

        var raw = RawAny(map, keys);
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        if (LooksLikeHtml(raw))
            return raw;

        if (LooksLikeMarkdown(raw))
            return MarkdownRenderer.RenderToHtml(raw);

        return System.Web.HttpUtility.HtmlEncode(raw);
    }

    private static bool LooksLikeHtml(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return HtmlTagRegex.IsMatch(value);
    }

    private static bool LooksLikeMarkdown(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.Contains("**", StringComparison.Ordinal) ||
            value.Contains("__", StringComparison.Ordinal) ||
            value.Contains("`", StringComparison.Ordinal) ||
            value.Contains("```", StringComparison.Ordinal))
            return true;

        if (value.Contains("\n- ", StringComparison.Ordinal) ||
            value.Contains("\n* ", StringComparison.Ordinal) ||
            value.Contains("\n1. ", StringComparison.Ordinal) ||
            value.StartsWith("> ", StringComparison.Ordinal))
            return true;

        if (value.Contains("# ", StringComparison.Ordinal) || value.Contains("## ", StringComparison.Ordinal))
            return true;

        return MarkdownLinkRegex.IsMatch(value);
    }

    private static string StripSingleParagraphWrapper(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var trimmed = html.Trim();
        if (!trimmed.StartsWith("<p>", StringComparison.OrdinalIgnoreCase) ||
            !trimmed.EndsWith("</p>", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        var inner = trimmed.Substring(3, trimmed.Length - 7);
        if (inner.IndexOf("<p>", StringComparison.OrdinalIgnoreCase) >= 0 ||
            inner.IndexOf("</p>", StringComparison.OrdinalIgnoreCase) >= 0)
            return trimmed;

        return inner.Trim();
    }

    internal static string RenderBenchmarks(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var list = ShortcodeProcessor.ResolveList(context.Data, attrs);
        if (list is null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<div class=\"pf-benchmarks\">");
        sb.AppendLine("  <table class=\"pf-benchmarks-table\">");
        sb.AppendLine("    <thead><tr><th>Scenario</th><th>Result</th><th>Notes</th></tr></thead>");
        sb.AppendLine("    <tbody>");
        foreach (var itemObj in list)
        {
            if (itemObj is not IReadOnlyDictionary<string, object?> item)
                continue;

            var label = HtmlAny(item, "title", "name", "scenario");
            var value = HtmlAny(item, "value", "score", "result");
            var unit = HtmlAny(item, "unit");
            var notes = HtmlAny(item, "note", "notes", "summary");
            var result = string.IsNullOrWhiteSpace(unit) ? value : $"{value} {unit}".Trim();

            sb.AppendLine("      <tr>");
            sb.AppendLine($"        <td>{label}</td>");
            sb.AppendLine($"        <td>{result}</td>");
            sb.AppendLine($"        <td>{notes}</td>");
            sb.AppendLine("      </tr>");
        }
        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    internal static string RenderEditLink(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var url = context.EditUrl;
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var label = attrs.TryGetValue("label", out var labelValue) && !string.IsNullOrWhiteSpace(labelValue)
            ? labelValue
            : "Edit on GitHub";
        var cssClass = attrs.TryGetValue("class", out var classValue) && !string.IsNullOrWhiteSpace(classValue)
            ? classValue
            : "edit-on-github";

        return $@"<div class=""{System.Web.HttpUtility.HtmlEncode(cssClass)}"">
    <a href=""{System.Web.HttpUtility.HtmlEncode(url)}"" target=""_blank"" rel=""noopener"">
        <svg viewBox=""0 0 24 24"" fill=""currentColor"" width=""16"" height=""16"" aria-hidden=""true"" focusable=""false"">
            <path d=""M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z""/>
        </svg>
        {System.Web.HttpUtility.HtmlEncode(label)}
    </a>
</div>";
    }

    internal static string RenderPricing(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var list = ShortcodeProcessor.ResolveList(context.Data, attrs);
        if (list is null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<div class=\"pf-pricing\">");
        foreach (var itemObj in list)
        {
            if (itemObj is not IReadOnlyDictionary<string, object?> item)
                continue;

            var name = HtmlAny(item, "name", "title");
            var price = HtmlAny(item, "price");
            var period = HtmlAny(item, "period");
            var description = HtmlAny(item, "description", "summary");
            var ctaLabel = HtmlAny(item, "cta_label", "ctaLabel", "cta");
            var ctaHref = HtmlAny(item, "cta_href", "ctaHref", "href", "url");

            sb.AppendLine("  <div class=\"pf-pricing-tier\">");
            if (!string.IsNullOrWhiteSpace(name))
                sb.AppendLine($"    <h3>{name}</h3>");
            if (!string.IsNullOrWhiteSpace(price))
            {
                var suffix = string.IsNullOrWhiteSpace(period) ? string.Empty : $" <span class=\"pf-pricing-period\">{period}</span>";
                sb.AppendLine($"    <div class=\"pf-pricing-price\">{price}{suffix}</div>");
            }
            if (!string.IsNullOrWhiteSpace(description))
                sb.AppendLine($"    <p>{description}</p>");

            if (item.TryGetValue("features", out var featuresObj) && featuresObj is IEnumerable<object?> features)
            {
                sb.AppendLine("    <ul class=\"pf-pricing-features\">");
                foreach (var feature in features)
                {
                    var label = ShortcodeProcessor.HtmlValue(feature);
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    sb.AppendLine($"      <li>{label}</li>");
                }
                sb.AppendLine("    </ul>");
            }

            if (!string.IsNullOrWhiteSpace(ctaLabel) && !string.IsNullOrWhiteSpace(ctaHref))
                sb.AppendLine($"    <a class=\"pf-pricing-cta\" href=\"{ctaHref}\">{ctaLabel}</a>");

            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</div>");
        return sb.ToString();
    }
}
