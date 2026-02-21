using System.Security.Cryptography;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static string ResolveApiSocialImagePath(
        WebApiDocsOptions options,
        ApiSocialProfile social,
        string title,
        string description,
        string route)
    {
        var imagePath = social.Image;
        if (options.AutoGenerateSocialCards)
        {
            var generated = TryGenerateApiSocialCard(options, title, description, route, social.SiteName);
            if (!string.IsNullOrWhiteSpace(generated))
                imagePath = generated;
        }

        return imagePath;
    }

    private static string TryGenerateApiSocialCard(
        WebApiDocsOptions options,
        string title,
        string description,
        string route,
        string siteName)
    {
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            return string.Empty;

        var siteRoot = ResolveApiSiteRoot(options.OutputPath, options.BaseUrl);
        if (string.IsNullOrWhiteSpace(siteRoot))
            return string.Empty;

        var cardPath = NormalizeApiSocialCardPath(options.SocialCardPath);
        var routeLabel = NormalizeApiRouteLabel(route);
        var slug = Slugify(routeLabel.Replace('/', '-'));
        if (string.IsNullOrWhiteSpace(slug))
            slug = "api";

        var hashInput = string.Join("|", new[]
        {
            routeLabel,
            title ?? string.Empty,
            description ?? string.Empty,
            siteName ?? string.Empty,
            options.Title ?? string.Empty
        });
        var hash = ComputeApiSocialHash(hashInput);
        var fileName = $"{slug}-{hash}.png";
        var relativePath = $"{cardPath.TrimStart('/')}/{fileName}".TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(siteRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsApiPathWithinRoot(siteRoot, fullPath))
            return string.Empty;

        var bytes = WebSocialCardGenerator.RenderPng(
            title,
            description,
            siteName,
            "API",
            routeLabel,
            options.SocialCardWidth,
            options.SocialCardHeight,
            "api",
            "standard");
        if (bytes is null || bytes.Length == 0)
            return string.Empty;

        WriteApiBytesIfChanged(fullPath, bytes);
        return "/" + relativePath.Replace('\\', '/');
    }

    private static string ResolveApiSiteRoot(string outputPath, string? baseUrl)
    {
        var root = Path.GetFullPath(outputPath);
        var normalizedBase = baseUrl ?? string.Empty;
        if (Uri.TryCreate(normalizedBase, UriKind.Absolute, out var absolute))
            normalizedBase = absolute.AbsolutePath;

        var segmentCount = normalizedBase
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Length;
        if (segmentCount <= 0)
            return root;

        for (var i = 0; i < segmentCount; i++)
        {
            var parent = Directory.GetParent(root);
            if (parent is null)
                return Path.GetFullPath(outputPath);
            root = parent.FullName;
        }

        return root;
    }

    private static string NormalizeApiSocialCardPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/assets/social/generated/api";

        var normalized = value.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized.TrimStart('/');
        return normalized.TrimEnd('/');
    }

    private static string NormalizeApiRouteLabel(string route)
    {
        var value = route ?? string.Empty;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            value = absolute.AbsolutePath;

        value = value.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(value))
            return "api";
        if (!value.StartsWith("/", StringComparison.Ordinal))
            value = "/" + value;
        return value.Trim('/');
    }

    private static string ComputeApiSocialHash(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant()[..10];
    }

    private static bool WriteApiBytesIfChanged(string path, byte[] content)
    {
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.AsSpan().SequenceEqual(content))
                    return false;
            }
        }
        catch
        {
            // Fall through and rewrite.
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, content);
        return true;
    }

    private static bool IsApiPathWithinRoot(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(candidatePath);
        if (string.Equals(root, full, StringComparison.OrdinalIgnoreCase))
            return true;
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
