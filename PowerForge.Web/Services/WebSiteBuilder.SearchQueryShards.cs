using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static List<Dictionary<string, object?>> WriteSearchQueryShards(string outputRoot, IReadOnlyList<SearchIndexEntry> entries)
    {
        var directory = Path.Combine(outputRoot, "search", "query");
        Directory.CreateDirectory(directory);
        var descriptors = new List<Dictionary<string, object?>>();
        // Package grouping lets the UI narrow discovery without loading other packages.
        foreach (var group in entries.GroupBy(entry => entry.Project ?? string.Empty).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            foreach (var chunk in group.OrderBy(entry => entry.Title, StringComparer.Ordinal)
                         .ThenBy(entry => entry.Url, StringComparer.Ordinal).Chunk(100))
            {
                var bounded = WebSearchIndexPolicy.CreateBoundedJson(chunk);
                var path = Path.Combine(directory, WebSearchIndexPolicy.ComputeSha256(bounded.Json) + ".json");
                var artifact = WriteSearchIndexArtifact(outputRoot, path, chunk);
                if (artifact.Truncated)
                    throw new InvalidDataException("A query search shard exceeds the bounded search artifact limits.");
                var descriptor = ToSearchManifestArtifact(artifact, "project", group.Key);
                var prefixes = chunk.SelectMany(entry => SearchQueryPrefixes(string.Join(' ',
                    entry.Title, string.Join(' ', entry.Aliases), entry.SearchText, entry.Description, entry.Snippet,
                    entry.Collection, entry.Kind, string.Join(' ', entry.Tags), string.Join(' ', entry.Categories))))
                    .Distinct(StringComparer.Ordinal).ToArray();
                descriptor["prefixBloom"] = CreateSearchPrefixBloom(prefixes);
                descriptors.Add(descriptor);
            }
        }
        return descriptors;
    }

    private static IEnumerable<string> SearchQueryPrefixes(string text)
    {
        // Match both contiguous identifiers and their human-readable camel-case words.
        text = (text + " " + Regex.Replace(text, "([a-z])([A-Z])", "$1 $2")).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        text = string.Concat(text.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
        foreach (Match match in Regex.Matches(text, @"[\p{L}\p{N}]+"))
        {
            for (var offset = 0; offset < match.Length; offset++)
                for (var length = 1; length <= Math.Min(3, match.Length - offset); length++)
                    yield return match.Value.Substring(offset, length);
        }
    }

    // Three fixed FNV-1a seeds are shared with site-search.v1.js. Bloom hints may
    // fetch an extra shard; they must never discard a shard containing a match.
    private static string CreateSearchPrefixBloom(string[] prefixes)
    {
        var bytes = new byte[Math.Max(32, (prefixes.Length * 12 + 7) / 8)];
        foreach (var prefix in prefixes)
            foreach (var seed in new uint[] { 2166136261, 3339675911, 1099511627 })
            {
                var hash = seed;
                foreach (var character in prefix)
                    hash = unchecked((hash ^ character) * 16777619);
                var bit = (int)(hash % (uint)(bytes.Length * 8));
                bytes[bit / 8] |= (byte)(1 << (bit % 8));
            }
        return Convert.ToBase64String(bytes);
    }

    private static void RetireSearchQueryShards(string outputRoot, IReadOnlyList<Dictionary<string, object?>> descriptors)
    {
        var directory = Path.Combine(outputRoot, "search", "query");
        if (!Directory.Exists(directory) ||
            (File.GetAttributes(Path.GetDirectoryName(directory)!) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
        var retained = descriptors.Select(descriptor => Path.GetFileName((string)descriptor["path"]!)).ToHashSet(StringComparer.Ordinal);
        // Only remove files owned by this generator, after the new manifest was written.
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (!retained.Contains(name) && Regex.IsMatch(name, "^[a-f0-9]{64}\\.json$") &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                File.Delete(path);
        }
    }
}
