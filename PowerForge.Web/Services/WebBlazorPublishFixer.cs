using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Options for fixing Blazor publish output.</summary>
public sealed class WebBlazorPublishFixOptions
{
    /// <summary>Publish output root path.</summary>
    public string PublishRoot { get; set; } = string.Empty;
    /// <summary>Optional base href override.</summary>
    public string? BaseHref { get; set; }
    /// <summary>When true, update blazor.boot.json integrity hashes.</summary>
    public bool UpdateBootIntegrity { get; set; } = true;
    /// <summary>When true, copy fingerprinted blazor.webassembly.*.js to stable name.</summary>
    public bool CopyFingerprintBlazorJs { get; set; } = true;
    /// <summary>When true, reference content-versioned copies of stable Blazor JS and local stylesheets.</summary>
    public bool AddCacheBuster { get; set; } = true;
}

/// <summary>Applies fixes to Blazor static publish output.</summary>
public static partial class WebBlazorPublishFixer
{
    /// <summary>Applies configured fixes to the publish output.</summary>
    /// <param name="options">Fix options.</param>
    public static void Apply(WebBlazorPublishFixOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.PublishRoot))
            throw new ArgumentException("PublishRoot is required.", nameof(options));

        var root = Path.GetFullPath(options.PublishRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Publish root not found: {root}");

        var siteRoot = root;
        var wwwroot = Path.Combine(root, "wwwroot");
        if (Directory.Exists(wwwroot) && File.Exists(Path.Combine(wwwroot, "index.html")))
            siteRoot = wwwroot;
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var affected = new HashSet<string>(StringComparer.Ordinal) { "index.html" };

        if (!string.IsNullOrWhiteSpace(options.BaseHref))
            UpdateBaseHref(Path.Combine(siteRoot, "index.html"), options.BaseHref);

        var frameworkPath = Path.Combine(siteRoot, "_framework");
        if (Directory.Exists(frameworkPath))
        {
            if (options.CopyFingerprintBlazorJs)
            {
                CopyFingerprintBlazorJs(frameworkPath);
                affected.Add("_framework/blazor.webassembly.js");
            }
            if (options.UpdateBootIntegrity)
            {
                UpdateBootIntegrity(frameworkPath);
                affected.Add("_framework/blazor.boot.json");
            }
        }

        if (options.AddCacheBuster)
        {
            AddCacheBuster(Path.Combine(siteRoot, "index.html"), siteRoot, aliases);
            VersionStylesheets(Path.Combine(siteRoot, "index.html"), siteRoot, aliases);
        }
        foreach (var alias in aliases)
        {
            affected.Add(alias.Key);
            affected.Add(alias.Value);
        }
        UpdateOfflineAssets(siteRoot, aliases, affected);
        foreach (var path in affected) RefreshCompressedAssets(siteRoot, path, aliases);
        UpdateEndpointManifests(root, siteRoot, aliases, affected);
    }

    private static void UpdateBaseHref(string htmlPath, string baseHref)
    {
        if (!File.Exists(htmlPath)) return;
        var content = File.ReadAllText(htmlPath);
        var pattern = "<base href=\"/\"\\s*/?>";
        var replacement = $"<base href=\"{baseHref.Trim()}\" />";
        var updated = Regex.Replace(content, pattern, replacement, RegexOptions.IgnoreCase);
        if (!string.Equals(updated, content, StringComparison.Ordinal))
            File.WriteAllText(htmlPath, updated);
    }

    private static void UpdateBootIntegrity(string frameworkPath)
    {
        var bootPath = Path.Combine(frameworkPath, "blazor.boot.json");
        if (!File.Exists(bootPath)) return;

        using var doc = JsonDocument.Parse(File.ReadAllText(bootPath));
        var root = doc.RootElement.Clone();
        var updated = JsonSerializer.Deserialize<Dictionary<string, object?>>(root.GetRawText());
        if (updated is null) return;

        if (!updated.TryGetValue("resources", out var resourcesObj) || resourcesObj is not JsonElement resourcesElement)
            return;

        var resources = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(resourcesElement.GetRawText());
        if (resources is null) return;

        foreach (var resourceSet in resources)
        {
            foreach (var file in resourceSet.Value.Keys.ToList())
            {
                var filePath = Path.Combine(frameworkPath, file);
                if (!File.Exists(filePath)) continue;
                var hash = ComputeHash(filePath);
                resourceSet.Value[file] = $"sha256-{hash}";
            }
        }

        updated["resources"] = resources;
        WriteChangedBytes(bootPath, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true })));
    }

    private static string ComputeHash(string filePath)
    {
        using var sha = SHA256.Create();
        var bytes = File.ReadAllBytes(filePath);
        return Convert.ToBase64String(sha.ComputeHash(bytes));
    }

    private static void CopyFingerprintBlazorJs(string frameworkPath)
    {
        var file = Directory.EnumerateFiles(frameworkPath, "blazor.webassembly.*.js")
            .FirstOrDefault(f => !f.EndsWith(".br", StringComparison.OrdinalIgnoreCase) &&
                                 !f.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(file)) return;

        var target = Path.Combine(frameworkPath, "blazor.webassembly.js");
        WriteChangedBytes(target, File.ReadAllBytes(file));
    }

    private static void AddCacheBuster(string htmlPath, string siteRoot, Dictionary<string, string> aliases)
    {
        if (!File.Exists(htmlPath)) return;
        var content = File.ReadAllText(htmlPath);
        var pattern = "src=\"(?<url>_framework/blazor\\.webassembly(?:\\.pf-[a-f0-9]{64})?\\.js(?:\\?[^\"]*)?)\"";
        var updated = Regex.Replace(content, pattern,
            match => $"src=\"{VersionAssetUrl(match.Groups["url"].Value, siteRoot, aliases)}\"", RegexOptions.IgnoreCase);
        if (!string.Equals(updated, content, StringComparison.Ordinal))
            File.WriteAllText(htmlPath, updated);
    }
}
