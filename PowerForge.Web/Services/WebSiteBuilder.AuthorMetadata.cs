namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string[] ResolveContentAuthorNames(ContentItem item)
    {
        if (item?.Meta is null)
            return Array.Empty<string>();

        var names = ReadMetaStringList(item.Meta, "author_names", "authors.names", "authors_names", "schema.author_names");
        if (names.Length > 0)
            return names;

        var single = ReadMetaString(item.Meta, "author.name", "article.author", "news.author", "schema.author", "author");
        return string.IsNullOrWhiteSpace(single)
            ? Array.Empty<string>()
            : new[] { single.Trim() };
    }

    private static string[] ResolveContentAuthorUrls(ContentItem item)
    {
        if (item?.Meta is null)
            return Array.Empty<string>();

        return ReadMetaStringList(item.Meta, "author_urls", "authors.urls", "authors_urls", "schema.author_urls");
    }

    private static object? BuildContentAuthorStructuredData(ContentItem item, string fallbackName)
    {
        var names = ResolveContentAuthorNames(item);
        if (names.Length == 0)
        {
            return new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = fallbackName
            };
        }

        var urls = ResolveContentAuthorUrls(item);
        var authors = names.Select((name, index) =>
        {
            var model = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = name
            };
            if (index < urls.Length && !string.IsNullOrWhiteSpace(urls[index]))
                model["url"] = urls[index];
            return model;
        }).ToArray();

        return authors.Length == 1 ? authors[0] : authors;
    }
}
