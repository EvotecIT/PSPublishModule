using System.Net;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebBlazorPublishFixer
{
    // Scoped CSS has a stable entry point which imports fingerprinted component bundles.
    // Version the entry point by its bytes so a cached older import cannot survive an upgrade.
    private static void VersionStylesheets(string htmlPath, string siteRoot, Dictionary<string, string> aliases)
    {
        if (!File.Exists(htmlPath)) return;
        var content = File.ReadAllText(htmlPath);
        var updated = Regex.Replace(content, @"<link\b[^>]*>", link =>
        {
            var rel = Regex.Match(link.Value, @"\srel\s*=\s*(['""])(?<value>.*?)\1", RegexOptions.IgnoreCase);
            if (!rel.Success || !rel.Groups["value"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Contains("stylesheet", StringComparer.OrdinalIgnoreCase)) return link.Value;

            return Regex.Replace(link.Value, @"\shref\s*=\s*(['""])(?<value>.*?)\1", href =>
            {
                var url = WebUtility.HtmlDecode(href.Groups["value"].Value);
                if (!url.Split('?', '#')[0].EndsWith(".css", StringComparison.OrdinalIgnoreCase)) return href.Value;
                var value = WebUtility.HtmlEncode(VersionAssetUrl(url, siteRoot, aliases));
                var valueIndex = href.Groups["value"].Index - href.Index;
                return href.Value[..valueIndex] + value
                    + href.Value[(valueIndex + href.Groups["value"].Length)..];
            }, RegexOptions.IgnoreCase);
        }, RegexOptions.IgnoreCase);
        if (!string.Equals(updated, content, StringComparison.Ordinal)) File.WriteAllText(htmlPath, updated);
    }

    private static string VersionAssetUrl(string url, string siteRoot, Dictionary<string, string> aliases)
    {
        var fragmentIndex = url.IndexOf('#');
        var fragment = fragmentIndex < 0 ? "" : url[fragmentIndex..];
        var resource = fragmentIndex < 0 ? url : url[..fragmentIndex];
        var queryIndex = resource.IndexOf('?');
        var path = queryIndex < 0 ? resource : resource[..queryIndex];
        var query = queryIndex < 0 ? "" : resource[(queryIndex + 1)..];
        // Only our own copies are mapped back to their original entry point on repeated applies.
        var original = Regex.Replace(path, @"\.pf-[a-f0-9]{64}(?=\.(?:css|js)$)", "", RegexOptions.IgnoreCase);
        var filePath = ResolveLocalAsset(siteRoot, original);
        if (filePath is null || !File.Exists(filePath)) return url;
        var version = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant();
        var extension = Path.GetExtension(original);
        var versioned = original[..^extension.Length] + ".pf-" + version + extension;
        var target = ResolveLocalAsset(siteRoot, versioned)!;
        WriteChangedBytes(target, File.ReadAllBytes(filePath));
        aliases[Path.GetRelativePath(siteRoot, filePath).Replace('\\', '/')] =
            Path.GetRelativePath(siteRoot, target).Replace('\\', '/');
        return versioned + (queryIndex < 0 ? "" : "?" + query) + fragment;
    }

    private static string? ResolveLocalAsset(string siteRoot, string path)
    {
        if (path.StartsWith('/') || path.StartsWith('\\') || Uri.TryCreate(path, UriKind.Absolute, out _)) return null;
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(siteRoot, Uri.UnescapeDataString(path)));
            var relative = Path.GetRelativePath(siteRoot, fullPath);
            return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(relative) ? null : fullPath;
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }
}
