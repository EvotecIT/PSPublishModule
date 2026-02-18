using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string ResolveStructuredArticleType(StructuredDataSpec config, ContentItem item)
    {
        if (config is null || item is null)
            return string.Empty;

        if (config.NewsArticle && IsNewsLikePage(item))
            return "NewsArticle";

        if (config.Article && IsArticleLikePage(item))
            return "Article";

        return string.Empty;
    }

    private static string BuildArticleStructuredDataScript(SiteSpec spec, ContentItem item, string pageUrl, string typeName)
    {
        if (item is null || string.IsNullOrWhiteSpace(typeName))
            return string.Empty;

        var articleModel = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = typeName,
            ["headline"] = item.Title,
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = pageUrl
            },
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = ReadMetaString(item.Meta, "author.name", "author", "article.author", "news.author", "schema.author")
            },
            ["publisher"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = ReadMetaString(item.Meta, "publisher.name", "publisher", "article.publisher", "news.publisher", "schema.publisher")
            }
        };

        var authorName = ((Dictionary<string, object?>)articleModel["author"]!)["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(authorName))
            ((Dictionary<string, object?>)articleModel["author"]!)["name"] = spec.Name;

        var publisherName = ((Dictionary<string, object?>)articleModel["publisher"]!)["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(publisherName))
            ((Dictionary<string, object?>)articleModel["publisher"]!)["name"] = spec.Name;

        var articleDescription = string.IsNullOrWhiteSpace(item.Description)
            ? BuildSnippet(item.HtmlContent, 200)
            : item.Description;
        if (!string.IsNullOrWhiteSpace(articleDescription))
            articleModel["description"] = articleDescription;

        if (item.Date.HasValue)
        {
            var iso = item.Date.Value.ToUniversalTime().ToString("O");
            articleModel["datePublished"] = iso;
            articleModel["dateModified"] = iso;
        }

        var imageOverride = ReadMetaString(item.Meta, "article.image", "news.image", "schema.image", "social_image");
        var imagePath = ResolveSocialImagePath(spec, item, string.Empty, item.Title, articleDescription, spec.Name, imageOverride);
        var image = ResolveAbsoluteUrl(spec.BaseUrl, imagePath);
        if (!string.IsNullOrWhiteSpace(image))
            articleModel["image"] = image;

        if (item.Tags is { Length: > 0 })
        {
            var keywords = item.Tags
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Select(static tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (keywords.Length > 0)
                articleModel["keywords"] = string.Join(", ", keywords);
        }

        return BuildJsonLdScript(articleModel);
    }

    private static bool IsNewsLikePage(ContentItem item)
    {
        if (!IsArticleLikePage(item))
            return false;

        if (!string.IsNullOrWhiteSpace(item.Collection) &&
            item.Collection.Equals("news", StringComparison.OrdinalIgnoreCase))
            return true;

        if (TryGetMetaBool(item.Meta, "news", out var isNews) && isNews)
            return true;

        if (TryGetMetaString(item.Meta, "schema.type", out var schemaType) &&
            schemaType.Equals("NewsArticle", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string BuildFaqStructuredDataScript(ContentItem item, string pageUrl)
    {
        var entries = ReadMetaList(item.Meta, "faq.questions", "faq.items", "faq.qa", "faq.questions_json", "faq.items_json")
            .Select(ParseFaqEntry)
            .Where(static entry => entry is not null)
            .Cast<StructuredQuestionAnswer>()
            .ToArray();

        if (entries.Length == 0)
            return string.Empty;

        var faqModel = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "FAQPage",
            ["mainEntity"] = entries.Select(static entry => new Dictionary<string, object?>
            {
                ["@type"] = "Question",
                ["name"] = entry.Question,
                ["acceptedAnswer"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Answer",
                    ["text"] = entry.Answer
                }
            }).ToArray()
        };

        var faqName = ReadMetaString(item.Meta, "faq.name");
        if (!string.IsNullOrWhiteSpace(faqName))
            faqModel["name"] = faqName;
        else if (!string.IsNullOrWhiteSpace(item.Title))
            faqModel["name"] = item.Title;

        var faqDescription = ReadMetaString(item.Meta, "faq.description");
        if (!string.IsNullOrWhiteSpace(faqDescription))
            faqModel["description"] = faqDescription;

        if (!string.IsNullOrWhiteSpace(pageUrl))
            faqModel["@id"] = pageUrl + "#faq";

        return BuildJsonLdScript(faqModel);
    }

    private static string BuildHowToStructuredDataScript(ContentItem item, string pageUrl)
    {
        var steps = ReadMetaList(item.Meta, "howto.steps", "howto.items", "howto.steps_json")
            .Select(ParseHowToStep)
            .Where(static step => step is not null)
            .Cast<StructuredHowToStep>()
            .ToArray();

        if (steps.Length == 0)
            return string.Empty;

        var howToName = ReadMetaString(item.Meta, "howto.name", "howto.title");
        if (string.IsNullOrWhiteSpace(howToName))
            howToName = item.Title;
        if (string.IsNullOrWhiteSpace(howToName))
            return string.Empty;

        var howToModel = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "HowTo",
            ["name"] = howToName,
            ["step"] = steps.Select(step => BuildHowToStepModel(step)).ToArray()
        };

        var description = ReadMetaString(item.Meta, "howto.description");
        if (string.IsNullOrWhiteSpace(description))
            description = item.Description;
        if (!string.IsNullOrWhiteSpace(description))
            howToModel["description"] = description;

        var totalTime = ReadMetaString(item.Meta, "howto.total_time", "howto.totalTime");
        if (!string.IsNullOrWhiteSpace(totalTime))
            howToModel["totalTime"] = totalTime;

        var supplies = ReadMetaStringList(item.Meta, "howto.supplies", "howto.supply");
        if (supplies.Length > 0)
        {
            howToModel["supply"] = supplies.Select(static name => new Dictionary<string, object?>
            {
                ["@type"] = "HowToSupply",
                ["name"] = name
            }).ToArray();
        }

        var tools = ReadMetaStringList(item.Meta, "howto.tools", "howto.tool");
        if (tools.Length > 0)
        {
            howToModel["tool"] = tools.Select(static name => new Dictionary<string, object?>
            {
                ["@type"] = "HowToTool",
                ["name"] = name
            }).ToArray();
        }

        if (!string.IsNullOrWhiteSpace(pageUrl))
            howToModel["@id"] = pageUrl + "#howto";

        return BuildJsonLdScript(howToModel);
    }

    private static Dictionary<string, object?> BuildHowToStepModel(StructuredHowToStep step)
    {
        var model = new Dictionary<string, object?>
        {
            ["@type"] = "HowToStep",
            ["name"] = step.Name
        };
        if (!string.IsNullOrWhiteSpace(step.Text))
            model["text"] = step.Text;
        if (!string.IsNullOrWhiteSpace(step.Url))
            model["url"] = step.Url;
        if (!string.IsNullOrWhiteSpace(step.Image))
            model["image"] = step.Image;
        return model;
    }

    private static string BuildSoftwareApplicationStructuredDataScript(SiteSpec spec, ContentItem item, string pageUrl)
    {
        if (!HasAnyMetaValue(item.Meta, "software", "softwareapplication"))
            return string.Empty;

        var name = ReadMetaString(item.Meta, "software.name", "softwareapplication.name", "software.title");
        if (string.IsNullOrWhiteSpace(name))
            name = item.Title;
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var appModel = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "SoftwareApplication",
            ["name"] = name
        };

        var description = ReadMetaString(item.Meta, "software.description", "softwareapplication.description");
        if (string.IsNullOrWhiteSpace(description))
            description = item.Description;
        if (!string.IsNullOrWhiteSpace(description))
            appModel["description"] = description;

        var category = ReadMetaString(item.Meta, "software.category", "software.application_category", "softwareapplication.applicationCategory");
        if (!string.IsNullOrWhiteSpace(category))
            appModel["applicationCategory"] = category;

        var operatingSystem = ReadMetaString(item.Meta, "software.operating_system", "software.operatingSystem", "softwareapplication.operatingSystem");
        if (!string.IsNullOrWhiteSpace(operatingSystem))
            appModel["operatingSystem"] = operatingSystem;

        var version = ReadMetaString(item.Meta, "software.version", "softwareapplication.softwareVersion");
        if (!string.IsNullOrWhiteSpace(version))
            appModel["softwareVersion"] = version;

        var downloadUrl = ReadMetaString(item.Meta, "software.download_url", "software.downloadUrl", "softwareapplication.downloadUrl");
        if (!string.IsNullOrWhiteSpace(downloadUrl))
            appModel["downloadUrl"] = ResolveAbsoluteUrl(spec.BaseUrl, downloadUrl);

        var appImage = ReadMetaString(item.Meta, "software.image", "softwareapplication.image");
        if (string.IsNullOrWhiteSpace(appImage))
            appImage = ReadMetaString(item.Meta, "social_image");
        if (!string.IsNullOrWhiteSpace(appImage))
            appModel["image"] = ResolveAbsoluteUrl(spec.BaseUrl, appImage);

        var offer = BuildOfferModel(item.Meta, "software");
        if (offer is not null)
            appModel["offers"] = offer;

        var rating = BuildAggregateRatingModel(item.Meta, "software");
        if (rating is not null)
            appModel["aggregateRating"] = rating;

        if (!string.IsNullOrWhiteSpace(pageUrl))
            appModel["url"] = pageUrl;

        return BuildJsonLdScript(appModel);
    }

    private static string BuildProductStructuredDataScript(SiteSpec spec, ContentItem item, string pageUrl)
    {
        if (!HasAnyMetaValue(item.Meta, "product"))
            return string.Empty;

        var name = ReadMetaString(item.Meta, "product.name", "product.title");
        if (string.IsNullOrWhiteSpace(name))
            name = item.Title;
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var productModel = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Product",
            ["name"] = name
        };

        var description = ReadMetaString(item.Meta, "product.description");
        if (string.IsNullOrWhiteSpace(description))
            description = item.Description;
        if (string.IsNullOrWhiteSpace(description))
            description = BuildSnippet(item.HtmlContent, 200);
        if (!string.IsNullOrWhiteSpace(description))
            productModel["description"] = description;

        var image = ReadMetaString(item.Meta, "product.image");
        if (string.IsNullOrWhiteSpace(image))
            image = ReadMetaString(item.Meta, "social_image");
        if (!string.IsNullOrWhiteSpace(image))
            productModel["image"] = ResolveAbsoluteUrl(spec.BaseUrl, image);

        var sku = ReadMetaString(item.Meta, "product.sku");
        if (!string.IsNullOrWhiteSpace(sku))
            productModel["sku"] = sku;

        var brand = ReadMetaString(item.Meta, "product.brand");
        if (!string.IsNullOrWhiteSpace(brand))
        {
            productModel["brand"] = new Dictionary<string, object?>
            {
                ["@type"] = "Brand",
                ["name"] = brand
            };
        }

        var offer = BuildOfferModel(item.Meta, "product");
        if (offer is not null)
            productModel["offers"] = offer;

        var rating = BuildAggregateRatingModel(item.Meta, "product");
        if (rating is not null)
            productModel["aggregateRating"] = rating;

        if (!string.IsNullOrWhiteSpace(pageUrl))
            productModel["url"] = pageUrl;

        return BuildJsonLdScript(productModel);
    }

    private static Dictionary<string, object?>? BuildOfferModel(Dictionary<string, object?> meta, string prefix)
    {
        var price = ReadMetaDecimal(meta, prefix + ".price");
        var currency = ReadMetaString(meta, prefix + ".price_currency", prefix + ".priceCurrency");
        if (!price.HasValue && string.IsNullOrWhiteSpace(currency))
            return null;

        var offer = new Dictionary<string, object?>
        {
            ["@type"] = "Offer"
        };

        if (price.HasValue)
            offer["price"] = price.Value.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(currency))
            offer["priceCurrency"] = currency;

        var availability = ReadMetaString(meta, prefix + ".availability");
        if (!string.IsNullOrWhiteSpace(availability))
            offer["availability"] = availability;

        var condition = ReadMetaString(meta, prefix + ".condition", prefix + ".itemCondition");
        if (!string.IsNullOrWhiteSpace(condition))
            offer["itemCondition"] = condition;

        return offer;
    }

    private static Dictionary<string, object?>? BuildAggregateRatingModel(Dictionary<string, object?> meta, string prefix)
    {
        var value = ReadMetaDecimal(meta, prefix + ".rating_value", prefix + ".ratingValue");
        if (!value.HasValue)
            return null;

        var rating = new Dictionary<string, object?>
        {
            ["@type"] = "AggregateRating",
            ["ratingValue"] = value.Value.ToString(CultureInfo.InvariantCulture)
        };

        var count = ReadMetaInt(meta, prefix + ".rating_count", prefix + ".ratingCount");
        if (count.HasValue && count.Value > 0)
            rating["ratingCount"] = count.Value;

        return rating;
    }

    private static StructuredQuestionAnswer? ParseFaqEntry(object? raw)
    {
        if (raw is null)
            return null;

        if (raw is string text)
        {
            var normalized = text.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            var parts = normalized.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                return new StructuredQuestionAnswer(parts[0], parts[1]);
            return null;
        }

        if (TryAsDictionary(raw, out var map))
        {
            var question = ReadMapString(map, "question", "q", "name", "title");
            var answer = ReadMapString(map, "answer", "a", "text", "content", "description");
            if (!string.IsNullOrWhiteSpace(question) && !string.IsNullOrWhiteSpace(answer))
                return new StructuredQuestionAnswer(question, answer);
        }

        return null;
    }

    private static StructuredHowToStep? ParseHowToStep(object? raw)
    {
        if (raw is null)
            return null;

        if (raw is string text)
        {
            var normalized = text.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            var parts = normalized.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
                return new StructuredHowToStep(parts[0], parts[1], string.Empty, string.Empty);
            return new StructuredHowToStep(normalized, normalized, string.Empty, string.Empty);
        }

        if (TryAsDictionary(raw, out var map))
        {
            var name = ReadMapString(map, "name", "title");
            var textValue = ReadMapString(map, "text", "content", "description");
            if (string.IsNullOrWhiteSpace(name))
                name = textValue;
            if (string.IsNullOrWhiteSpace(textValue))
                textValue = name;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var url = ReadMapString(map, "url");
            var image = ReadMapString(map, "image");
            return new StructuredHowToStep(name, textValue, url, image);
        }

        return null;
    }

    private static bool TryAsDictionary(object value, out IReadOnlyDictionary<string, object?> map)
    {
        if (value is IReadOnlyDictionary<string, object?> roMap)
        {
            map = roMap;
            return true;
        }

        if (value is Dictionary<string, object?> dict)
        {
            map = dict;
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            map = ConvertJsonObject(element);
            return true;
        }

        map = new Dictionary<string, object?>();
        return false;
    }

    private static IReadOnlyDictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            map[property.Name] = ConvertJsonValue(property.Value);
        return map;
    }

    private static object? ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToArray(),
            JsonValueKind.Object => ConvertJsonObject(element),
            _ => null
        };
    }

    private static object?[] ReadMetaList(Dictionary<string, object?> meta, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetMetaValue(meta, key, out var value) || value is null)
                continue;

            var items = ExpandListValue(value);
            if (items.Length > 0)
                return items;
        }

        return Array.Empty<object?>();
    }

    private static string[] ReadMetaStringList(Dictionary<string, object?> meta, params string[] keys)
    {
        var values = new List<string>();
        foreach (var value in ReadMetaList(meta, keys))
        {
            var text = ConvertToString(value);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(token))
                    values.Add(token);
            }
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static object?[] ExpandListValue(object value)
    {
        if (value is string text)
        {
            var trimmed = text.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        return doc.RootElement.EnumerateArray().Select(ConvertJsonValue).ToArray();
                }
                catch
                {
                    // Keep string payload as a single item when JSON parse fails.
                }
            }

            return new object?[] { trimmed };
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
                return element.EnumerateArray().Select(ConvertJsonValue).ToArray();
            return new[] { ConvertJsonValue(element) };
        }

        if (value is IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
                list.Add(item);
            return list.ToArray();
        }

        return new[] { value };
    }

    private static string ReadMetaString(Dictionary<string, object?> meta, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetMetaValue(meta, key, out var value) || value is null)
                continue;

            var text = ConvertToString(value);
            if (!string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }

        return string.Empty;
    }

    private static decimal? ReadMetaDecimal(Dictionary<string, object?> meta, params string[] keys)
    {
        var text = ReadMetaString(meta, keys);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;
        return null;
    }

    private static int? ReadMetaInt(Dictionary<string, object?> meta, params string[] keys)
    {
        var text = ReadMetaString(meta, keys);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        return null;
    }

    private static bool HasAnyMetaValue(Dictionary<string, object?> meta, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetMetaValue(meta, key, out var value) || value is null)
                continue;

            if (value is string text)
            {
                if (!string.IsNullOrWhiteSpace(text))
                    return true;
                continue;
            }

            if (value is IReadOnlyDictionary<string, object?> map)
                return map.Count > 0;

            if (value is IEnumerable enumerable)
            {
                var enumerator = enumerable.GetEnumerator();
                try
                {
                    if (enumerator.MoveNext())
                        return true;
                }
                finally
                {
                    (enumerator as IDisposable)?.Dispose();
                }

                continue;
            }

            return true;
        }

        return false;
    }

    private static string ReadMapString(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;

            var text = ConvertToString(value);
            if (!string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }

        return string.Empty;
    }

    private static string ConvertToString(object? value)
    {
        if (value is null)
            return string.Empty;

        if (value is string text)
            return text;

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;
            if (element.ValueKind == JsonValueKind.Number)
                return element.GetRawText();
            if (element.ValueKind == JsonValueKind.True)
                return bool.TrueString;
            if (element.ValueKind == JsonValueKind.False)
                return bool.FalseString;
            return string.Empty;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private sealed record StructuredQuestionAnswer(string Question, string Answer);

    private sealed record StructuredHowToStep(string Name, string Text, string Url, string Image);
}
