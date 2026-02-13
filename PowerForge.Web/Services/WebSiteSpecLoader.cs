using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Web;

/// <summary>Loads site and project specifications from disk.</summary>
public static class WebSiteSpecLoader
{
    /// <summary>Loads a site spec and returns the resolved path.</summary>
    /// <param name="configPath">Path to site.json.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>Deserialized spec and full path.</returns>
    public static (SiteSpec Spec, string FullPath) LoadWithPath(string configPath, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Config path is required.", nameof(configPath));

        var fullPath = Path.GetFullPath(configPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Config file not found: {fullPath}");

        var merged = LoadJsonWithExtends(fullPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var opts = options ?? WebJson.Options;
        var spec = merged.Deserialize<SiteSpec>(opts);
        if (spec is null)
            throw new InvalidOperationException($"Failed to deserialize site config: {fullPath}");

        ApplyVersioningHub(spec, fullPath, opts);

        return (spec, fullPath);
    }

    /// <summary>Loads a project spec and returns the resolved path.</summary>
    /// <param name="projectPath">Path to project.json.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>Deserialized spec and full path.</returns>
    public static (ProjectSpec Spec, string FullPath) LoadProjectWithPath(string projectPath, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("Project path is required.", nameof(projectPath));

        var fullPath = Path.GetFullPath(projectPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Project file not found: {fullPath}");

        var json = File.ReadAllText(fullPath);
        var opts = options ?? WebJson.Options;
        var spec = JsonSerializer.Deserialize<ProjectSpec>(json, opts);
        if (spec is null)
            throw new InvalidOperationException($"Failed to deserialize project config: {fullPath}");

        return (spec, fullPath);
    }

    private static JsonObject LoadJsonWithExtends(string configPath, HashSet<string> chain)
    {
        var fullPath = Path.GetFullPath(configPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Config file not found: {fullPath}");

        if (!chain.Add(fullPath))
            throw new InvalidOperationException($"Site spec inheritance loop detected at {fullPath}");

        var json = File.ReadAllText(fullPath);
        var documentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };
        var node = JsonNode.Parse(json, null, documentOptions) as JsonObject;
        if (node is null)
            throw new InvalidOperationException($"Failed to parse site config: {fullPath}");

        var basePaths = ReadExtends(node);
        RemoveExtends(node);

        var merged = new JsonObject();
        var baseDir = Path.GetDirectoryName(fullPath) ?? ".";
        foreach (var basePath in basePaths)
        {
            var resolved = Path.IsPathRooted(basePath) ? basePath : Path.Combine(baseDir, basePath);
            var baseNode = LoadJsonWithExtends(resolved, chain);
            merged = MergeObjects(merged, baseNode);
        }

        merged = MergeObjects(merged, node);
        chain.Remove(fullPath);
        return merged;
    }

    private static List<string> ReadExtends(JsonObject node)
    {
        var paths = new List<string>();
        if (TryGetProperty(node, "extends", out var value) || TryGetProperty(node, "Extends", out value))
        {
            if (value is JsonValue jsonValue)
            {
                var text = jsonValue.TryGetValue<string>(out var single) ? single : jsonValue.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    paths.Add(text);
            }
            else if (value is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is null) continue;
                    var text = item is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : item.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        paths.Add(text);
                }
            }
        }
        return paths;
    }

    private static void RemoveExtends(JsonObject node)
    {
        var keys = node.Select(k => k.Key).ToList();
        foreach (var key in keys)
        {
            if (string.Equals(key, "extends", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "Extends", StringComparison.OrdinalIgnoreCase))
            {
                node.Remove(key);
            }
        }
    }

    private static bool TryGetProperty(JsonObject node, string name, out JsonNode? value)
    {
        foreach (var kvp in node)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static JsonObject MergeObjects(JsonObject baseNode, JsonObject overlay)
    {
        var merged = new JsonObject();
        foreach (var kvp in baseNode)
        {
            merged[kvp.Key] = kvp.Value?.DeepClone();
        }

        foreach (var kvp in overlay)
        {
            var key = FindExistingKey(merged, kvp.Key) ?? kvp.Key;
            var incoming = kvp.Value;
            if (merged.TryGetPropertyValue(key, out var existing) &&
                existing is JsonObject existingObj &&
                incoming is JsonObject incomingObj)
            {
                merged[key] = MergeObjects(existingObj, incomingObj);
            }
            else
            {
                merged[key] = incoming?.DeepClone();
            }
        }

        return merged;
    }

    private static string? FindExistingKey(JsonObject obj, string key)
    {
        foreach (var existing in obj)
        {
            if (string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase))
                return existing.Key;
        }
        return null;
    }

    private static void ApplyVersioningHub(SiteSpec spec, string configPath, JsonSerializerOptions options)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var versioning = spec.Versioning;
        if (versioning is null || string.IsNullOrWhiteSpace(versioning.HubPath))
            return;

        if (versioning.Versions is { Length: > 0 })
            return;

        var baseDir = Path.GetDirectoryName(configPath) ?? ".";
        var resolvedHubPath = versioning.HubPath.Trim();
        if (!Path.IsPathRooted(resolvedHubPath))
            resolvedHubPath = Path.Combine(baseDir, resolvedHubPath);
        resolvedHubPath = Path.GetFullPath(resolvedHubPath);

        if (!File.Exists(resolvedHubPath))
            throw new FileNotFoundException($"Versioning hub file not found: {resolvedHubPath}");

        var json = File.ReadAllText(resolvedHubPath);
        var hub = JsonSerializer.Deserialize<WebVersionHubDocument>(json, options);
        if (hub is null)
            throw new InvalidOperationException($"Failed to deserialize versioning hub: {resolvedHubPath}");

        var loadedVersions = new List<VersionSpec>();
        if (hub.Versions is not null)
        {
            foreach (var entry in hub.Versions)
            {
                if (entry is null)
                    continue;

                var name = !string.IsNullOrWhiteSpace(entry.Id) ? entry.Id.Trim() : (entry.Version ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                loadedVersions.Add(new VersionSpec
                {
                    Name = name,
                    Label = string.IsNullOrWhiteSpace(entry.Label) ? name : entry.Label.Trim(),
                    Url = string.IsNullOrWhiteSpace(entry.Path) ? null : entry.Path.Trim(),
                    Latest = entry.Latest,
                    Lts = entry.Lts,
                    Deprecated = entry.Deprecated
                });
            }
        }

        versioning.Versions = loadedVersions.ToArray();
        if (versioning.Versions.Length == 0)
            return;

        if (string.IsNullOrWhiteSpace(versioning.Current))
        {
            versioning.Current = versioning.Versions
                .FirstOrDefault(static v => v.Latest)?.Name
                ?? versioning.Versions[0].Name;
        }
    }
}
