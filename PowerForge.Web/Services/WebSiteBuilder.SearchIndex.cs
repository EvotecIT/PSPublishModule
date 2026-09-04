using System.Text;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static readonly string[] SearchDisplayMetaKeys =
    [
        "categories",
        "tags",
        "date",
        "image",
        "pagination_page",
        "pagination_total_items",
        "pagination_previous_url",
        "pagination_next_url",
        "pagination_base_url",
        "term",
        "i18n.fallback_copy",
        "i18n.fallback_source_language",
        "i18n.requested_language"
    ];

    private static readonly string[] SearchableMetaKeys =
    [
        "aliases",
        "keywords",
        "search_keywords",
        "search_terms",
        "term"
    ];

    private static string BuildSearchText(ContentItem item, string snippet)
    {
        var parts = new List<string>();

        foreach (var key in SearchableMetaKeys)
        {
            if (TryGetSearchMetaValue(item.Meta, key, out var value))
                WebSearchIndexPolicy.AppendTextValue(parts, value);
        }

        parts.AddRange(
        [
            item.Title,
            item.Description,
            snippet,
            item.Collection,
            item.Kind.ToString(),
            item.ProjectSlug ?? string.Empty,
            string.Join(" ", item.Tags ?? Array.Empty<string>()),
            string.Join(" ", item.Categories ?? Array.Empty<string>())
        ]);

        var combined = string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();
        return WebSearchIndexPolicy.Truncate(combined, WebSearchIndexPolicy.MaximumSearchTextCharacters);
    }

    private static Dictionary<string, object?>? BuildSearchMeta(ContentItem item)
    {
        if (item.Meta.Count == 0)
            return null;

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in SearchDisplayMetaKeys)
        {
            if (TryGetSearchMetaValue(item.Meta, key, out var value))
                result[key] = value;
        }
        return result.Count == 0 ? null : result;
    }

    private static bool TryGetSearchMetaValue(
        IReadOnlyDictionary<string, object?> values,
        string key,
        out object? value)
    {
        if (values.TryGetValue(key, out value))
            return true;

        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static SearchIndexArtifactInfo WriteSearchIndexArtifact(
        string outputRoot,
        string path,
        IReadOnlyCollection<SearchIndexEntry> entries)
    {
        var bounded = WebSearchIndexPolicy.CreateBoundedJson(entries);
        WriteAllTextIfChanged(path, bounded.Json);
        return new SearchIndexArtifactInfo(
            "/" + Path.GetRelativePath(outputRoot, path).Replace('\\', '/'),
            bounded.SourceCount,
            bounded.Count,
            Encoding.UTF8.GetByteCount(bounded.Json),
            WebSearchIndexPolicy.ComputeSha256(bounded.Json),
            bounded.Truncated);
    }

    private static Dictionary<string, object?> ToSearchManifestArtifact(
        SearchIndexArtifactInfo artifact,
        string? discriminatorName = null,
        string? discriminatorValue = null)
    {
        var result = new Dictionary<string, object?>
        {
            ["path"] = artifact.Path,
            ["sourceCount"] = artifact.SourceCount,
            ["count"] = artifact.Count,
            ["bytes"] = artifact.Bytes,
            ["sha256"] = artifact.Sha256,
            ["truncated"] = artifact.Truncated
        };
        if (!string.IsNullOrWhiteSpace(discriminatorName))
            result[discriminatorName] = discriminatorValue;
        return result;
    }

    private static CollectionSearchShardSpec[] AllocateCollectionSearchShardSpecs(IEnumerable<string> collections)
    {
        var candidates = collections
            .Select(static collection => new CollectionSearchShardSpec(collection, Slugify(collection)))
            .Where(static shard => !string.IsNullOrWhiteSpace(shard.Token))
            .OrderBy(static shard => shard.Token, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static shard => shard.Collection, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var usedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < candidates.Length; index++)
        {
            var shard = candidates[index];
            if (usedTokens.Add(shard.Token))
                continue;

            var baseToken = shard.Token;
            var digest = WebSearchIndexPolicy.ComputeSha256(shard.Collection.ToUpperInvariant())[..12];
            var suffix = 0;
            string token;
            do
            {
                token = suffix == 0
                    ? $"{baseToken}-{digest}"
                    : $"{baseToken}-{digest}-{suffix}";
                suffix++;
            }
            while (!usedTokens.Add(token));

            candidates[index] = shard with { Token = token };
        }

        return candidates;
    }
}
