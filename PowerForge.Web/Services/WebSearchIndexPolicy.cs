using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Shared generation and consumption limits for public website search indexes.</summary>
internal static class WebSearchIndexPolicy
{
    internal const int MaximumDecodedBytes = 8 * 1024 * 1024;
    internal const int MaximumEntries = 5_000;
    internal const int MaximumSearchTextCharacters = 4_096;
    internal const int CompressionRecommendationBytes = 64 * 1024;

    internal static bool TryValidateJsonArray(
        string? json,
        out int entryCount,
        out int decodedBytes,
        out string message)
    {
        entryCount = 0;
        decodedBytes = string.IsNullOrEmpty(json) ? 0 : Encoding.UTF8.GetByteCount(json);
        if (string.IsNullOrWhiteSpace(json))
        {
            message = "the search index is empty.";
            return false;
        }
        if (decodedBytes > MaximumDecodedBytes)
        {
            message = $"the decoded search index is {decodedBytes} bytes; the WebMCP limit is {MaximumDecodedBytes} bytes.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                message = "the search index root must be a JSON array.";
                return false;
            }

            entryCount = document.RootElement.GetArrayLength();
            if (entryCount > MaximumEntries)
            {
                message = $"the search index contains {entryCount} entries; the WebMCP limit is {MaximumEntries}.";
                return false;
            }

            message = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            message = $"the search index is not valid JSON ({ex.GetType().Name}).";
            return false;
        }
    }

    internal static string ComputeSha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    internal static BoundedSearchIndexJson CreateBoundedJson(IReadOnlyCollection<SearchIndexEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var serializedEntries = new List<string>(Math.Min(entries.Count, MaximumEntries));
        var decodedBytes = 2; // JSON array brackets.
        foreach (var entry in entries)
        {
            if (serializedEntries.Count >= MaximumEntries)
                break;

            var entryJson = JsonSerializer.Serialize(entry, WebJson.Options);
            var projectedBytes = decodedBytes
                                 + (serializedEntries.Count == 0 ? 0 : 1)
                                 + Encoding.UTF8.GetByteCount(entryJson);
            if (projectedBytes > MaximumDecodedBytes)
                continue;

            serializedEntries.Add(entryJson);
            decodedBytes = projectedBytes;
        }

        var json = $"[{string.Join(',', serializedEntries)}]";
        return new BoundedSearchIndexJson(
            json,
            entries.Count,
            serializedEntries.Count,
            serializedEntries.Count != entries.Count);
    }

    internal static void AppendTextValue(List<string> parts, object? value, int depth = 0)
    {
        if (value is null || depth > 3)
            return;

        switch (value)
        {
            case string text when !string.IsNullOrWhiteSpace(text):
                parts.Add(text);
                return;
            case JsonElement element:
                AppendJsonElement(parts, element, depth);
                return;
            case IEnumerable values when value is not string:
                foreach (var item in values)
                {
                    AppendTextValue(parts, item, depth + 1);
                    if (parts.Sum(static part => part.Length) >= MaximumSearchTextCharacters)
                        break;
                }
                return;
        }
    }

    internal static string Truncate(string? value, int maximumCharacters)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= maximumCharacters ? text : text[..maximumCharacters];
    }

    private static void AppendJsonElement(List<string> parts, JsonElement element, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AppendTextValue(parts, element.GetString(), depth + 1);
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                    AppendTextValue(parts, child, depth + 1);
                break;
        }
    }
}

internal sealed record BoundedSearchIndexJson(string Json, int SourceCount, int Count, bool Truncated);
internal sealed record SearchIndexArtifactInfo(
    string Path,
    int SourceCount,
    int Count,
    int Bytes,
    string Sha256,
    bool Truncated);
internal sealed record CollectionSearchShardSpec(string Collection, string Token);
