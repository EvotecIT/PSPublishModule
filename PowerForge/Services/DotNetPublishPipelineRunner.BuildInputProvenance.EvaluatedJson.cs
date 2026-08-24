using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryResolveEvaluatedItemPath(
        JsonElement item,
        string metadataName,
        string baseDirectory,
        out string? fullPath)
    {
        fullPath = null;
        string? value = ReadItemText(item, metadataName);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            fullPath = Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(baseDirectory, value));
            return File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsOutputRelevantNoneItem(JsonElement item)
        => HasRelevantMetadata(item, "CopyToOutputDirectory")
           || HasRelevantMetadata(item, "CopyToPublishDirectory")
           || (item.TryGetProperty("Pack", out JsonElement pack) &&
               pack.ValueKind == JsonValueKind.String &&
               bool.TryParse(pack.GetString(), out bool packs) && packs);

    private static bool HasRelevantMetadata(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
           && !value.GetString()!.Equals("Never", StringComparison.OrdinalIgnoreCase);

    private static string? ReadItemText(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void AddSemicolonSeparatedPathValues(
        JsonElement properties,
        string name,
        string baseDirectory,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return;
        foreach (string value in (property.GetString() ?? string.Empty).Split(
                     new[] { ';' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string fullPath = Path.GetFullPath(
                Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value));
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
                values.Add(fullPath);
        }
    }

    private static void AddSemicolonSeparatedValues(
        JsonElement properties,
        string name,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return;
        foreach (string value in (property.GetString() ?? string.Empty).Split(
                     new[] { ';' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            values.Add(value.Trim());
        }
    }
}
