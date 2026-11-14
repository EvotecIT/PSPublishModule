using System.Globalization;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static void ApplyTechArticleMetadata(Dictionary<string, object?> model, ContentItem item, string? baseUrl)
    {
        foreach (var property in new[] { "about", "mentions" })
        {
            var raw = TryGetMetaValue(item.Meta, "schema." + property, out var value) ? value : null;
            var values = raw is null ? Array.Empty<object?>() : TryAsDictionary(raw, out _) ? new[] { raw } : ExpandListValue(raw);
            var entities = values
                .Where(value => value is not null)
                .Select(value => NormalizeStructuredEntity(value!, baseUrl))
                .ToArray();
            if (entities.Length > 0)
                model[property] = entities;
        }

        var dependencies = string.Join(", ", ReadMetaStringList(item.Meta, "schema.dependencies"));
        if (!string.IsNullOrWhiteSpace(dependencies))
            model["dependencies"] = dependencies;

        var relatedLinks = ReadMetaList(item.Meta, "schema.related_link", "schema.relatedLink")
            .Select(ConvertToString).Where(link => !string.IsNullOrWhiteSpace(link))
            .Select(link => ResolveAbsoluteUrl(baseUrl, link)).ToArray();
        if (relatedLinks.Length > 0)
            model["relatedLink"] = relatedLinks;

        var modified = ReadMetaString(item.Meta, "schema.date_modified", "schema.dateModified");
        if (!string.IsNullOrWhiteSpace(modified) && DateTimeOffset.TryParse(modified, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var date))
            model["dateModified"] = date.ToUniversalTime().ToString("O");
    }

    private static object NormalizeStructuredEntity(object value, string? baseUrl)
    {
        if (!TryAsDictionary(value, out var map))
            return ConvertToString(value);

        var entity = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in map)
        {
            var key = pair.Key switch
            {
                "type" => "@type",
                "id" => "@id",
                "code_repository" => "codeRepository",
                "programming_language" => "programmingLanguage",
                "runtime_platform" => "runtimePlatform",
                _ => pair.Key
            };
            var normalized = NormalizeStructuredDataValue(pair.Value);
            if (key is "url" or "@id" or "codeRepository" or "relatedLink" && normalized is string url)
                normalized = ResolveAbsoluteUrl(baseUrl, url);
            entity[key] = normalized;
        }
        return entity;
    }
}
