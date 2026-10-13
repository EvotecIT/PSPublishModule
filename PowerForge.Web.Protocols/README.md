# PowerForge.Web.Protocols

Read and write web interchange documents without loading a website generator or browser engine. The package supports .NET 8 and .NET Framework 4.7.2 and has no external runtime package dependencies. It uses the platform XML and JSON serializers; Framework uses its built-in `System.Web.Extensions` serializer.

The library contains:

- IndexNow submission, batching, bounded retries, cancellation, and response diagnostics.
- Sitemap URL-set and index readers, XML creation, count/UTF-8-size partitioning, and bundled offline schemas.
- RSS and Atom readers and writers, and a JSON Feed 1.1 writer.
- Safe JSON-LD serialization for publisher-supplied dictionaries and graphs.

## Ownership

| Owner | Responsibility |
| --- | --- |
| PowerForge.Web.Protocols | Format parsing, serialization, protocol limits, and IndexNow transport |
| PowerForge.Web | Site content selection, canonical URLs, publication dates, output paths, and generated-file cleanup |
| HtmlTinkerX | HTML and browser extraction; existing feed/sitemap APIs delegate to this package |
| Ingestion applications | Acquisition, polling, crawl budgets, provenance, deduplication, storage, and discovery-item mapping |

Readers do not fetch linked resources. XML readers prohibit DTDs and external entity resolution and bound document characters to 64 MiB. Caller acquisition limits should also bound downloaded bytes. Feed content remains untrusted markup; callers decide how to render or sanitize it. Sitemap modification dates remain distinct from article publication dates.

## Read a feed or sitemap

```csharp
using PowerForge.Web.Protocols.Feeds;
using PowerForge.Web.Protocols.Sitemaps;

var feed = SyndicationReader.ParseSyndicationFeed(xml, new Uri(feedUrl), feedUrl);
foreach (var item in feed.Items)
    Console.WriteLine($"{item.Id}: {item.Title} ({item.Published})");

var sitemap = SitemapReader.Read(sitemapXml, new Uri(sitemapUrl));
// The application decides whether to traverse an index and which pages to fetch.
```

Feed models normalize supported metadata, including identifiers, separate publication/update dates, authors, categories, and content. Unknown foreign-namespace extension elements are retained. This is not a lossless XML editor: original prefixes, whitespace, and every optional format construct are not preserved. RSS IDs marked `isPermaLink="false"` are not used as fallback article URLs. Feed and item source URLs use the caller-supplied source first, then a resolved Atom self link when available.

## Write publisher-selected entries

```csharp
var output = SyndicationWriter.WriteRss(new SyndicationFeed
{
    Title = "News",
    Url = "https://example.org/news/",
    SourceFeedUrl = "https://example.org/news/rss.xml",
    Description = "Published updates",
    Items = new[]
    {
        new SyndicationItem
        {
            Id = "update-42",
            Url = "https://example.org/news/update-42/",
            Title = "A published update",
            Summary = "The publisher's summary.",
            Published = publishedAt
        }
    }
});
```

Atom writers require explicit feed/entry IDs, update dates, and a feed author or authors on every entry. JSON Feed requires entry IDs and supplies `content_text` from the summary when a full body is absent. The library does not invent publication dates, authors, site identities, or ordering.

Inline Atom XML media content retains its element structure and namespaces. XML and XHTML bodies are parsed with the same DTD prohibition and size bounds as feed input; text, escaped HTML and binary Base64 bodies remain text. Remote content is not fetched.

JSON Feed marks known HTML/XHTML content as `content_html`. Other explicit media types are preserved as literal `content_text`, including serialized XML and Base64; the writer does not decode binary bodies. An unspecified content type retains the HTML default.

`SitemapWriter.Create` writes an explicit URL set or index. `Partition` accepts URL XML elements, preserving extension elements, and returns documents bounded by the 50,000-entry and 50 MiB limits. File names, index URLs, ownership markers, and obsolete-file removal remain caller decisions. The bundled sitemaps.org schemas validate the base protocol; extensions require their own validation.

## Embed structured data

```csharp
using PowerForge.Web.Protocols.StructuredData;

var script = JsonLdWriter.WriteScript(new Dictionary<string, object?>
{
    ["@context"] = "https://schema.org",
    ["@type"] = "Article",
    ["headline"] = title,
    ["datePublished"] = publishedAt.ToString("O")
});
```

Use JSON-compatible dictionaries, arrays, strings, numbers, booleans and null. Supply dates as ISO strings. Script-sensitive characters are escaped. This API does not load remote contexts, expand or compact JSON-LD, reason about RDF, choose Schema.org entities, or promise search-engine eligibility.

## IndexNow

Reference `PowerForge.Web.Protocols` and import `PowerForge.Web.Protocols.IndexNow`. Existing consumers of the retired `PowerForge.IndexNow` package must update their package reference and namespace, then rebuild. Transport uses an optional caller-owned `HttpClient`; durable queues, checkpoints and operational retry scheduling belong to the application.
