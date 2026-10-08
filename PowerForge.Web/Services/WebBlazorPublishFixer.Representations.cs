using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace PowerForge.Web;

public static partial class WebBlazorPublishFixer
{
    private static void WriteChangedBytes(string path, byte[] bytes)
    {
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
    }

    private static void RefreshCompressedAssets(string siteRoot, string relative, Dictionary<string, string> aliases)
    {
        var path = ResolveLocalAsset(siteRoot, relative);
        if (path is null || !File.Exists(path)) return;
        var sourceRelative = aliases.FirstOrDefault(pair => pair.Value == relative).Key ?? relative;
        var sourcePath = ResolveLocalAsset(siteRoot, sourceRelative)!;
        var bytes = File.ReadAllBytes(path);
        foreach (var extension in new[] { ".gz", ".br" })
        {
            if (!File.Exists(path + extension) && !File.Exists(sourcePath + extension)) continue;
            using var output = new MemoryStream();
            using (Stream compressor = extension == ".gz"
                ? new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)
                : new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true)) compressor.Write(bytes);
            WriteChangedBytes(path + extension, output.ToArray());
        }
    }

    private static void UpdateEndpointManifests(string root, string siteRoot, Dictionary<string, string> aliases, HashSet<string> affected)
    {
        var manifests = Directory.EnumerateFiles(root, "*.staticwebassets.endpoints.json")
            .Concat(root == siteRoot ? Array.Empty<string>() : Directory.EnumerateFiles(siteRoot, "*.staticwebassets.endpoints.json"));
        foreach (var manifestPath in manifests)
        {
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
            if (manifest?["Endpoints"] is not JsonArray endpoints) continue;
            // Retain existing routes and add corresponding SDK endpoints for the content-versioned copies.
            foreach (var endpoint in endpoints.OfType<JsonObject>().ToArray())
            {
                var route = endpoint["Route"]?.GetValue<string>();
                var file = endpoint["AssetFile"]?.GetValue<string>();
                if (route is null || file is null || !aliases.TryGetValue(route, out var alias)) continue;
                var aliasFile = alias + (file.EndsWith(".br", StringComparison.Ordinal) ? ".br"
                    : file.EndsWith(".gz", StringComparison.Ordinal) ? ".gz" : "");
                if (endpoints.OfType<JsonObject>().Any(e => e["Route"]?.GetValue<string>() == alias
                    && e["AssetFile"]?.GetValue<string>() == aliasFile)) continue;
                var copy = (JsonObject)endpoint.DeepClone();
                copy["Route"] = alias;
                copy["AssetFile"] = aliasFile;
                endpoints.Add(copy);
            }
            foreach (var endpoint in endpoints.OfType<JsonObject>()) UpdateEndpoint(endpoint, siteRoot, affected);
            WriteChangedBytes(manifestPath, Encoding.UTF8.GetBytes(manifest.ToJsonString(new() { WriteIndented = true })));
        }
    }

    private static void UpdateEndpoint(JsonObject endpoint, string siteRoot, HashSet<string> affected)
    {
        var relative = endpoint["AssetFile"]?.GetValue<string>();
        if (relative is null) return;
        var identity = relative.EndsWith(".gz", StringComparison.Ordinal) || relative.EndsWith(".br", StringComparison.Ordinal)
            ? relative[..^3] : relative;
        if (!affected.Contains(identity)) return;
        var path = ResolveLocalAsset(siteRoot, relative);
        var identityPath = ResolveLocalAsset(siteRoot, identity);
        if (path is null || identityPath is null || !File.Exists(path) || !File.Exists(identityPath)) return;
        var length = new FileInfo(path).Length;
        var hash = ComputeHash(path);
        var identityHash = ComputeHash(identityPath);
        SetMetadata(endpoint, "ResponseHeaders", "Content-Length", length.ToString(CultureInfo.InvariantCulture));
        SetMetadata(endpoint, "ResponseHeaders", "ETag", "\"" + hash + "\"");
        SetMetadata(endpoint, "ResponseHeaders", "Last-Modified", File.GetLastWriteTimeUtc(path).ToString("R", CultureInfo.InvariantCulture));
        SetMetadata(endpoint, "EndpointProperties", "integrity", "sha256-" + identityHash);
        if (relative != identity)
        {
            SetMetadata(endpoint, "EndpointProperties", "original-resource", "\"" + identityHash + "\"");
            if (endpoint["Selectors"] is JsonArray selectors)
                foreach (var selector in selectors.OfType<JsonObject>())
                    if (selector["Name"]?.GetValue<string>() == "Content-Encoding")
                        selector["Quality"] = (length == 0 ? 0 : 1d / length).ToString("G", CultureInfo.InvariantCulture);
        }
    }

    private static void SetMetadata(JsonObject endpoint, string collection, string name, string value)
    {
        if (endpoint[collection] is not JsonArray metadata) return;
        var entry = metadata.OfType<JsonObject>().FirstOrDefault(item => item["Name"]?.GetValue<string>() == name);
        if (entry is not null) entry["Value"] = value;
    }
}
