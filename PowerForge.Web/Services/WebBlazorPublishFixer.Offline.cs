using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge.Web;

public static partial class WebBlazorPublishFixer
{
    private static void UpdateOfflineAssets(string siteRoot, Dictionary<string, string> aliases, HashSet<string> affected)
    {
        var manifestPath = Path.Combine(siteRoot, "service-worker-assets.js");
        if (!File.Exists(manifestPath)) return;
        var content = File.ReadAllText(manifestPath);
        var assignment = Regex.Match(content, @"\A(?<prefix>\s*self\.assetsManifest\s*=\s*)(?<json>\{[\s\S]*\})(?<suffix>;?\s*)\z");
        if (!assignment.Success) return; // Custom worker manifests remain owned by their publisher.
        var manifest = JsonNode.Parse(assignment.Groups["json"].Value) as JsonObject;
        if (manifest?["assets"] is not JsonArray assets) return;
        foreach (var asset in assets.OfType<JsonObject>())
        {
            var url = asset["url"]?.GetValue<string>();
            if (url is null) continue;
            var original = Regex.Replace(url, @"\.pf-[a-f0-9]{64}(?=\.(?:css|js)$)", "", RegexOptions.IgnoreCase);
            if (aliases.TryGetValue(original, out var alias)) asset["url"] = url = alias;
            var path = ResolveLocalAsset(siteRoot, url);
            if (path is not null && File.Exists(path) && affected.Contains(url))
                asset["hash"] = "sha256-" + ComputeHash(path);
        }
        // The worker's cache name must change with the final request keys and integrity hashes.
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assets.ToJsonString()))).ToLowerInvariant();
        manifest["version"] = version;
        WriteChangedBytes(manifestPath, Encoding.UTF8.GetBytes(assignment.Groups["prefix"].Value
            + manifest.ToJsonString(new() { WriteIndented = true }) + assignment.Groups["suffix"].Value));
        affected.Add("service-worker-assets.js");

        var workerPath = Path.Combine(siteRoot, "service-worker.js");
        if (!File.Exists(workerPath)) return;
        var worker = File.ReadAllText(workerPath);
        var updated = Regex.Replace(worker, @"\A/\* Manifest version: [^\r\n]*? \*/", "/* Manifest version: " + version + " */");
        if (worker != updated) WriteChangedBytes(workerPath, Encoding.UTF8.GetBytes(updated));
        affected.Add("service-worker.js");
    }
}
