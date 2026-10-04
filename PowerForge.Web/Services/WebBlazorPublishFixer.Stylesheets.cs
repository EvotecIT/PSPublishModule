using System.Net;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebBlazorPublishFixer
{
    // Scoped CSS has a stable entry point which imports fingerprinted component bundles.
    // Version the entry point by its bytes so a cached older import cannot survive an upgrade.
    private static void VersionStylesheets(string htmlPath, string siteRoot)
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
                if (url.StartsWith('/') || url.StartsWith('\\') || Uri.TryCreate(url, UriKind.Absolute, out _))
                    return href.Value;

                var fragmentIndex = url.IndexOf('#');
                var fragment = fragmentIndex < 0 ? "" : url[fragmentIndex..];
                var resource = fragmentIndex < 0 ? url : url[..fragmentIndex];
                var queryIndex = resource.IndexOf('?');
                var path = queryIndex < 0 ? resource : resource[..queryIndex];
                var query = queryIndex < 0 ? "" : resource[(queryIndex + 1)..];
                string filePath;
                try
                {
                    filePath = Path.GetFullPath(Path.Combine(siteRoot, Uri.UnescapeDataString(path)));
                }
                catch (ArgumentException) { return href.Value; }
                catch (NotSupportedException) { return href.Value; }

                var relative = Path.GetRelativePath(siteRoot, filePath);
                if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || Path.IsPathRooted(relative) || !File.Exists(filePath)
                    || !filePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) return href.Value;

                var version = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(filePath)))
                    .ToLowerInvariant();
                var parameters = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => !p.Split('=', 2)[0].Equals("v", StringComparison.OrdinalIgnoreCase)).ToList();
                parameters.Add("v=" + version);
                var value = WebUtility.HtmlEncode(path + "?" + string.Join("&", parameters) + fragment);
                var valueIndex = href.Groups["value"].Index - href.Index;
                return href.Value[..valueIndex] + value
                    + href.Value[(valueIndex + href.Groups["value"].Length)..];
            }, RegexOptions.IgnoreCase);
        }, RegexOptions.IgnoreCase);
        if (!string.Equals(updated, content, StringComparison.Ordinal)) File.WriteAllText(htmlPath, updated);
    }
}
