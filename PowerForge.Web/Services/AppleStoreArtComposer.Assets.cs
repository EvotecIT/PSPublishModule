using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class AppleStoreArtComposer
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private static readonly Lazy<Dictionary<string, byte[]>> Assets = new(() =>
    {
        const string prefix = "PowerForge.Web.Assets.StoreArt.";
        var assembly = typeof(AppleStoreArtComposer).Assembly;
        return assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal).ToDictionary(n => n[prefix.Length..], n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
            }, StringComparer.Ordinal);
    });
    private static string Asset(string name) => Encoding.UTF8.GetString(Assets.Value[name]);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static string Escape(string? text) => WebUtility.HtmlEncode(text ?? "");
    private static string Headline(string text) => Escape(text).Replace("\n", "<br>")
        .Replace("&lt;em&gt;", "<em>").Replace("&lt;/em&gt;", "</em>");
    private static string FontCss(bool includeCjk)
    {
        var fonts = new[] { ("Inter", "Inter"), ("Hanken Grotesk", "HankenGrotesk"),
            ("JetBrains Mono", "JetBrainsMono"), ("Noto Sans CJK", "NotoSansCJK") };
        return string.Join("\n", fonts.Where(f => includeCjk || f.Item2 != "NotoSansCJK").Select(f => "@font-face{font-family:'" + f.Item1 +
            "';font-style:normal;font-weight:100 900;font-display:block;src:url(data:font/ttf;base64," +
            Convert.ToBase64String(Assets.Value["Fonts." + f.Item2 + ".ttf"]) + ") format('truetype')}"));
    }
    private static string TemplateHash()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var asset in Assets.Value.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(asset.Key + "\0")); hash.AppendData(asset.Value);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static string ThemeCss(AppleStoreArtTheme theme)
    {
        if (theme.Name is not ("casaray" or "tactra" or "officeimo"))
            throw new ArgumentException("Theme must be casaray, tactra, or officeimo.");
        var tokens = new StringBuilder();
        foreach (var pair in theme.Tokens.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!Regex.IsMatch(pair.Key, "^--[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant) ||
                string.IsNullOrWhiteSpace(pair.Value) || pair.Value.IndexOfAny(['<', '>', '{', '}', ';', '@', '\\']) >= 0 ||
                Regex.IsMatch(pair.Value, @"url\s*\(|expression\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new ArgumentException($"Invalid theme token '{pair.Key}'. Only CSS token values are supported.");
            tokens.Append(pair.Key).Append(':').Append(pair.Value).Append(';');
        }
        return Asset(theme.Name + ".css") + "\n:root,body.light,body.dark{" + tokens + "}";
    }
    private static void SafeId(string value, string name)
    {
        if (!Regex.IsMatch(value, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$", RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid {name} '{value}'. Use letters, numbers, '-' and '_'.");
    }
    private static string Inside(string root, string path)
    {
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        var relative = Path.GetRelativePath(root, full);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Store art path escapes capture root: {path}");
        Unlinked(full, root);
        return full;
    }
    private static void Unlinked(string path, string? boundary = null)
    {
        var full = Path.GetFullPath(path);
        // The caller-selected root is trusted. Inspect its entries, without rejecting OS aliases
        // above it (for example macOS /var -> /private/var).
        var stop = Path.GetFullPath(boundary ?? Path.GetDirectoryName(full)!).TrimEnd(Path.DirectorySeparatorChar);
        FileSystemInfo? item = new FileInfo(full);
        while (item is not null)
        {
            if (item.LinkTarget is not null || (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new InvalidOperationException($"Store art paths must not traverse links: {item.FullName}");
            if (string.Equals(item.FullName.TrimEnd(Path.DirectorySeparatorChar), stop, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) break;
            item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent;
        }
    }
}
