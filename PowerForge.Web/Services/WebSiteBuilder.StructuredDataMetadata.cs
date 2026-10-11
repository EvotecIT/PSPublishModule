using System.Collections;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    internal static string RenderStructuredData(SiteSpec spec, ContentItem item, BreadcrumbItem[] breadcrumbs, object? metadata)
    {
        // A theme cannot re-enable a page that its author has explicitly excluded.
        if (TryGetMetaBool(item.Meta, "structured_data", out var enabled) && !enabled)
            return string.Empty;
        if (metadata is null)
            return BuildStructuredDataHtml(spec, item, breadcrumbs);
        if (!TryAsDictionary(metadata, out var map))
            throw new ArgumentException("Structured data metadata must be an object.", nameof(metadata));

        var page = CloneContentItem(item);
        page.LastModifiedUtc = item.LastModifiedUtc;
        foreach (var pair in map)
            page.Meta[pair.Key] = NormalizeStructuredDataValue(pair.Value);
        return BuildStructuredDataHtml(spec, page, breadcrumbs);
    }

    private static object? NormalizeStructuredDataValue(object? value)
    {
        if (value is null || value is string)
            return value;
        if (TryAsDictionary(value, out var map))
            return map.ToDictionary(pair => pair.Key, pair => NormalizeStructuredDataValue(pair.Value), StringComparer.OrdinalIgnoreCase);
        if (value is IEnumerable items)
            return items.Cast<object?>().Select(NormalizeStructuredDataValue).ToArray();
        return value;
    }

    private static IEnumerable<object?> ReadFaqEntries(Dictionary<string, object?> meta)
    {
        var direct = ReadMetaList(meta, "faq.questions", "faq.items", "faq.qa", "faq.questions_json", "faq.items_json");
        if (direct.Length > 0)
            return direct;
        return ReadMetaList(meta, "faq.sections")
            .Where(section => section is not null)
            .SelectMany(section => TryAsDictionary(section!, out var map) && map.TryGetValue("items", out var items) && items is not null
                ? ExpandListValue(items) : Array.Empty<object?>());
    }

    private static string ReadFaqAnswer(IReadOnlyDictionary<string, object?> map)
    {
        foreach (var key in new[] { "answer", "a", "text", "content", "description" })
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;
            var paragraphs = ExpandListValue(value).Select(ConvertToString).Where(text => !string.IsNullOrWhiteSpace(text));
            var answer = string.Join("\n\n", paragraphs);
            if (!string.IsNullOrWhiteSpace(answer))
                return answer;
        }
        return string.Empty;
    }
}
