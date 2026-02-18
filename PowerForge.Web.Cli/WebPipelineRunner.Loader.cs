using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static JsonDocument LoadPipelineDocumentWithExtends(string pipelinePath)
    {
        if (string.IsNullOrWhiteSpace(pipelinePath))
            throw new ArgumentException("Pipeline path is required.", nameof(pipelinePath));

        var merged = LoadPipelineJsonWithExtends(pipelinePath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var json = merged.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });

        return JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
    }

    private static JsonObject LoadPipelineJsonWithExtends(string pipelinePath, HashSet<string> chain)
    {
        var fullPath = Path.GetFullPath(pipelinePath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Pipeline config not found: {fullPath}");

        if (!chain.Add(fullPath))
            throw new InvalidOperationException($"Pipeline config inheritance loop detected at {fullPath}");

        var node = ParsePipelineConfigNode(fullPath);
        var basePaths = ReadPipelineExtends(node);
        RemovePipelineExtends(node);

        var merged = new JsonObject();
        var baseDir = Path.GetDirectoryName(fullPath) ?? ".";
        foreach (var basePath in basePaths)
        {
            var resolved = Path.IsPathRooted(basePath) ? basePath : Path.Combine(baseDir, basePath);
            var baseNode = LoadPipelineJsonWithExtends(resolved, chain);
            merged = MergePipelineObjects(merged, baseNode);
        }

        merged = MergePipelineObjects(merged, node);
        chain.Remove(fullPath);
        return merged;
    }

    private static JsonObject ParsePipelineConfigNode(string fullPath)
    {
        var json = File.ReadAllText(fullPath);
        var node = JsonNode.Parse(json, null, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        }) as JsonObject;
        if (node is null)
            throw new InvalidOperationException($"Failed to parse pipeline config: {fullPath}");
        return node;
    }

    private static List<string> ReadPipelineExtends(JsonObject node)
    {
        var paths = new List<string>();
        if (!TryGetPipelineProperty(node, "extends", out var value) &&
            !TryGetPipelineProperty(node, "Extends", out value))
        {
            return paths;
        }

        if (value is JsonValue scalar)
        {
            var text = scalar.TryGetValue<string>(out var single) ? single : scalar.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                paths.Add(text.Trim());
            return paths;
        }

        if (value is not JsonArray array)
            return paths;

        foreach (var item in array)
        {
            if (item is null)
                continue;
            var text = item is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var parsed)
                ? parsed
                : item.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                paths.Add(text.Trim());
        }

        return paths;
    }

    private static void RemovePipelineExtends(JsonObject node)
    {
        var keys = node.Select(kvp => kvp.Key).ToArray();
        foreach (var key in keys)
        {
            if (string.Equals(key, "extends", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "Extends", StringComparison.OrdinalIgnoreCase))
            {
                node.Remove(key);
            }
        }
    }

    private static bool TryGetPipelineProperty(JsonObject node, string name, out JsonNode? value)
    {
        foreach (var kvp in node)
        {
            if (!string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                continue;
            value = kvp.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static JsonObject MergePipelineObjects(JsonObject baseNode, JsonObject overlay)
    {
        var merged = new JsonObject();
        foreach (var kvp in baseNode)
            merged[kvp.Key] = kvp.Value?.DeepClone();

        foreach (var kvp in overlay)
        {
            var key = FindPipelineExistingKey(merged, kvp.Key) ?? kvp.Key;
            var incoming = kvp.Value;
            if (merged.TryGetPropertyValue(key, out var existing))
            {
                if (existing is JsonObject existingObject &&
                    incoming is JsonObject incomingObject)
                {
                    merged[key] = MergePipelineObjects(existingObject, incomingObject);
                    continue;
                }

                if (string.Equals(key, "steps", StringComparison.OrdinalIgnoreCase) &&
                    existing is JsonArray existingArray &&
                    incoming is JsonArray incomingArray)
                {
                    var combined = new JsonArray();
                    foreach (var item in existingArray)
                        combined.Add(item?.DeepClone());
                    foreach (var item in incomingArray)
                        combined.Add(item?.DeepClone());
                    merged[key] = combined;
                    continue;
                }
            }

            merged[key] = incoming?.DeepClone();
        }

        return merged;
    }

    private static string? FindPipelineExistingKey(JsonObject node, string key)
    {
        foreach (var existing in node)
        {
            if (string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase))
                return existing.Key;
        }

        return null;
    }
}
