using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string ResolveMetaDescriptionDefault(SiteSpec spec, ContentItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Description))
            return item.Description.Trim();

        var socialDescription = GetMetaString(item.Meta, "social_description");
        if (!string.IsNullOrWhiteSpace(socialDescription))
            return socialDescription.Trim();

        var snippet = BuildSnippet(item.HtmlContent, 180);
        if (!string.IsNullOrWhiteSpace(snippet))
            return snippet.Trim();

        return string.IsNullOrWhiteSpace(spec.Name)
            ? "Documentation and reference."
            : $"{spec.Name} documentation and reference.";
    }

    private static IEnumerable<string> ResolveRssCategories(ContentItem item)
    {
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in item.Tags ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(tag))
                categories.Add(tag.Trim());
        }

        foreach (var category in item.Categories ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(category))
                categories.Add(category.Trim());
        }

        foreach (var value in GetTaxonomyValues(item, new TaxonomySpec { Name = "categories" }))
        {
            if (!string.IsNullOrWhiteSpace(value))
                categories.Add(value.Trim());
        }

        return categories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }
    private static PowerForge.Web.Protocols.Feeds.SyndicationFeed CreateProtocolFeed(
        SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> items, string format, string suffix)
    {
        var localization = ResolveLocalizationConfig(spec);
        var baseUrl = (ResolveLanguageBaseUrl(spec, localization, item.Language) ?? string.Empty).TrimEnd('/');
        string Absolute(string route) => string.IsNullOrWhiteSpace(baseUrl) ? route : baseUrl + route;
        var route = ResolveOutputRoute(item.OutputPath, new OutputFormatSpec { Name = format, Suffix = suffix });
        var selected = ResolveFeedItems(spec, item, items, spec.Feed?.MaxItems ?? 0).ToArray();
        var now = DateTimeOffset.UtcNow;
        var feed = new PowerForge.Web.Protocols.Feeds.SyndicationFeed
        {
            Title = ResolveFeedChannelTitle(spec, item), Description = ResolveFeedChannelDescription(spec, item),
            Url = Absolute(item.OutputPath), SourceFeedUrl = Absolute(route), Id = Absolute(route),
            Authors = format == "atom" ? new[] { new PowerForge.Web.Protocols.Feeds.FeedAuthor
                { Name = string.IsNullOrWhiteSpace(spec.Name) ? ResolveFeedChannelTitle(spec, item) : spec.Name } }
                : Array.Empty<PowerForge.Web.Protocols.Feeds.FeedAuthor>(),
            Updated = format == "atom" ? selected.Where(entry => entry.Date.HasValue)
                .Select(entry => new DateTimeOffset(entry.Date!.Value.ToUniversalTime())).DefaultIfEmpty(now).Max() : null,
            Items = selected.Select(entry =>
            {
                var names = ResolveContentAuthorNames(entry);
                var urls = ResolveContentAuthorUrls(entry);
                var date = entry.Date.HasValue ? new DateTimeOffset(entry.Date.Value.ToUniversalTime()) : now;
                return new PowerForge.Web.Protocols.Feeds.SyndicationItem
                {
                    Title = entry.Title, Url = Absolute(entry.OutputPath), Id = Absolute(entry.OutputPath), IdIsPermaLink = true,
                    Summary = string.IsNullOrWhiteSpace(entry.Description) ? BuildSnippet(entry.HtmlContent, 200) : entry.Description,
                    Published = format == "atom" ? null : date, Updated = format == "atom" ? date : null,
                    Content = spec.Feed?.IncludeContent == true ? entry.HtmlContent : null, ContentType = "html",
                    Categories = spec.Feed?.IncludeCategories != false ? ResolveRssCategories(entry).ToArray() : Array.Empty<string>(),
                    Authors = names.Select((name, index) => new PowerForge.Web.Protocols.Feeds.FeedAuthor
                    { Name = name, Url = index < urls.Length ? urls[index] : null }).ToArray()
                };
            }).ToArray()
        };
        return feed;
    }
}
