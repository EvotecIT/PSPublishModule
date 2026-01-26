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
}
