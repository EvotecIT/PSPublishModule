using System.Collections;
using System.Globalization;
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
        var explicitMetaImage = FirstNonEmpty(
            GetMetaString(item.Meta, "social_image"),
            GetMetaString(item.Meta, "social.image"),
            GetMetaString(item.Meta, "og_image"),
            GetMetaString(item.Meta, "twitter_image"),
            GetMetaString(item.Meta, "cover_image"),
            GetMetaString(item.Meta, "thumbnail"),
            GetMetaString(item.Meta, "image"));

        if (!string.IsNullOrWhiteSpace(explicitMetaImage))
            return explicitMetaImage!;

        if (!IsEditorialCollection(item.Collection))
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

        if (!string.IsNullOrWhiteSpace(spec.Social?.Image))
            return spec.Social.Image!;

        if (string.Equals(item.Collection, "blog", StringComparison.OrdinalIgnoreCase))
            return TryExtractFirstBodyImage(item.SourcePath);

        return string.Empty;
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

        var badge = ResolveSocialBadge(item, routeForSlug);
        var themeKey = ResolveSocialCardThemeKey(spec, item);
        var cardTheme = ResolveSocialCardTheme(spec.Social, themeKey);
        var hashThemeKey = cardTheme is null ? string.Empty : themeKey;
        var styleKey = ResolveSocialCardStyle(spec, item, badge, routeForSlug, cardTheme);
        var inlineImageCandidate = ResolveSocialCardInlineImageCandidate(item);
        var variantKey = ResolveSocialCardVariant(spec, item, styleKey, routeForSlug, cardTheme);
        var colorScheme = ResolveSocialCardColorScheme(spec, item, cardTheme) ?? string.Empty;
        var allowRemoteMediaFetch = ResolveSocialCardAllowRemoteMediaFetch(spec, item, cardTheme);
        var logoSource = ResolveSocialCardAssetDataUri(spec, item, ResolveSocialCardLogoCandidate(spec, item, cardTheme));
        var inlineImageSource = ShouldRenderSocialCardInlineImage(item, styleKey, variantKey, inlineImageCandidate)
            ? ResolveSocialCardAssetDataUri(spec, item, inlineImageCandidate)
            : string.Empty;
        var metrics = ResolveSocialCardMetrics(spec, item, cardTheme);
        var themeTokens = MergeSocialCardThemeTokens(BuildRenderCacheScope.Value?.Manifest?.Tokens, cardTheme?.Tokens);
        var themeTokenFingerprint = ComputeThemeTokenFingerprint(themeTokens);
        var metricsFingerprint = ComputeSocialCardMetricsFingerprint(metrics);
        var hashInput = string.Join("|", new[]
        {
            WebSocialCardGenerator.RendererVersion,
            hashThemeKey,
            routeForSlug,
            title ?? string.Empty,
            description ?? string.Empty,
            siteName ?? string.Empty,
            badge,
            styleKey,
            variantKey,
            colorScheme,
            allowRemoteMediaFetch ? "remote-media-enabled" : "remote-media-disabled",
            themeTokenFingerprint,
            metricsFingerprint,
            logoSource,
            inlineImageSource
        });
        var hash = ComputeSocialHash(hashInput);
        var fileName = $"{routeSlug}-{hash}.png";
        var relativePath = $"{generatedPath.TrimStart('/')}/{fileName}".TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithinRoot(normalizedOutputRoot, fullPath))
            return string.Empty;
        var bytes = WebSocialCardGenerator.RenderPng(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = title,
            Description = description,
            Eyebrow = siteName,
            Badge = badge,
            FooterLabel = routeLabel,
            Width = spec.Social.GeneratedCardWidth,
            Height = spec.Social.GeneratedCardHeight,
            StyleKey = styleKey,
            VariantKey = variantKey,
            ColorScheme = colorScheme,
            ThemeTokens = themeTokens,
            AllowRemoteMediaFetch = allowRemoteMediaFetch,
            LogoDataUri = logoSource,
            InlineImageDataUri = inlineImageSource,
            Metrics = metrics
        });
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
        var routeOverride = FirstNonEmpty(
            GetMetaString(item.Meta, "social_card_route"),
            GetMetaString(item.Meta, "social.route"));
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
        var metaBadge = FirstNonEmpty(
            GetMetaString(item.Meta, "social_card_badge"),
            GetMetaString(item.Meta, "social.badge"));
        if (!string.IsNullOrWhiteSpace(metaBadge))
            return metaBadge!.Trim().ToUpperInvariant();

        if (item.Kind == PageKind.Home)
            return "HOME";

        var normalizedRoute = NormalizePath(route).Trim('/');
        if (normalizedRoute.Contains("examples", StringComparison.OrdinalIgnoreCase))
            return "EXAMPLE";
        if (normalizedRoute.Contains("api", StringComparison.OrdinalIgnoreCase))
            return "API";
        if (normalizedRoute.Contains("docs", StringComparison.OrdinalIgnoreCase))
            return "DOCS";
        if (normalizedRoute.Contains("download", StringComparison.OrdinalIgnoreCase))
            return "DOWNLOAD";
        if (normalizedRoute.Contains("changelog", StringComparison.OrdinalIgnoreCase) ||
            normalizedRoute.Contains("release", StringComparison.OrdinalIgnoreCase))
            return "RELEASE";
        if (normalizedRoute.Contains("rss", StringComparison.OrdinalIgnoreCase) ||
            normalizedRoute.Contains("feed", StringComparison.OrdinalIgnoreCase))
            return "FEED";
        if (normalizedRoute.Contains("benchmark", StringComparison.OrdinalIgnoreCase))
            return "BENCH";

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

        if (normalizedRoute.StartsWith("docs", StringComparison.OrdinalIgnoreCase))
            return "DOCS";
        if (normalizedRoute.StartsWith("contact", StringComparison.OrdinalIgnoreCase) ||
            normalizedRoute.StartsWith("support", StringComparison.OrdinalIgnoreCase))
            return "CONTACT";
        if (string.IsNullOrWhiteSpace(normalizedRoute) ||
            string.Equals(normalizedRoute, "index", StringComparison.OrdinalIgnoreCase))
            return "HOME";

        return "PAGE";
    }

    private static string ResolveSocialCardStyle(
        SiteSpec spec,
        ContentItem item,
        string badge,
        string route,
        SocialCardThemeSpec? cardTheme)
    {
        var styleOverride = FirstNonEmpty(
            GetMetaString(item.Meta, "social_card_style"),
            GetMetaString(item.Meta, "social.style"));
        if (!string.IsNullOrWhiteSpace(styleOverride))
            return styleOverride!.Trim();

        var collection = item.Collection?.Trim();
        if (!string.IsNullOrWhiteSpace(collection) &&
            TryResolveCollectionCardPreset(spec.Social?.GeneratedCardStylesByCollection, collection!, out var collectionStyle))
            return collectionStyle;

        if (!string.IsNullOrWhiteSpace(cardTheme?.Style))
            return cardTheme.Style!.Trim();

        if (!string.IsNullOrWhiteSpace(spec.Social?.GeneratedCardStyle))
            return spec.Social.GeneratedCardStyle!.Trim();

        return InferSocialCardStyle(badge, route);
    }

    private static string ResolveSocialCardVariant(
        SiteSpec spec,
        ContentItem item,
        string styleKey,
        string route,
        SocialCardThemeSpec? cardTheme)
    {
        var variantOverride = FirstNonEmpty(
            GetMetaString(item.Meta, "social_card_variant"),
            GetMetaString(item.Meta, "social.variant"));
        if (!string.IsNullOrWhiteSpace(variantOverride))
            return variantOverride!.Trim();

        var collection = item.Collection?.Trim();
        if (!string.IsNullOrWhiteSpace(collection) &&
            TryResolveCollectionCardPreset(spec.Social?.GeneratedCardVariantsByCollection, collection!, out var collectionVariant))
            return collectionVariant;

        if (!string.IsNullOrWhiteSpace(cardTheme?.Variant))
            return cardTheme.Variant!.Trim();

        if (!string.IsNullOrWhiteSpace(spec.Social?.GeneratedCardVariant))
            return spec.Social.GeneratedCardVariant!.Trim();

        return InferSocialCardVariant(item, styleKey, route);
    }

    private static string? ResolveSocialCardColorScheme(SiteSpec spec, ContentItem item, SocialCardThemeSpec? cardTheme)
    {
        var colorSchemeOverride = FirstNonEmpty(
            GetMetaString(item.Meta, "social_card_color_scheme"),
            GetMetaString(item.Meta, "social.color_scheme"));
        if (!string.IsNullOrWhiteSpace(colorSchemeOverride))
            return colorSchemeOverride!.Trim();

        var collection = item.Collection?.Trim();
        if (!string.IsNullOrWhiteSpace(collection) &&
            TryResolveCollectionCardPreset(spec.Social?.GeneratedCardColorSchemesByCollection, collection!, out var collectionScheme))
            return collectionScheme;

        if (!string.IsNullOrWhiteSpace(cardTheme?.ColorScheme))
            return cardTheme.ColorScheme!.Trim();

        if (!string.IsNullOrWhiteSpace(spec.Social?.GeneratedCardColorScheme))
            return spec.Social.GeneratedCardColorScheme!.Trim();

        return null;
    }

    private static bool ResolveSocialCardAllowRemoteMediaFetch(SiteSpec spec, ContentItem item, SocialCardThemeSpec? cardTheme)
    {
        if (TryGetMetaBool(item.Meta, "social_card_allow_remote_media_fetch", out var explicitValue))
            return explicitValue;

        if (TryGetMetaBool(item.Meta, "social.allow_remote_media_fetch", out explicitValue))
            return explicitValue;

        if (TryGetMetaBool(item.Meta, "social_card_allow_remote_media", out explicitValue))
            return explicitValue;

        if (TryGetMetaBool(item.Meta, "social.allow_remote_media", out explicitValue))
            return explicitValue;

        if (cardTheme?.AllowRemoteMediaFetch is not null)
            return cardTheme.AllowRemoteMediaFetch.Value;

        return spec.Social?.GeneratedCardAllowRemoteMediaFetch ?? false;
    }

    private static IReadOnlyList<SocialCardMetricSpec> ResolveSocialCardMetrics(
        SiteSpec spec,
        ContentItem item,
        SocialCardThemeSpec? cardTheme)
    {
        if (TryGetMetaValue(item.Meta, "social_card_metrics", out var metaMetrics) ||
            TryGetMetaValue(item.Meta, "social.metrics", out metaMetrics))
        {
            return NormalizeSocialCardMetrics(ParseSocialCardMetrics(metaMetrics));
        }

        if (cardTheme?.Metrics is { Count: > 0 })
            return NormalizeSocialCardMetrics(cardTheme.Metrics);

        if (spec.Social?.GeneratedCardMetrics is { Count: > 0 })
            return NormalizeSocialCardMetrics(spec.Social.GeneratedCardMetrics);

        return InferSocialCardMetrics(item);
    }

    private static IReadOnlyList<SocialCardMetricSpec> InferSocialCardMetrics(ContentItem item)
    {
        var metrics = new List<SocialCardMetricSpec>();

        AddMetricFromMeta(metrics, item.Meta, "project_github_stars", "star", "Stars");
        AddMetricFromMeta(metrics, item.Meta, "project_github_forks", "fork", "Forks");
        AddMetricFromMeta(metrics, item.Meta, "project_github_open_issues", "issue", "Issues");
        AddMetricFromMeta(metrics, item.Meta, "project_downloads_total", "download", "Downloads");
        AddMetricFromMeta(metrics, item.Meta, "project_psgallery_downloads", "download", "Downloads");
        AddMetricFromMeta(metrics, item.Meta, "project_release_latest_tag", "tag", "Release", abbreviateNumber: false);
        AddMetricFromMeta(metrics, item.Meta, "project_github_language", "code", "Language", abbreviateNumber: false);

        if (TryGetMetaBool(item.Meta, "project_surface_docs", out var docs) && docs)
            metrics.Add(new SocialCardMetricSpec { Icon = "book", Value = "yes", Label = "Docs" });
        if (TryGetMetaBool(item.Meta, "project_surface_examples", out var examples) && examples)
            metrics.Add(new SocialCardMetricSpec { Icon = "code", Value = "yes", Label = "Examples" });

        return NormalizeSocialCardMetrics(metrics);
    }

    private static void AddMetricFromMeta(
        List<SocialCardMetricSpec> metrics,
        Dictionary<string, object?>? meta,
        string key,
        string icon,
        string label,
        bool abbreviateNumber = true)
    {
        if (metrics.Count >= 5 || meta is null || !TryGetMetaValue(meta, key, out var raw) || raw is null)
            return;

        var value = FormatSocialCardMetricValue(raw, abbreviateNumber);
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
            return;

        if (metrics.Any(metric => string.Equals(metric.Label, label, StringComparison.OrdinalIgnoreCase)))
            return;

        metrics.Add(new SocialCardMetricSpec
        {
            Icon = icon,
            Value = value,
            Label = label
        });
    }

    private static string FormatSocialCardMetricValue(object raw, bool abbreviateNumber)
    {
        if (!abbreviateNumber)
            return SocialCardMetricNormalizer.Trim(Convert.ToString(raw), 16);

        if (raw is int i)
            return AbbreviateSocialCardNumber(i);
        if (raw is long l)
            return AbbreviateSocialCardNumber(l);
        if (raw is double d)
            return AbbreviateSocialCardNumber((long)Math.Round(d));
        if (raw is decimal m)
            return AbbreviateSocialCardNumber((long)Math.Round(m));

        var text = Convert.ToString(raw);
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return AbbreviateSocialCardNumber(number);

        return SocialCardMetricNormalizer.Trim(text, 16);
    }

    private static string AbbreviateSocialCardNumber(long value)
    {
        var absolute = Math.Abs(value);
        if (absolute >= 1_000_000)
            return (value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (absolute >= 1_000)
            return (value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<SocialCardMetricSpec> ParseSocialCardMetrics(object? value)
    {
        if (value is null)
            return Array.Empty<SocialCardMetricSpec>();

        if (value is IEnumerable<SocialCardMetricSpec> typedMetrics)
            return typedMetrics.ToList();

        if (value is System.Text.Json.JsonElement element)
            return ParseSocialCardMetrics(ConvertJsonElement(element));

        if (value is string text)
            return ParseSocialCardMetricsString(text);

        if (value is IDictionary dictionary)
        {
            var metric = ParseSocialCardMetricMap(dictionary);
            return metric is null ? Array.Empty<SocialCardMetricSpec>() : [metric];
        }

        if (value is IEnumerable enumerable)
        {
            var metrics = new List<SocialCardMetricSpec>();
            foreach (var item in enumerable)
            {
                metrics.AddRange(ParseSocialCardMetrics(item));
                if (metrics.Count >= 5)
                    break;
            }

            return metrics;
        }

        return Array.Empty<SocialCardMetricSpec>();
    }

    private static IReadOnlyList<SocialCardMetricSpec> ParseSocialCardMetricsString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<SocialCardMetricSpec>();

        var metrics = new List<SocialCardMetricSpec>();
        foreach (var segment in value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                metrics.Add(new SocialCardMetricSpec { Value = parts[0], Label = parts[1], Icon = parts.Length > 2 ? parts[2] : null });
                continue;
            }

            var equalsIndex = segment.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex > 0)
                metrics.Add(new SocialCardMetricSpec { Label = segment[..equalsIndex], Value = segment[(equalsIndex + 1)..] });
        }

        return metrics;
    }

    private static SocialCardMetricSpec? ParseSocialCardMetricMap(IDictionary dictionary)
    {
        static string? Read(IDictionary map, params string[] keys)
        {
            foreach (var key in keys)
            {
                foreach (DictionaryEntry entry in map)
                {
                    if (entry.Key is not null &&
                        string.Equals(Convert.ToString(entry.Key), key, StringComparison.OrdinalIgnoreCase))
                    {
                        return Convert.ToString(entry.Value);
                    }
                }
            }

            return null;
        }

        var metric = new SocialCardMetricSpec
        {
            Icon = Read(dictionary, "icon", "glyph"),
            Value = Read(dictionary, "value", "number", "count"),
            Label = Read(dictionary, "label", "name", "title"),
            Color = Read(dictionary, "color", "accent")
        };

        return string.IsNullOrWhiteSpace(metric.Value) && string.IsNullOrWhiteSpace(metric.Label)
            ? null
            : metric;
    }

    private static IReadOnlyList<SocialCardMetricSpec> NormalizeSocialCardMetrics(IEnumerable<SocialCardMetricSpec>? metrics)
    {
        return SocialCardMetricNormalizer.Normalize(metrics);
    }

    private static string ComputeSocialCardMetricsFingerprint(IReadOnlyList<SocialCardMetricSpec> metrics)
    {
        if (metrics.Count == 0)
            return string.Empty;

        var canonical = metrics.Select(static metric => new
        {
            metric.Icon,
            metric.Value,
            metric.Label,
            metric.Color
        });
        return ComputeSocialHash(System.Text.Json.JsonSerializer.Serialize(canonical));
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
        if (combined.Contains("home", StringComparison.Ordinal))
            return "home";
        if (combined.Contains("api", StringComparison.Ordinal))
            return "api";
        if (combined.Contains("doc", StringComparison.Ordinal))
            return "docs";
        if (combined.Contains("example", StringComparison.Ordinal) ||
            combined.Contains("sample", StringComparison.Ordinal))
            return "examples";
        if (combined.Contains("download", StringComparison.Ordinal) ||
            combined.Contains("package", StringComparison.Ordinal) ||
            combined.Contains("gallery", StringComparison.Ordinal))
            return "downloads";
        if (combined.Contains("release", StringComparison.Ordinal) ||
            combined.Contains("changelog", StringComparison.Ordinal))
            return "release";
        if (combined.Contains("rss", StringComparison.Ordinal) ||
            combined.Contains("atom", StringComparison.Ordinal) ||
            combined.Contains("feed", StringComparison.Ordinal))
            return "feed";
        if (combined.Contains("benchmark", StringComparison.Ordinal) ||
            combined.Contains("performance", StringComparison.Ordinal))
            return "benchmark";
        if (combined.Contains("qr", StringComparison.Ordinal) ||
            combined.Contains("barcode", StringComparison.Ordinal))
            return "code";
        if (combined.Contains("contact", StringComparison.Ordinal) ||
            combined.Contains("support", StringComparison.Ordinal))
            return "contact";
        if (combined.Contains("blog", StringComparison.Ordinal) ||
            combined.Contains("post", StringComparison.Ordinal) ||
            combined.Contains("news", StringComparison.Ordinal) ||
            combined.Contains("article", StringComparison.Ordinal))
            return "blog";
        return "default";
    }

    private static string InferSocialCardVariant(ContentItem item, string styleKey, string route)
    {
        if (item.Kind == PageKind.Home)
            return "spotlight";

        var normalizedRoute = NormalizePath(route).Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedRoute) ||
            string.Equals(normalizedRoute, "index", StringComparison.OrdinalIgnoreCase))
            return "spotlight";

        return styleKey switch
        {
            "home" => "spotlight",
            "docs" => "shelf",
            "api" => "reference",
            "blog" => "editorial",
            "contact" => "connect",
            "examples" => "code",
            "downloads" => "metrics",
            "release" => "timeline",
            "feed" => "feed",
            "benchmark" => "metrics",
            "code" => "code",
            _ => "product"
        };
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
        catch (Exception)
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

        // Docs pages default to the site-level social image unless explicitly overridden
        // with front matter (meta.social_card: true/false).
        if (string.Equals(item.Collection, "docs", StringComparison.OrdinalIgnoreCase))
            return false;

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
        catch (Exception)
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

    private static string ResolveSocialCardLogoCandidate(SiteSpec spec, ContentItem item, SocialCardThemeSpec? cardTheme)
    {
        return FirstNonEmpty(
                   GetMetaString(item.Meta, "social_card_logo"),
                   GetMetaString(item.Meta, "social.logo"),
                   cardTheme?.Logo,
                   spec.Social?.GeneratedCardLogo,
                   spec.StructuredData?.OrganizationLogo) ??
               string.Empty;
    }

    private static string ResolveSocialCardInlineImageCandidate(ContentItem item)
    {
        var explicitCardImage = FirstNonEmpty(
            GetMetaString(item.Meta, "social_card_image"),
            GetMetaString(item.Meta, "social.card_image"),
            GetMetaString(item.Meta, "social_card_media"),
            GetMetaString(item.Meta, "social.media"),
            GetMetaString(item.Meta, "social_image"),
            GetMetaString(item.Meta, "social.image"),
            GetMetaString(item.Meta, "cover_image"),
            GetMetaString(item.Meta, "thumbnail"),
            GetMetaString(item.Meta, "image"));

        if (!string.IsNullOrWhiteSpace(explicitCardImage))
            return explicitCardImage!;

        if (!IsEditorialCollection(item.Collection))
            return string.Empty;

        return TryExtractFirstBodyImageCandidate(item.SourcePath);
    }

    private static bool ShouldRenderSocialCardInlineImage(ContentItem item, string styleKey, string variantKey, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        if (TryGetMetaBool(item.Meta, "social_card_image_inline", out var explicitInline))
            return explicitInline;

        if (TryGetMetaBool(item.Meta, "social.image_inline", out explicitInline))
            return explicitInline;

        if (string.Equals(variantKey, "inline-image", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(styleKey, "blog", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ResolveSocialCardAssetDataUri(SiteSpec spec, ContentItem item, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        var trimmed = candidate.Trim();
        if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        var sourcePath = TryResolveSocialCardAssetPath(spec, item, trimmed);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return string.Empty;

        var mimeType = Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(mimeType))
            return string.Empty;

        return $"data:{mimeType};base64,{Convert.ToBase64String(File.ReadAllBytes(sourcePath))}";
    }

    internal static string TryResolveSocialCardAssetPath(SiteSpec spec, ContentItem item, string candidate)
    {
        var sourceDir = Path.GetDirectoryName(item.SourcePath);
        var allowedRoots = BuildAllowedSocialCardAssetRoots(spec, sourceDir);

        if (Path.IsPathRooted(candidate) && File.Exists(candidate))
        {
            var fullCandidate = Path.GetFullPath(candidate);
            return IsSocialCardAssetPathWithinAllowedRoots(allowedRoots, fullCandidate)
                ? fullCandidate
                : string.Empty;
        }

        var normalizedCandidate = candidate.Replace('\\', '/').Trim();
        foreach (var resource in item.Resources ?? Array.Empty<PageResource>())
        {
            var resourcePath = resource.RelativePath?.Replace('\\', '/').TrimStart('/') ?? string.Empty;
            if (string.Equals(resourcePath, normalizedCandidate.TrimStart('/'), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resource.Name, Path.GetFileName(normalizedCandidate), StringComparison.OrdinalIgnoreCase))
            {
                var resourceSourcePath = Path.GetFullPath(resource.SourcePath);
                return IsSocialCardAssetPathWithinAllowedRoots(allowedRoots, resourceSourcePath)
                    ? resourceSourcePath
                    : string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceDir))
        {
            var relativeCandidate = Path.GetFullPath(Path.Combine(sourceDir, normalizedCandidate.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(relativeCandidate) &&
                IsSocialCardAssetPathWithinAllowedRoots(allowedRoots, relativeCandidate))
                return relativeCandidate;
        }

        foreach (var root in allowedRoots)
        {
            var combined = Path.GetFullPath(Path.Combine(root, normalizedCandidate.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(combined) &&
                IsSocialCardAssetPathWithinAllowedRoots(allowedRoots, combined))
                return combined;
        }

        return string.Empty;
    }

    private static List<string> BuildAllowedSocialCardAssetRoots(SiteSpec spec, string? sourceDir)
    {
        var roots = new List<string>();
        var rootPath = BuildRootPathScope.Value;
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            roots.Add(Path.GetFullPath(rootPath));
            roots.Add(Path.GetFullPath(Path.Combine(rootPath, "static")));
            if (!string.IsNullOrWhiteSpace(spec.ContentRoot))
                roots.Add(Path.GetFullPath(Path.Combine(rootPath, spec.ContentRoot)));
            if (spec.ContentRoots is { Length: > 0 })
            {
                foreach (var contentRoot in spec.ContentRoots.Where(static value => !string.IsNullOrWhiteSpace(value)))
                    roots.Add(Path.GetFullPath(Path.Combine(rootPath, contentRoot)));
            }
        }
        else if (!string.IsNullOrWhiteSpace(sourceDir))
        {
            roots.Add(Path.GetFullPath(sourceDir));
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSocialCardAssetPathWithinAllowedRoots(IReadOnlyCollection<string> allowedRoots, string candidatePath)
    {
        if (allowedRoots is null || allowedRoots.Count == 0)
            return false;

        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (IsPathWithinRoot(Path.GetFullPath(root), candidatePath))
                return true;
        }

        return false;
    }

    private static string TryExtractFirstBodyImageCandidate(string? sourcePath)
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
                return NormalizeSocialCardMediaCandidate(ExtractMarkdownImageTarget(markdownMatch.Groups["target"].Value));

            var htmlMatch = HtmlImageRegex.Match(scrubbed);
            if (htmlMatch.Success)
                return NormalizeSocialCardMediaCandidate(htmlMatch.Groups["src"].Value);
        }
        catch (Exception)
        {
            // Ignore extraction errors and fall back to generated/default image.
        }

        return string.Empty;
    }

    private static string NormalizeSocialCardMediaCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        var trimmed = candidate.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/", StringComparison.Ordinal) ||
            trimmed.StartsWith("./", StringComparison.Ordinal) ||
            trimmed.StartsWith("../", StringComparison.Ordinal))
            return trimmed;

        return trimmed;
    }
}
