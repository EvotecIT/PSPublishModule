using System.Security.Cryptography;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string ResolveSocialImagePath(
        SiteSpec spec,
        ContentItem item,
        string outputRoot,
        string title,
        string description,
        string siteName,
        string imageOverride)
    {
        if (!string.IsNullOrWhiteSpace(imageOverride))
            return imageOverride;

        if (spec.Social?.AutoGenerateCards == true && ShouldAutoGenerateSocialCardForPage(item))
        {
            var generated = TryGenerateSocialCardPath(spec, item, outputRoot, title, description, siteName);
            if (!string.IsNullOrWhiteSpace(generated))
                return generated;
        }

        return spec.Social?.Image ?? string.Empty;
    }

    private static string TryGenerateSocialCardPath(
        SiteSpec spec,
        ContentItem item,
        string outputRoot,
        string title,
        string description,
        string siteName)
    {
        if (spec.Social is null || string.IsNullOrWhiteSpace(outputRoot))
            return string.Empty;

        var normalizedOutputRoot = NormalizeRootPathForSink(outputRoot);
        var generatedPath = NormalizeGeneratedCardsPath(spec.Social.GeneratedCardsPath);
        var routeForSlug = BuildSocialRouteLabel(item);
        var routeSlug = Slugify(routeForSlug.Replace('/', '-'));
        if (string.IsNullOrWhiteSpace(routeSlug))
            routeSlug = "page";

        var hashInput = string.Join("|", new[]
        {
            routeForSlug,
            title ?? string.Empty,
            description ?? string.Empty,
            siteName ?? string.Empty
        });
        var hash = ComputeSocialHash(hashInput);
        var fileName = $"{routeSlug}-{hash}.png";
        var relativePath = $"{generatedPath.TrimStart('/')}/{fileName}".TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithinRoot(normalizedOutputRoot, fullPath))
            return string.Empty;

        var badge = string.IsNullOrWhiteSpace(item.Collection)
            ? "PAGE"
            : item.Collection.ToUpperInvariant();
        var bytes = WebSocialCardGenerator.RenderPng(
            title,
            description,
            siteName,
            badge,
            spec.Social.GeneratedCardWidth,
            spec.Social.GeneratedCardHeight);
        if (bytes is null || bytes.Length == 0)
            return string.Empty;

        if (!WriteAllBytesIfChanged(fullPath, bytes))
            return "/" + relativePath.Replace('\\', '/');
        return "/" + relativePath.Replace('\\', '/');
    }

    private static string NormalizeGeneratedCardsPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/assets/social/generated";

        var normalized = value.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized.TrimStart('/');
        return normalized.TrimEnd('/');
    }

    private static string BuildSocialRouteLabel(ContentItem item)
    {
        var route = string.IsNullOrWhiteSpace(item.Canonical) ? item.OutputPath : item.Canonical!;
        if (Uri.TryCreate(route, UriKind.Absolute, out var absolute))
            route = absolute.AbsolutePath;

        var normalized = NormalizePath(route).Trim('/');
        return string.IsNullOrWhiteSpace(normalized) ? "index" : normalized;
    }

    private static string ComputeSocialHash(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant()[..10];
    }

    private static bool WriteAllBytesIfChanged(string path, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(path) || content is null)
            return false;

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
            // Fall back to writing when comparison fails.
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(path, content);
        UpdatedSink.Value?.Invoke(path);
        return true;
    }

    private static bool ShouldAutoGenerateSocialCardForPage(ContentItem item)
    {
        if (item is null)
            return false;

        // Explicit front matter override: meta.social_card: true/false.
        if (TryGetMetaBool(item.Meta, "social_card", out var overrideValue))
            return overrideValue;

        if (string.Equals(NormalizePath(item.OutputPath), "404", StringComparison.OrdinalIgnoreCase))
            return false;

        if (item.Kind == PageKind.Home || item.Kind == PageKind.Section)
            return true;

        if (string.Equals(item.Collection, "pages", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IsEditorialCollection(item.Collection))
            return true;

        return false;
    }
}
