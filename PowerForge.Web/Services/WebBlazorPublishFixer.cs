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
    /// <summary>When true, append cache-busting query to blazor JS.</summary>
    public bool AddCacheBuster { get; set; } = true;
}

/// <summary>Applies fixes to Blazor static publish output.</summary>
public static class WebBlazorPublishFixer
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

        if (!string.IsNullOrWhiteSpace(options.BaseHref))
            UpdateBaseHref(Path.Combine(siteRoot, "index.html"), options.BaseHref);

        var frameworkPath = Path.Combine(siteRoot, "_framework");
        if (Directory.Exists(frameworkPath))
        {
            if (options.CopyFingerprintBlazorJs)
                CopyFingerprintBlazorJs(frameworkPath);
            if (options.UpdateBootIntegrity)
                UpdateBootIntegrity(frameworkPath);
        }

        if (options.AddCacheBuster)
            AddCacheBuster(Path.Combine(siteRoot, "index.html"));
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
        File.WriteAllText(bootPath, JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true }));
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
        File.Copy(file, target, overwrite: true);
    }

    private static void AddCacheBuster(string htmlPath)
    {
        if (!File.Exists(htmlPath)) return;
        var content = File.ReadAllText(htmlPath);
        var version = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var pattern = "src=\"_framework/blazor\\.webassembly\\.js\"";
        var replacement = $"src=\"_framework/blazor.webassembly.js?v={version}\"";
        var updated = Regex.Replace(content, pattern, replacement, RegexOptions.IgnoreCase);
        if (!string.Equals(updated, content, StringComparison.Ordinal))
            File.WriteAllText(htmlPath, updated);
    }
}
