using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static readonly TimeSpan SocialRegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex MarkdownFenceRegex = new(
        "```[\\s\\S]*?```",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        SocialRegexTimeout);
    private static readonly Regex MarkdownImageRegex = new(
        "!\\[[^\\]]*\\]\\((?<target>[^)]+)\\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        SocialRegexTimeout);
    private static readonly Regex HtmlImageRegex = new(
        "<img\\b[^>]*\\bsrc\\s*=\\s*['\"](?<src>[^'\"]+)['\"][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        SocialRegexTimeout);

    private static string ResolveSocialImageOverride(ContentItem item)
    {
        var explicitMetaImage =
            GetMetaString(item.Meta, "social_image") ??
            GetMetaString(item.Meta, "social.image") ??
            GetMetaString(item.Meta, "og_image") ??
            GetMetaString(item.Meta, "twitter_image") ??
            GetMetaString(item.Meta, "cover_image") ??
            GetMetaString(item.Meta, "thumbnail") ??
            GetMetaString(item.Meta, "image");

        if (!string.IsNullOrWhiteSpace(explicitMetaImage))
            return explicitMetaImage!;

        if (!string.Equals(item.Collection, "blog", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return TryExtractFirstBodyImage(item.SourcePath);
    }

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
        var routeLabel = ResolveSocialRouteLabel(item, routeForSlug);
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

        var badge = ResolveSocialBadge(item, routeForSlug);
        var styleKey = ResolveSocialCardStyle(spec, item, badge, routeForSlug);
        var variantKey = ResolveSocialCardVariant(spec, item, styleKey, routeForSlug);
        var bytes = WebSocialCardGenerator.RenderPng(
            title,
            description,
            siteName,
            badge,
            routeLabel,
            spec.Social.GeneratedCardWidth,
            spec.Social.GeneratedCardHeight,
            styleKey,
            variantKey);
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

    private static string ResolveSocialRouteLabel(ContentItem item, string? route)
    {
        var routeOverride =
            GetMetaString(item.Meta, "social_card_route") ??
            GetMetaString(item.Meta, "social.route");
        if (!string.IsNullOrWhiteSpace(routeOverride))
            return routeOverride!.Trim();

        return BuildSocialRouteDisplayLabel(route);
    }

    private static string BuildSocialRouteDisplayLabel(string? route)
    {
        var normalized = NormalizePath(route).Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, "index", StringComparison.OrdinalIgnoreCase))
            return "/";

        return "/" + normalized;
    }

    private static string ResolveSocialBadge(ContentItem item, string route)
    {
        var metaBadge =
            GetMetaString(item.Meta, "social_card_badge") ??
            GetMetaString(item.Meta, "social.badge");
        if (!string.IsNullOrWhiteSpace(metaBadge))
            return metaBadge!.Trim().ToUpperInvariant();

        if (item.Kind == PageKind.Home)
            return "HOME";

        if (!string.IsNullOrWhiteSpace(item.Collection))
        {
            var collection = item.Collection.Trim();
            if (string.Equals(collection, "pages", StringComparison.OrdinalIgnoreCase))
                return "PAGES";
            if (string.Equals(collection, "docs", StringComparison.OrdinalIgnoreCase))
                return "DOCS";
            if (string.Equals(collection, "blog", StringComparison.OrdinalIgnoreCase))
                return "BLOG";
            return collection.ToUpperInvariant();
        }

        var normalizedRoute = NormalizePath(route).Trim('/');
        if (normalizedRoute.StartsWith("docs", StringComparison.OrdinalIgnoreCase))
            return "DOCS";

        return "PAGE";
    }

    private static string ResolveSocialCardStyle(
        SiteSpec spec,
        ContentItem item,
        string badge,
        string route)
    {
        var styleOverride =
            GetMetaString(item.Meta, "social_card_style") ??
            GetMetaString(item.Meta, "social.style");
        if (!string.IsNullOrWhiteSpace(styleOverride))
            return styleOverride!.Trim();

        var collection = item.Collection?.Trim();
        if (!string.IsNullOrWhiteSpace(collection) &&
            TryResolveCollectionCardPreset(spec.Social?.GeneratedCardStylesByCollection, collection!, out var collectionStyle))
            return collectionStyle;

        if (!string.IsNullOrWhiteSpace(spec.Social?.GeneratedCardStyle))
            return spec.Social.GeneratedCardStyle!.Trim();

        return InferSocialCardStyle(badge, route);
    }

    private static string ResolveSocialCardVariant(
        SiteSpec spec,
        ContentItem item,
        string styleKey,
        string route)
    {
        var variantOverride =
            GetMetaString(item.Meta, "social_card_variant") ??
            GetMetaString(item.Meta, "social.variant");
        if (!string.IsNullOrWhiteSpace(variantOverride))
            return variantOverride!.Trim();

        var collection = item.Collection?.Trim();
        if (!string.IsNullOrWhiteSpace(collection) &&
            TryResolveCollectionCardPreset(spec.Social?.GeneratedCardVariantsByCollection, collection!, out var collectionVariant))
            return collectionVariant;

        if (!string.IsNullOrWhiteSpace(spec.Social?.GeneratedCardVariant))
            return spec.Social.GeneratedCardVariant!.Trim();

        return InferSocialCardVariant(item, styleKey, route);
    }

    private static bool TryResolveCollectionCardPreset(
        Dictionary<string, string>? map,
        string collection,
        out string value)
    {
        value = string.Empty;
        if (map is null || map.Count == 0 || string.IsNullOrWhiteSpace(collection))
            return false;

        if (map.TryGetValue(collection, out var direct) && !string.IsNullOrWhiteSpace(direct))
        {
            value = direct.Trim();
            return true;
        }

        foreach (var kvp in map)
        {
            if (string.Equals(kvp.Key, collection, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(kvp.Value))
            {
                value = kvp.Value.Trim();
                return true;
            }
        }

        return false;
    }

    private static string InferSocialCardStyle(string badge, string route)
    {
        var combined = string.Concat(badge ?? string.Empty, " ", route ?? string.Empty).ToLowerInvariant();
        if (combined.Contains("api", StringComparison.Ordinal))
            return "api";
        if (combined.Contains("doc", StringComparison.Ordinal))
            return "docs";
        if (combined.Contains("blog", StringComparison.Ordinal) ||
            combined.Contains("post", StringComparison.Ordinal) ||
            combined.Contains("news", StringComparison.Ordinal) ||
            combined.Contains("article", StringComparison.Ordinal))
            return "editorial";
        return "default";
    }

    private static string InferSocialCardVariant(ContentItem item, string styleKey, string route)
    {
        if (item.Kind == PageKind.Home)
            return "hero";

        var normalizedRoute = NormalizePath(route).Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedRoute) ||
            string.Equals(normalizedRoute, "index", StringComparison.OrdinalIgnoreCase))
            return "hero";

        if (string.Equals(styleKey, "docs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(styleKey, "editorial", StringComparison.OrdinalIgnoreCase))
            return "compact";

        return "standard";
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

        if (ShouldGenerateSocialCardForDocsEntry(item))
            return true;

        if (string.Equals(item.Collection, "pages", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IsEditorialCollection(item.Collection))
            return true;

        return false;
    }

    private static bool ShouldGenerateSocialCardForDocsEntry(ContentItem item)
    {
        if (!string.Equals(item.Collection, "docs", StringComparison.OrdinalIgnoreCase))
            return false;

        var route = BuildSocialRouteLabel(item);
        var normalized = NormalizePath(route).Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (string.Equals(normalized, "docs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "docs/index", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!normalized.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Length;
        return segments <= 2;
    }

    private static string TryExtractFirstBodyImage(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return string.Empty;

        try
        {
            var markdown = File.ReadAllText(sourcePath);
            var (_, body) = FrontMatterParser.Parse(markdown);
            var scrubbed = MarkdownFenceRegex.Replace(body ?? string.Empty, string.Empty);

            var markdownMatch = MarkdownImageRegex.Match(scrubbed);
            if (markdownMatch.Success)
            {
                var target = ExtractMarkdownImageTarget(markdownMatch.Groups["target"].Value);
                var normalized = NormalizeSocialImageCandidate(target);
                if (!string.IsNullOrWhiteSpace(normalized))
                    return normalized;
            }

            var htmlMatch = HtmlImageRegex.Match(scrubbed);
            if (htmlMatch.Success)
            {
                var normalized = NormalizeSocialImageCandidate(htmlMatch.Groups["src"].Value);
                if (!string.IsNullOrWhiteSpace(normalized))
                    return normalized;
            }
        }
        catch
        {
            // Ignore extraction errors and fall back to generated/default image.
        }

        return string.Empty;
    }

    private static string ExtractMarkdownImageTarget(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var value = raw.Trim();
        if (value.StartsWith("<", StringComparison.Ordinal) && value.EndsWith(">", StringComparison.Ordinal) && value.Length > 2)
            value = value[1..^1].Trim();

        var titleDelimiter = value.IndexOf(" \"", StringComparison.Ordinal);
        if (titleDelimiter > 0)
            value = value[..titleDelimiter].Trim();

        return value;
    }

    private static string NormalizeSocialImageCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        var trimmed = candidate.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
            return trimmed;

        return string.Empty;
    }
}
