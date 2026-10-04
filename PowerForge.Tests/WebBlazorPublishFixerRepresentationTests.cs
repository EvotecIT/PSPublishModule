using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebBlazorPublishFixerRepresentationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "powerforge-blazor-representations-" + Guid.NewGuid().ToString("N"));

    public WebBlazorPublishFixerRepresentationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void RewrittenHtmlAndCopiedLegacyAssetsKeepCompressedResponsesAndMetadataConsistent()
    {
        var site = Path.Combine(_root, "wwwroot");
        Directory.CreateDirectory(Path.Combine(site, "_framework"));
        File.WriteAllText(Path.Combine(site, "index.html"), "<base href=\"/\"><link rel='stylesheet' href='App.styles.css'><script src=\"_framework/blazor.webassembly.js\"></script>");
        File.WriteAllText(Path.Combine(site, "App.styles.css"), "body{color:purple}");
        File.WriteAllText(Path.Combine(site, "_framework", "blazor.webassembly.hash.js"), "new bootstrap");
        File.WriteAllText(Path.Combine(site, "_framework", "blazor.webassembly.js"), "old bootstrap");
        File.WriteAllText(Path.Combine(site, "_framework", "blazor.boot.json"), "{\"resources\":{\"assembly\":{\"blazor.webassembly.js\":\"sha256-old\"}}}");
        var files = new[] { "index.html", "App.styles.css", "_framework/blazor.webassembly.js", "_framework/blazor.boot.json" };
        var endpoints = new JsonArray();
        foreach (var file in files)
        {
            foreach (var extension in new[] { "", ".br", ".gz" })
            {
                if (extension != "") File.WriteAllText(Path.Combine(site, file + extension), "stale compressed bytes");
                endpoints.Add(Endpoint(file, extension));
            }
        }
        var manifestPath = Path.Combine(_root, "App.staticwebassets.endpoints.json");
        File.WriteAllText(manifestPath, new JsonObject { ["Endpoints"] = endpoints }.ToJsonString());
        WebBlazorPublishFixer.Apply(new() { PublishRoot = _root, BaseHref = "/playground/" });
        Assert.Contains("/playground/", File.ReadAllText(Path.Combine(site, "index.html")));
        var result = JsonNode.Parse(File.ReadAllText(manifestPath))!["Endpoints"]!.AsArray();
        Assert.Contains(result, node => node!["Route"]!.GetValue<string>().Contains("App.styles.pf-", StringComparison.Ordinal));
        Assert.Contains(result, node => node!["Route"]!.GetValue<string>().Contains("blazor.webassembly.pf-", StringComparison.Ordinal));
        foreach (var endpoint in result.OfType<JsonObject>())
        {
            var file = endpoint["AssetFile"]!.GetValue<string>();
            var path = Path.Combine(site, file);
            var compressed = file.EndsWith(".gz", StringComparison.Ordinal) || file.EndsWith(".br", StringComparison.Ordinal);
            var identity = compressed ? path[..^3] : path;
            if (compressed) Assert.Equal(File.ReadAllBytes(identity), Decompress(path));
            Assert.Equal(new FileInfo(path).Length.ToString(), Value(endpoint, "ResponseHeaders", "Content-Length"));
            Assert.Equal("\"" + Hash(path) + "\"", Value(endpoint, "ResponseHeaders", "ETag"));
            Assert.Equal("sha256-" + Hash(identity), Value(endpoint, "EndpointProperties", "integrity"));
        }
    }

    [Fact]
    public void OfflineManifestUsesVersionedStylesheetKeysFinalHashesAndNewCacheVersion()
    {
        File.WriteAllText(Path.Combine(_root, "index.html"), "<link rel='stylesheet' href='App.styles.css'>");
        File.WriteAllText(Path.Combine(_root, "App.styles.css"), "body{color:purple}");
        File.WriteAllText(Path.Combine(_root, "service-worker-assets.js"), "self.assetsManifest = {\"version\":\"old\",\"assets\":[{\"url\":\"index.html\",\"hash\":\"sha256-old\"},{\"url\":\"App.styles.css\",\"hash\":\"sha256-old\"}]};");
        File.WriteAllText(Path.Combine(_root, "service-worker.js"), "/* Manifest version: old */\nself.importScripts('./service-worker-assets.js');");
        File.WriteAllText(Path.Combine(_root, "service-worker-assets.js.gz"), "stale manifest");
        WebBlazorPublishFixer.Apply(new() { PublishRoot = _root });
        var first = ReadOfflineManifest();
        var css = first["assets"]!.AsArray().OfType<JsonObject>().Single(a => a["url"]!.GetValue<string>().EndsWith(".css", StringComparison.Ordinal));
        var cssUrl = css["url"]!.GetValue<string>();
        Assert.Contains(cssUrl, File.ReadAllText(Path.Combine(_root, "index.html")));
        Assert.Contains(".pf-", cssUrl);
        foreach (var asset in first["assets"]!.AsArray().OfType<JsonObject>())
            Assert.Equal("sha256-" + Hash(Path.Combine(_root, asset["url"]!.GetValue<string>())), asset["hash"]!.GetValue<string>());
        Assert.NotEqual("old", first["version"]!.GetValue<string>());
        Assert.Contains(first["version"]!.GetValue<string>(), File.ReadAllText(Path.Combine(_root, "service-worker.js")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(_root, "service-worker-assets.js")), Decompress(Path.Combine(_root, "service-worker-assets.js.gz")));
        WebBlazorPublishFixer.Apply(new() { PublishRoot = _root });
        Assert.Equal(first.ToJsonString(), ReadOfflineManifest().ToJsonString());
        File.WriteAllText(Path.Combine(_root, "App.styles.css"), "body{color:green}");
        WebBlazorPublishFixer.Apply(new() { PublishRoot = _root });
        var upgraded = ReadOfflineManifest();
        Assert.NotEqual(first["version"]!.GetValue<string>(), upgraded["version"]!.GetValue<string>());
        var upgradedCss = upgraded["assets"]!.AsArray().OfType<JsonObject>().Single(a => a["url"]!.GetValue<string>().EndsWith(".css", StringComparison.Ordinal));
        Assert.NotEqual(cssUrl, upgradedCss["url"]!.GetValue<string>());
        Assert.Contains(upgradedCss["url"]!.GetValue<string>(), File.ReadAllText(Path.Combine(_root, "index.html")));
    }

    private JsonNode ReadOfflineManifest() => JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "service-worker-assets.js"))["self.assetsManifest = ".Length..].TrimEnd(';'))!;
    private static string Hash(string path) => Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(path)));
    private static string? Value(JsonObject endpoint, string collection, string name) => endpoint[collection]!.AsArray().OfType<JsonObject>()
        .Single(item => item["Name"]!.GetValue<string>() == name)["Value"]!.GetValue<string>();

    private static JsonObject Endpoint(string file, string extension) => new()
    {
        ["Route"] = file, ["AssetFile"] = file + extension,
        ["Selectors"] = extension == "" ? new JsonArray() : new JsonArray(new JsonObject { ["Name"] = "Content-Encoding", ["Value"] = extension == ".gz" ? "gzip" : "br", ["Quality"] = "0.1" }),
        ["ResponseHeaders"] = new JsonArray(new JsonObject { ["Name"] = "Content-Length", ["Value"] = "1" }, new JsonObject { ["Name"] = "ETag", ["Value"] = "old" }),
        ["EndpointProperties"] = new JsonArray(new JsonObject { ["Name"] = "integrity", ["Value"] = "sha256-old" }, new JsonObject { ["Name"] = "original-resource", ["Value"] = "old" })
    };

    private static byte[] Decompress(string path)
    {
        using var input = File.OpenRead(path);
        using Stream decompressor = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(input, CompressionMode.Decompress) : new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
