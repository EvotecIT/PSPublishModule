using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string ResolveCardTitle(SiteSpec spec, ContentItem item, string seoTitle)
    {
        var explicitTitle = GetMetaString(item.Meta, "social_title");
        if (!string.IsNullOrWhiteSpace(explicitTitle))
            return explicitTitle;
        var source = spec.Social?.GeneratedCardTitle;
        if (TryResolveCollectionCardPreset(spec.Social?.GeneratedCardTitlesByCollection, item.Collection, out var collectionSource))
            source = collectionSource;
        if (string.IsNullOrWhiteSpace(source) || source.Equals("seo", StringComparison.OrdinalIgnoreCase))
            return seoTitle;
        if (!source.Equals("page", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unknown generated card title source '{source}'. Use 'page' or 'seo'.");
        return string.IsNullOrWhiteSpace(item.Title) ? seoTitle : item.Title;
    }

    private static string ResolveCardDescription(SiteSpec spec, ContentItem item, string description)
    {
        var explicitDescription = GetMetaString(item.Meta, "social_description");
        if (!string.IsNullOrWhiteSpace(explicitDescription))
            return explicitDescription;
        return spec.Social?.GeneratedCardTextFit == true
            ? FirstCardSentence(string.IsNullOrWhiteSpace(item.Description) ? description : item.Description)
            : description;
    }

    internal static string FirstCardSentence(string description)
    {
        // Sentence punctuation followed by whitespace avoids splitting version numbers, URLs, or .NET names.
        var match = Regex.Match(description ?? string.Empty, @"[.!?](?=\s|$)", RegexOptions.CultureInvariant, SocialRegexTimeout);
        return match.Success ? description![..(match.Index + 1)] : description ?? string.Empty;
    }

    private static string ResolveConfiguredCardBadge(SiteSpec spec, ContentItem item, string route)
    {
        var explicitBadge = FirstNonEmpty(GetMetaString(item.Meta, "social_card_badge"), GetMetaString(item.Meta, "social.badge"));
        if (!string.IsNullOrWhiteSpace(explicitBadge))
            return explicitBadge.Trim().ToUpperInvariant();
        var key = spec.Social?.GeneratedCardBadgeKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            var value = GetMetaString(item.Meta, key.StartsWith("meta.", StringComparison.OrdinalIgnoreCase) ? key[5..] : key);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim().ToUpperInvariant();
        }
        var map = spec.Social?.GeneratedCardBadges;
        if (map is not null)
        {
            var path = "/" + (route == "index" ? string.Empty : route.Trim('/')) + "/";
            var prefix = map.Where(pair => pair.Key.StartsWith('/') && !string.IsNullOrWhiteSpace(pair.Value))
                .OrderByDescending(pair => pair.Key.TrimEnd('/').Length)
                .FirstOrDefault(pair => path.StartsWith(pair.Key.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(prefix.Value))
                return prefix.Value.Trim().ToUpperInvariant();
            if (TryResolveCollectionCardPreset(map, item.Collection, out var collectionBadge))
                return collectionBadge.ToUpperInvariant();
        }
        return ResolveSocialBadge(item, route);
    }

    private static string ResolveConfiguredCardImage(SiteSpec spec, ContentItem item, string? rootPath, bool allowRemoteMediaFetch)
    {
        // Explicit card artwork wins over a convention; generic social/cover images remain the legacy fallback.
        var explicitImage = FirstNonEmpty(GetMetaString(item.Meta, "social_card_image"), GetMetaString(item.Meta, "social.card_image"),
            GetMetaString(item.Meta, "social_card_media"), GetMetaString(item.Meta, "social.media"));
        var rule = spec.Social?.GeneratedCardImages?.FirstOrDefault(pair => pair.Key.Equals(item.Collection, StringComparison.OrdinalIgnoreCase)).Value;
        var hasExplicitImage = !string.IsNullOrWhiteSpace(explicitImage);
        if (rule is null)
        {
            var directCandidate = hasExplicitImage ? explicitImage! : ResolveSocialCardInlineImageCandidate(item);
            if (!string.IsNullOrWhiteSpace(directCandidate) && spec.Social?.GeneratedCardImagesRequired == true &&
                !CardImageAvailable(spec, item, directCandidate, rootPath, allowRemoteMediaFetch))
                throw new InvalidOperationException($"Required social card image '{directCandidate}' could not be resolved for '{item.OutputPath}'.");
            return directCandidate;
        }

        string Expand(string? pattern) => Regex.Replace(pattern ?? string.Empty, @"\{(?<key>meta\.[^{}]+|slug|title)\}", match =>
        {
            var key = match.Groups["key"].Value;
            return key == "slug" ? item.Slug ?? string.Empty : key == "title" ? item.Title ?? string.Empty : GetMetaString(item.Meta, key[5..]);
        }, RegexOptions.CultureInvariant, SocialRegexTimeout);

        var candidate = hasExplicitImage ? explicitImage! : Expand(rule.Pattern);
        // An unresolved placeholder never becomes an accidental /cards/.svg match.
        var missingKey = !hasExplicitImage && Regex.Matches(rule.Pattern ?? string.Empty, @"\{meta\.([^{}]+)\}", RegexOptions.CultureInvariant, SocialRegexTimeout)
            .Any(match => string.IsNullOrWhiteSpace(GetMetaString(item.Meta, match.Groups[1].Value)));
        if (!missingKey && CardImageAvailable(spec, item, candidate, rootPath, allowRemoteMediaFetch))
            return candidate;
        ReportCardWarning($"Social card image missing for '{item.OutputPath}': '{(hasExplicitImage ? explicitImage : rule.Pattern)}' (resolved '{candidate}').");
        var fallback = Expand(rule.Fallback);
        if (CardImageAvailable(spec, item, fallback, rootPath, allowRemoteMediaFetch))
            return fallback;
        if (!string.IsNullOrWhiteSpace(rule.Fallback))
            ReportCardWarning($"Social card fallback image missing for '{item.OutputPath}': '{fallback}'.");
        if (spec.Social?.GeneratedCardImagesRequired == true)
            throw new InvalidOperationException($"Required social card image could not be resolved for '{item.OutputPath}'.");
        return string.Empty;
    }

    private static bool CardImageExists(SiteSpec spec, ContentItem item, string candidate, string? rootPath)
        => !string.IsNullOrWhiteSpace(candidate) &&
           (candidate.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(TryResolveSocialCardAssetPath(spec, item, candidate, rootPath)));

    private static bool CardImageAvailable(SiteSpec spec, ContentItem item, string candidate, string? rootPath, bool allowRemoteMediaFetch)
        => IsRemoteCardImage(candidate)
            ? WebSocialCardGenerator.IsRenderableImageSource(candidate, allowRemoteMediaFetch)
            : CardImageExists(spec, item, candidate, rootPath);

    private static bool IsRemoteCardImage(string candidate)
        => Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static void ReportCardWarning(string message)
    {
        Trace.TraceWarning(message);
        ReportBuildProgress("WARNING: " + message);
    }
}
