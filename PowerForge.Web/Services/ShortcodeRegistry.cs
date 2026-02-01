using System.Collections.Concurrent;

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

internal static class ShortcodeDefaults
{
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
                    var question = HtmlAny(item, "question", "q", "title");
                    var answer = HtmlAny(item, "answer", "a", "text", "summary");

                    sb.Append($"    <div class=\"pf-faq-item\"");
                    if (!string.IsNullOrWhiteSpace(id))
                        sb.Append($" id=\"{id}\"");
                    sb.AppendLine(">");
                    if (!string.IsNullOrWhiteSpace(question))
                        sb.AppendLine($"      <h3>{question}</h3>");
                    if (!string.IsNullOrWhiteSpace(answer))
                        sb.AppendLine($"      <p>{answer}</p>");
                    sb.AppendLine("    </div>");
                }
            }

            sb.AppendLine("  </section>");
        }
        sb.AppendLine("</div>");
        return sb.ToString();
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
