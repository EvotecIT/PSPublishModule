using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static LocalizationRuntime BuildLocalizationRuntime(SiteSpec spec, ContentItem page, IReadOnlyList<ContentItem> allItems)
    {
        var localization = ResolveLocalizationConfig(spec);
        var currentCode = ResolveEffectiveLanguageCode(localization, page.Language);
        var currentPath = string.IsNullOrWhiteSpace(page.OutputPath)
            ? "/"
            : ResolvePublicRouteForLanguage(spec, localization, page.OutputPath, currentCode);

        var languages = new List<LocalizationLanguageRuntime>();
        foreach (var language in localization.Languages)
        {
            var route = ResolveLocalizedPageUrl(spec, localization, page, allItems, language.Code, currentCode);
            var localRoute = string.IsNullOrWhiteSpace(route)
                ? currentPath
                : ResolvePublicRouteForLanguage(spec, localization, route, language.Code);
            var publicRoute = route;
            if (!string.IsNullOrWhiteSpace(route) &&
                !route.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !route.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                publicRoute = ResolvePublicRouteForLanguage(spec, localization, route, language.Code);
            }

            var url = string.IsNullOrWhiteSpace(language.BaseUrl)
                ? publicRoute
                : ResolveAbsoluteUrl(language.BaseUrl, publicRoute);
            languages.Add(new LocalizationLanguageRuntime
            {
                Code = language.Code,
                Label = language.Label,
                Prefix = language.Prefix,
                BaseUrl = language.BaseUrl,
                RenderAtRoot = language.RenderAtRoot,
                IsDefault = language.IsDefault,
                IsCurrent = language.Code.Equals(currentCode, StringComparison.OrdinalIgnoreCase),
                Url = string.IsNullOrWhiteSpace(url) ? currentPath : url,
                LocalUrl = localRoute
            });
        }

        if (languages.Count == 0)
        {
            languages.Add(new LocalizationLanguageRuntime
            {
                Code = currentCode,
                Label = currentCode,
                Prefix = currentCode,
                BaseUrl = NormalizeAbsoluteBaseUrl(spec.BaseUrl),
                RenderAtRoot = true,
                IsDefault = true,
                IsCurrent = true,
                Url = currentPath,
                LocalUrl = currentPath
            });
        }

        var current = languages.FirstOrDefault(l => l.IsCurrent) ?? languages[0];
        return new LocalizationRuntime
        {
            Enabled = localization.Enabled,
            Current = current,
            Languages = languages.ToArray()
        };
    }

    private static string ResolveLocalizedPageUrl(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        ContentItem page,
        IReadOnlyList<ContentItem> allItems,
        string targetLanguage,
        string currentLanguage)
    {
        var resolvedTargetLanguage = ResolveEffectiveLanguageCode(localization, targetLanguage);
        var resolvedCurrentLanguage = ResolveEffectiveLanguageCode(localization, currentLanguage);

        if (TryResolveExplicitLocalizedRoute(spec, localization, page, resolvedTargetLanguage, out var explicitRoute))
            return explicitRoute;

        if (allItems.Count > 0 && !string.IsNullOrWhiteSpace(page.TranslationKey))
        {
            var translated = allItems
                .Where(i => !i.Draft)
                .Where(i => string.Equals(i.ProjectSlug ?? string.Empty, page.ProjectSlug ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Where(i => !string.IsNullOrWhiteSpace(i.TranslationKey))
                .FirstOrDefault(i =>
                    i.TranslationKey!.Equals(page.TranslationKey, StringComparison.OrdinalIgnoreCase) &&
                    ResolveEffectiveLanguageCode(localization, i.Language).Equals(resolvedTargetLanguage, StringComparison.OrdinalIgnoreCase));
            if (translated is not null)
                return translated.OutputPath;

            if (localization.FallbackToDefaultLanguage &&
                !resolvedTargetLanguage.Equals(localization.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            {
                var fallback = allItems
                    .Where(i => !i.Draft)
                    .Where(i => string.Equals(i.ProjectSlug ?? string.Empty, page.ProjectSlug ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    .Where(i => !string.IsNullOrWhiteSpace(i.TranslationKey))
                    .FirstOrDefault(i =>
                        i.TranslationKey!.Equals(page.TranslationKey, StringComparison.OrdinalIgnoreCase) &&
                        ResolveEffectiveLanguageCode(localization, i.Language).Equals(localization.DefaultLanguage, StringComparison.OrdinalIgnoreCase));
                if (fallback is not null)
                {
                    if (localization.MaterializeFallbackPages)
                    {
                        if (CollectionSupportsFallbackLanguage(spec, localization, page.Collection, resolvedTargetLanguage))
                        {
                            var fallbackBaseRoute = StripLanguagePrefix(localization, fallback.OutputPath);
                            return ApplyLanguagePrefixToRoute(spec, fallbackBaseRoute, resolvedTargetLanguage);
                        }

                        return ResolveFallbackDefaultLanguageRoute(spec, localization, resolvedTargetLanguage, fallback.OutputPath);
                    }
                    return ResolveFallbackDefaultLanguageRoute(spec, localization, resolvedTargetLanguage, fallback.OutputPath);
                }
            }
        }

        var baseRoute = StripLanguagePrefix(localization, page.OutputPath);
        if (string.IsNullOrWhiteSpace(baseRoute))
            baseRoute = "/";

        if (!resolvedTargetLanguage.Equals(resolvedCurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            if (localization.FallbackToDefaultLanguage)
            {
                // When fallback pages are materialized, keep the requested language route
                // (for example /pl/projects/) while canonical points to default-language source.
                if (localization.MaterializeFallbackPages &&
                    CollectionSupportsFallbackLanguage(spec, localization, page.Collection, resolvedTargetLanguage))
                    return ApplyLanguagePrefixToRoute(spec, baseRoute, resolvedTargetLanguage);

                return ResolveFallbackDefaultLanguageRoute(spec, localization, resolvedTargetLanguage, baseRoute);
            }
            return ApplyLanguagePrefixToRoute(spec, baseRoute, resolvedTargetLanguage);
        }

        return ApplyLanguagePrefixToRoute(spec, baseRoute, resolvedCurrentLanguage);
    }

    private static bool TryResolveExplicitLocalizedRoute(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        ContentItem page,
        string targetLanguage,
        out string route)
    {
        route = string.Empty;
        if (page.Meta is null || page.Meta.Count == 0 || string.IsNullOrWhiteSpace(targetLanguage))
            return false;

        foreach (var key in BuildLocalizedRouteMetaKeys(targetLanguage))
        {
            if (!TryGetMetaString(page.Meta, key, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            var trimmed = raw.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                route = trimmed;
                return true;
            }

            if (!trimmed.StartsWith("/", StringComparison.Ordinal))
                trimmed = "/" + trimmed.TrimStart('/');

            route = EnsureTrailingSlash(trimmed, spec.TrailingSlash);
            return true;
        }

        return false;
    }

    private static IEnumerable<string> BuildLocalizedRouteMetaKeys(string languageCode)
    {
        var normalized = NormalizeLanguageToken(languageCode);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalized };
        if (normalized.Contains('-', StringComparison.Ordinal))
            variants.Add(normalized.Replace('-', '_'));
        if (normalized.Contains('_', StringComparison.Ordinal))
            variants.Add(normalized.Replace('_', '-'));

        foreach (var variant in variants)
        {
            yield return $"i18n.routes.{variant}";
            yield return $"i18n.route.{variant}";
            yield return $"i18n.urls.{variant}";
            yield return $"i18n.url.{variant}";
            yield return $"translations.{variant}.route";
            yield return $"translations.{variant}.url";
            yield return $"translations.{variant}.path";
        }
    }

    private static string ResolveItemLanguage(
        SiteSpec spec,
        string relativePath,
        FrontMatter? matter,
        out string localizedRelativePath,
        out string localizedRelativeDir)
    {
        var localization = ResolveLocalizationConfig(spec);
        var normalizedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
        localizedRelativePath = normalizedRelativePath;
        localizedRelativeDir = NormalizePath(Path.GetDirectoryName(normalizedRelativePath) ?? string.Empty);

        if (!localization.Enabled)
            return ResolveEffectiveLanguageCode(localization, ResolveLanguageFromFrontMatter(matter));

        string? pathLanguage = null;
        if (localization.DetectFromPath && TryExtractLeadingSegment(normalizedRelativePath, out var segment, out var remainder))
        {
            if (TryResolveConfiguredLanguage(localization, segment, matchByPrefix: true, out var fromPath))
            {
                pathLanguage = fromPath;
                localizedRelativePath = remainder;
                localizedRelativeDir = NormalizePath(Path.GetDirectoryName(remainder) ?? string.Empty);
            }
        }

        var frontMatterLanguage = ResolveLanguageFromFrontMatter(matter);
        if (TryResolveConfiguredLanguage(localization, frontMatterLanguage, matchByPrefix: true, out var resolvedFrontMatterLanguage))
            return resolvedFrontMatterLanguage;

        return !string.IsNullOrWhiteSpace(pathLanguage)
            ? pathLanguage
            : localization.DefaultLanguage;
    }

    private static string ResolveLanguageFromFrontMatter(FrontMatter? matter)
    {
        if (!string.IsNullOrWhiteSpace(matter?.Language))
            return NormalizeLanguageToken(matter.Language);

        if (matter?.Meta is null || matter.Meta.Count == 0)
            return string.Empty;

        if (TryGetMetaString(matter.Meta, "language", out var language))
            return NormalizeLanguageToken(language);
        if (TryGetMetaString(matter.Meta, "lang", out language))
            return NormalizeLanguageToken(language);
        if (TryGetMetaString(matter.Meta, "i18n.language", out language))
            return NormalizeLanguageToken(language);
        if (TryGetMetaString(matter.Meta, "i18n.lang", out language))
            return NormalizeLanguageToken(language);

        return string.Empty;
    }

    private static string ResolveTranslationKey(FrontMatter? matter, string? collectionName, string localizedRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(matter?.TranslationKey))
            return matter.TranslationKey.Trim();

        if (matter?.Meta is not null)
        {
            if (TryGetMetaString(matter.Meta, "translation_key", out var translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
            if (TryGetMetaString(matter.Meta, "translationKey", out translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
            if (TryGetMetaString(matter.Meta, "translation.key", out translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
            if (TryGetMetaString(matter.Meta, "i18n.key", out translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
            if (TryGetMetaString(matter.Meta, "i18n.translation_key", out translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
            if (TryGetMetaString(matter.Meta, "i18n.translationKey", out translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
            if (TryGetMetaString(matter.Meta, "i18n.group", out translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
        }

        var collection = string.IsNullOrWhiteSpace(collectionName)
            ? "content"
            : collectionName.Trim().ToLowerInvariant();
        var path = NormalizePath(Path.ChangeExtension(localizedRelativePath, null) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(path))
            path = "index";
        return $"{collection}:{path.ToLowerInvariant()}";
    }

    private static string ApplyLanguagePrefixToRoute(SiteSpec spec, string route, string? language)
    {
        var localization = ResolveLocalizationConfig(spec);
        if (!localization.Enabled)
            return route;

        var languageCode = ResolveEffectiveLanguageCode(localization, language);
        if (!localization.ByCode.TryGetValue(languageCode, out var languageSpec))
            return route;

        var buildContext = BuildLanguageContextScope.Value;
        if (buildContext is not null &&
            buildContext.LanguageAsRoot &&
            !string.IsNullOrWhiteSpace(buildContext.Language))
        {
            var selectedLanguage = ResolveEffectiveLanguageCode(localization, buildContext.Language);
            if (languageCode.Equals(selectedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                var strippedRoute = StripLanguagePrefix(localization, route);
                return EnsureTrailingSlash(strippedRoute, spec.TrailingSlash);
            }
        }

        if (languageSpec.IsDefault && !localization.PrefixDefaultLanguage)
            return route;

        var prefix = NormalizePath(languageSpec.Prefix);
        if (string.IsNullOrWhiteSpace(prefix))
            return route;

        var stripped = StripLanguagePrefix(localization, route);
        var withoutLeadingSlash = stripped.TrimStart('/');
        var prefixed = string.IsNullOrWhiteSpace(withoutLeadingSlash)
            ? "/" + prefix
            : "/" + prefix + "/" + withoutLeadingSlash;

        return EnsureTrailingSlash(prefixed, spec.TrailingSlash);
    }

    private static string StripLanguagePrefix(ResolvedLocalizationConfig localization, string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return "/";

        var normalized = route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route;
        if (!TryExtractLeadingSegment(normalized.TrimStart('/'), out var segment, out var remainder))
            return normalized;

        var token = NormalizeLanguageToken(segment);
        if (string.IsNullOrWhiteSpace(token))
            return normalized;

        if (!localization.ByPrefix.ContainsKey(token))
            return normalized;

        return string.IsNullOrWhiteSpace(remainder) ? "/" : "/" + remainder;
    }

    private static bool TryExtractLeadingSegment(string value, out string segment, out string remainder)
    {
        segment = string.Empty;
        remainder = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var slash = normalized.IndexOf('/');
        if (slash < 0)
        {
            segment = normalized;
            remainder = string.Empty;
            return true;
        }

        segment = normalized.Substring(0, slash);
        remainder = normalized[(slash + 1)..];
        return true;
    }

    private static ResolvedLocalizationConfig ResolveLocalizationConfig(SiteSpec spec)
    {
        var localizationSpec = spec.Localization;
        var defaultLanguage = NormalizeLanguageToken(localizationSpec?.DefaultLanguage);
        if (string.IsNullOrWhiteSpace(defaultLanguage))
            defaultLanguage = "en";

        var entries = new List<ResolvedLocalizationLanguage>();
        if (localizationSpec?.Languages is { Length: > 0 })
        {
            foreach (var language in localizationSpec.Languages)
            {
                if (language is null || language.Disabled)
                    continue;

                var code = NormalizeLanguageToken(language.Code);
                if (string.IsNullOrWhiteSpace(code) || entries.Any(e => e.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var prefix = NormalizePath(string.IsNullOrWhiteSpace(language.Prefix) ? code : language.Prefix);
                if (string.IsNullOrWhiteSpace(prefix))
                    prefix = code;

                var languageBaseUrl = NormalizeAbsoluteBaseUrl(language.BaseUrl);

                entries.Add(new ResolvedLocalizationLanguage
                {
                    Code = code,
                    Label = string.IsNullOrWhiteSpace(language.Label) ? code : language.Label.Trim(),
                    Prefix = prefix,
                    BaseUrl = languageBaseUrl,
                    RenderAtRoot = language.RenderAtRoot,
                    IsDefault = language.Default
                });
            }
        }

        if (entries.Count == 0)
        {
            entries.Add(new ResolvedLocalizationLanguage
            {
                Code = defaultLanguage,
                Label = defaultLanguage,
                Prefix = defaultLanguage,
                RenderAtRoot = true,
                IsDefault = true
            });
        }

        var explicitDefault = entries.FirstOrDefault(e => e.IsDefault);
        if (explicitDefault is null)
            explicitDefault = entries.FirstOrDefault(e => e.Code.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase)) ?? entries[0];

        foreach (var entry in entries)
            entry.IsDefault = entry.Code.Equals(explicitDefault.Code, StringComparison.OrdinalIgnoreCase);

        var byCode = new Dictionary<string, ResolvedLocalizationLanguage>(StringComparer.OrdinalIgnoreCase);
        var byPrefix = new Dictionary<string, ResolvedLocalizationLanguage>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!byCode.ContainsKey(entry.Code))
                byCode[entry.Code] = entry;

            var normalizedPrefix = NormalizeLanguageToken(entry.Prefix);
            if (!string.IsNullOrWhiteSpace(normalizedPrefix) && !byPrefix.ContainsKey(normalizedPrefix))
                byPrefix[normalizedPrefix] = entry;
        }

        return new ResolvedLocalizationConfig
        {
            Enabled = localizationSpec?.Enabled == true && byCode.Count > 0,
            DetectFromPath = localizationSpec?.DetectFromPath ?? true,
            PrefixDefaultLanguage = localizationSpec?.PrefixDefaultLanguage == true,
            FallbackToDefaultLanguage = localizationSpec?.FallbackToDefaultLanguage == true,
            MaterializeFallbackPages = localizationSpec?.MaterializeFallbackPages == true,
            DefaultLanguage = explicitDefault.Code,
            Languages = entries.ToArray(),
            ByCode = byCode,
            ByPrefix = byPrefix
        };
    }

    private static string ResolveEffectiveLanguageCode(ResolvedLocalizationConfig localization, string? language)
    {
        if (TryResolveConfiguredLanguage(localization, language, matchByPrefix: true, out var resolved))
            return resolved;
        return localization.DefaultLanguage;
    }

    private static bool TryResolveConfiguredLanguage(
        ResolvedLocalizationConfig localization,
        string? language,
        bool matchByPrefix,
        out string resolved)
    {
        resolved = string.Empty;
        var token = NormalizeLanguageToken(language);
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (localization.ByCode.TryGetValue(token, out var byCode))
        {
            resolved = byCode.Code;
            return true;
        }

        if (matchByPrefix && localization.ByPrefix.TryGetValue(token, out var byPrefix))
        {
            resolved = byPrefix.Code;
            return true;
        }

        return false;
    }

    private static string NormalizeLanguageToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Trim().Replace('_', '-').Trim('/').ToLowerInvariant();
    }

    private static string? NormalizeAbsoluteBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return null;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return null;
        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}";
    }

    private static string ResolveLanguageBaseUrl(SiteSpec spec, ResolvedLocalizationConfig localization, string? languageCode)
    {
        var normalizedSiteBase = NormalizeAbsoluteBaseUrl(spec.BaseUrl);
        if (!localization.Enabled)
            return normalizedSiteBase ?? spec.BaseUrl;

        var effectiveLanguage = ResolveEffectiveLanguageCode(localization, languageCode);
        if (localization.ByCode.TryGetValue(effectiveLanguage, out var language) && !string.IsNullOrWhiteSpace(language.BaseUrl))
            return language.BaseUrl!;

        return normalizedSiteBase ?? spec.BaseUrl;
    }

    private static string ResolveAbsoluteLanguageRoute(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        string? languageCode,
        string route)
    {
        if (string.IsNullOrWhiteSpace(route) || IsAbsoluteHttpUrl(route))
            return route;

        var effectiveLanguage = ResolveEffectiveLanguageCode(localization, languageCode);
        var publicRoute = ResolvePublicRouteForLanguage(spec, localization, route, effectiveLanguage);
        var baseUrl = ResolveLanguageBaseUrl(spec, localization, effectiveLanguage);
        return string.IsNullOrWhiteSpace(baseUrl)
            ? publicRoute
            : ResolveAbsoluteUrl(baseUrl, publicRoute);
    }

    private static string ResolveFallbackDefaultLanguageRoute(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        string targetLanguage,
        string route)
    {
        if (!TargetLanguageHasAbsoluteBaseUrl(localization, targetLanguage))
            return ResolvePublicRouteForLanguage(spec, localization, route, localization.DefaultLanguage);

        return ResolveAbsoluteLanguageRoute(
            spec,
            localization,
            localization.DefaultLanguage,
            route);
    }

    private static bool TargetLanguageHasAbsoluteBaseUrl(ResolvedLocalizationConfig localization, string targetLanguage)
    {
        var effectiveLanguage = ResolveEffectiveLanguageCode(localization, targetLanguage);
        return localization.ByCode.TryGetValue(effectiveLanguage, out var language) &&
               !string.IsNullOrWhiteSpace(language.BaseUrl);
    }

    private static bool ShouldRenderLanguageAtRoot(ResolvedLocalizationConfig localization, string? languageCode)
    {
        if (!localization.Enabled)
            return true;

        var effectiveLanguage = ResolveEffectiveLanguageCode(localization, languageCode);
        if (!localization.ByCode.TryGetValue(effectiveLanguage, out var language))
            return false;

        return language.RenderAtRoot || (language.IsDefault && !localization.PrefixDefaultLanguage);
    }

    private static string ResolvePublicRouteForLanguage(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        string? route,
        string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(route))
            return "/";

        if (IsAbsoluteHttpUrl(route) ||
            route.StartsWith("//", StringComparison.Ordinal) ||
            IsNonRouteReference(route))
            return route;

        var normalizedRoute = string.IsNullOrWhiteSpace(route)
            ? "/"
            : route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route.TrimStart('/');

        if (!ShouldRenderLanguageAtRoot(localization, languageCode))
            return EnsureTrailingSlash(normalizedRoute, spec.TrailingSlash);

        var stripped = StripLanguagePrefix(localization, normalizedRoute);
        var publicRoute = NormalizeRootNotFoundPublicRoute(stripped);
        if (publicRoute.StartsWith("/404.html", StringComparison.OrdinalIgnoreCase))
            return publicRoute;

        return EnsureTrailingSlash(publicRoute, spec.TrailingSlash);
    }

    private static string NormalizeRootNotFoundPublicRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return route;

        var suffixIndex = route.IndexOfAny(new[] { '?', '#' });
        var path = suffixIndex >= 0 ? route.Substring(0, suffixIndex) : route;
        var suffix = suffixIndex >= 0 ? route.Substring(suffixIndex) : string.Empty;
        var normalizedPath = NormalizePath(path);
        return normalizedPath.Equals("404", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Equals("404.html", StringComparison.OrdinalIgnoreCase)
            ? "/404.html" + suffix
            : route;
    }

    private static string RebaseRouteForSelectedLanguageRootBuild(
        SiteSpec spec,
        string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return route;

        var buildContext = BuildLanguageContextScope.Value;
        if (buildContext is null || !buildContext.LanguageAsRoot || string.IsNullOrWhiteSpace(buildContext.Language))
            return route;

        if (route.StartsWith("//", StringComparison.Ordinal))
            return route;

        var localization = ResolveLocalizationConfig(spec);
        var selectedLanguage = ResolveEffectiveLanguageCode(localization, buildContext.Language);
        if (!ShouldRenderLanguageAtRoot(localization, selectedLanguage))
            return route;

        if (TryRebaseAbsoluteUrlForSelectedLanguageRootBuild(spec, localization, route, selectedLanguage, out var absoluteRebased))
            return absoluteRebased;

        if (IsAbsoluteHttpUrl(route) ||
            route.StartsWith("//", StringComparison.Ordinal) ||
            IsNonRouteReference(route))
            return route;

        return ResolvePublicRouteForLanguage(spec, localization, route, selectedLanguage);
    }

    private static bool TryRebaseAbsoluteUrlForSelectedLanguageRootBuild(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        string route,
        string selectedLanguage,
        out string rebased)
    {
        rebased = route;
        if (!IsAbsoluteHttpUrl(route) ||
            !localization.ByCode.TryGetValue(selectedLanguage, out var language) ||
            string.IsNullOrWhiteSpace(language.BaseUrl))
            return false;

        var normalizedBaseUrl = NormalizeAbsoluteBaseUrl(language.BaseUrl);
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl) ||
            !Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var languageBaseUri) ||
            !Uri.TryCreate(route, UriKind.Absolute, out var absoluteUri))
            return false;

        if (!UrisShareOrigin(absoluteUri, languageBaseUri))
            return false;

        var rebasedPath = ResolvePublicRouteForLanguage(spec, localization, absoluteUri.AbsolutePath, selectedLanguage);
        if (string.IsNullOrWhiteSpace(rebasedPath))
            return false;

        var builder = new UriBuilder(absoluteUri)
        {
            Path = rebasedPath,
            Query = absoluteUri.Query.TrimStart('?'),
            Fragment = absoluteUri.Fragment.TrimStart('#')
        };

        rebased = builder.Uri.AbsoluteUri;
        return true;
    }

    private static bool UrisShareOrigin(Uri left, Uri right)
    {
        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
               left.Port == right.Port;
    }

    private static bool IsNonRouteReference(string value)
    {
        return value.StartsWith("#", StringComparison.Ordinal) ||
               value.StartsWith("?", StringComparison.Ordinal) ||
               value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("blob:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAbsoluteHttpUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ResolveLocalizedLanguagesForCollection(
        ResolvedLocalizationConfig localization,
        CollectionSpec? collection)
    {
        if (!localization.Enabled || localization.Languages.Length == 0)
            return new[] { localization.DefaultLanguage };

        var configured = collection?.LocalizedLanguages;
        if (configured is null || configured.Length == 0)
            configured = collection?.ExpectedTranslationLanguages;

        if (configured is null || configured.Length == 0)
        {
            return localization.Languages
                .Select(static language => language.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static language => language, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var resolved = configured
            .Select(NormalizeLanguageToken)
            .Where(static language => !string.IsNullOrWhiteSpace(language))
            .Where(language => localization.ByCode.ContainsKey(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static language => language, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return resolved.Length > 0
            ? resolved
            : localization.Languages
                .Select(static language => language.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static language => language, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static bool CollectionSupportsLocalizedLanguage(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        string? collectionName,
        string? languageCode)
    {
        var normalizedLanguage = ResolveEffectiveLanguageCode(localization, languageCode);
        var collection = ResolveCollectionSpec(spec, collectionName);
        var supportedLanguages = ResolveLocalizedLanguagesForCollection(localization, collection);
        return supportedLanguages.Contains(normalizedLanguage, StringComparer.OrdinalIgnoreCase);
    }

    private static string[] ResolveFallbackLanguagesForCollection(
        ResolvedLocalizationConfig localization,
        CollectionSpec? collection)
    {
        if (collection?.MaterializeFallbackPages == false)
            return Array.Empty<string>();

        if (!localization.Enabled || localization.Languages.Length == 0)
            return new[] { localization.DefaultLanguage };

        var configured = collection?.FallbackLanguages;
        if (configured is null || configured.Length == 0)
        {
            return localization.Languages
                .Select(static language => language.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static language => language, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var resolved = configured
            .Select(NormalizeLanguageToken)
            .Where(static language => !string.IsNullOrWhiteSpace(language))
            .Where(language => localization.ByCode.ContainsKey(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static language => language, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return resolved.Length > 0
            ? resolved
            : localization.Languages
                .Select(static language => language.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static language => language, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static bool CollectionSupportsFallbackLanguage(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        string? collectionName,
        string? languageCode)
    {
        var normalizedLanguage = ResolveEffectiveLanguageCode(localization, languageCode);
        var collection = ResolveCollectionSpec(spec, collectionName);
        var supportedLanguages = ResolveFallbackLanguagesForCollection(localization, collection);
        return supportedLanguages.Contains(normalizedLanguage, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ResolvedLocalizationConfig
    {
        public bool Enabled { get; init; }
        public bool DetectFromPath { get; init; }
        public bool PrefixDefaultLanguage { get; init; }
        public bool FallbackToDefaultLanguage { get; init; }
        public bool MaterializeFallbackPages { get; init; }
        public string DefaultLanguage { get; init; } = "en";
        public ResolvedLocalizationLanguage[] Languages { get; init; } = Array.Empty<ResolvedLocalizationLanguage>();
        public Dictionary<string, ResolvedLocalizationLanguage> ByCode { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ResolvedLocalizationLanguage> ByPrefix { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ResolvedLocalizationLanguage
    {
        public string Code { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string Prefix { get; init; } = string.Empty;
        public string? BaseUrl { get; init; }
        public bool RenderAtRoot { get; init; }
        public bool IsDefault { get; set; }
    }

    private static VersioningRuntime BuildVersioningRuntime(SiteSpec spec, string? currentPath)
    {
        var versioning = spec.Versioning;
        if (versioning is null || !versioning.Enabled || versioning.Versions is null || versioning.Versions.Length == 0)
            return new VersioningRuntime();

        var versionMap = new Dictionary<string, VersionRuntimeItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in versioning.Versions)
        {
            if (version is null || string.IsNullOrWhiteSpace(version.Name))
                continue;

            if (versionMap.ContainsKey(version.Name))
                continue;

            versionMap[version.Name] = new VersionRuntimeItem
            {
                Name = version.Name.Trim(),
                Label = string.IsNullOrWhiteSpace(version.Label) ? version.Name.Trim() : version.Label.Trim(),
                Url = ResolveVersionUrl(versioning.BasePath, version),
                Default = version.Default,
                Latest = version.Latest,
                Lts = version.Lts,
                Deprecated = version.Deprecated
            };
        }

        var versions = versionMap.Values.ToArray();
        if (versions.Length == 0)
            return new VersioningRuntime();

        var current = ResolveCurrentVersion(versioning.Current, currentPath, versions);
        var latest = versions.FirstOrDefault(v => v.Latest) ?? versions.FirstOrDefault(v => v.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase)) ?? versions[0];
        var lts = versions.FirstOrDefault(v => v.Lts);
        var @default = versions.FirstOrDefault(v => v.Default) ?? versions[0];

        foreach (var version in versions)
            version.IsCurrent = version.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase);

        return new VersioningRuntime
        {
            Enabled = true,
            BasePath = NormalizeVersionBasePath(versioning.BasePath),
            Current = current,
            Latest = latest,
            Lts = lts,
            Default = @default,
            Versions = versions
        };
    }

    private static VersionRuntimeItem ResolveCurrentVersion(string? configuredCurrent, string? currentPath, VersionRuntimeItem[] versions)
    {
        if (!string.IsNullOrWhiteSpace(configuredCurrent))
        {
            var configured = versions.FirstOrDefault(v => v.Name.Equals(configuredCurrent.Trim(), StringComparison.OrdinalIgnoreCase));
            if (configured is not null)
                return configured;
        }

        var normalizedCurrentPath = NormalizeRouteForMatch(currentPath);
        if (!string.IsNullOrWhiteSpace(normalizedCurrentPath))
        {
            var inferred = versions
                .Select(v => new
                {
                    Version = v,
                    Url = NormalizeRouteForMatch(v.Url)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Url) && normalizedCurrentPath.StartsWith(x.Url, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Url.Length)
                .Select(x => x.Version)
                .FirstOrDefault();
            if (inferred is not null)
                return inferred;
        }

        return versions.FirstOrDefault(v => v.Default) ??
               versions.FirstOrDefault(v => v.Latest) ??
               versions[0];
    }

    private static string ResolveVersionUrl(string? basePath, VersionSpec version)
    {
        if (!string.IsNullOrWhiteSpace(version.Url))
            return NormalizeRouteForMatch(version.Url);

        var versionName = version.Name.Trim('/');
        if (string.IsNullOrWhiteSpace(versionName))
            return NormalizeRouteForMatch(basePath);

        var normalizedBasePath = NormalizeVersionBasePath(basePath);
        if (string.IsNullOrWhiteSpace(normalizedBasePath) || normalizedBasePath == "/")
            return NormalizeRouteForMatch("/" + versionName + "/");

        return NormalizeRouteForMatch($"{normalizedBasePath}/{versionName}/");
    }

    private static string NormalizeVersionBasePath(string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return string.Empty;

        var normalized = NormalizeRouteForMatch(basePath);
        return normalized == "/"
            ? "/"
            : normalized.TrimEnd('/');
    }

    private static NavigationVisibilitySpec? CloneVisibility(NavigationVisibilitySpec? visibility)
    {
        if (visibility is null)
            return null;
        return new NavigationVisibilitySpec
        {
            Paths = visibility.Paths?.ToArray() ?? Array.Empty<string>(),
            ExcludePaths = visibility.ExcludePaths?.ToArray() ?? Array.Empty<string>(),
            Collections = visibility.Collections?.ToArray() ?? Array.Empty<string>(),
            Layouts = visibility.Layouts?.ToArray() ?? Array.Empty<string>(),
            Projects = visibility.Projects?.ToArray() ?? Array.Empty<string>()
        };
    }

    private static MenuSectionSpec[] CloneSections(MenuSectionSpec[]? sections)
    {
        if (sections is null || sections.Length == 0)
            return Array.Empty<MenuSectionSpec>();
        return sections.Select(section => new MenuSectionSpec
        {
            Name = section.Name,
            Title = section.Title,
            Description = section.Description,
            CssClass = section.CssClass,
            Items = CloneMenuItems(section.Items),
            Columns = CloneColumns(section.Columns)
        }).ToArray();
    }

    private static MenuColumnSpec[] CloneColumns(MenuColumnSpec[]? columns)
    {
        if (columns is null || columns.Length == 0)
            return Array.Empty<MenuColumnSpec>();
        return columns.Select(column => new MenuColumnSpec
        {
            Name = column.Name,
            Title = column.Title,
            Items = CloneMenuItems(column.Items)
        }).ToArray();
    }

    private static Dictionary<string, object?>? CloneMeta(Dictionary<string, object?>? meta)
    {
        if (meta is null || meta.Count == 0)
            return null;
        return meta.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static NavigationRegionSpec[] CloneRegions(NavigationRegionSpec[]? regions)
    {
        if (regions is null || regions.Length == 0)
            return Array.Empty<NavigationRegionSpec>();
        return regions.Select(region => new NavigationRegionSpec
        {
            Name = region.Name,
            Title = region.Title,
            Menus = region.Menus?.ToArray() ?? Array.Empty<string>(),
            Items = CloneMenuItems(region.Items),
            IncludeActions = region.IncludeActions,
            Template = region.Template,
            CssClass = region.CssClass,
            Meta = CloneMeta(region.Meta)
        }).ToArray();
    }

    private static NavigationFooterSpec? CloneFooter(NavigationFooterSpec? footer)
    {
        if (footer is null)
            return null;
        return new NavigationFooterSpec
        {
            Label = footer.Label,
            Template = footer.Template,
            CssClass = footer.CssClass,
            Meta = CloneMeta(footer.Meta),
            Columns = CloneFooterColumns(footer.Columns),
            Menus = footer.Menus?.ToArray() ?? Array.Empty<string>(),
            Legal = CloneMenuItems(footer.Legal)
        };
    }

    private static NavigationFooterColumnSpec[] CloneFooterColumns(NavigationFooterColumnSpec[]? columns)
    {
        if (columns is null || columns.Length == 0)
            return Array.Empty<NavigationFooterColumnSpec>();
        return columns.Select(column => new NavigationFooterColumnSpec
        {
            Name = column.Name,
            Title = column.Title,
            Template = column.Template,
            CssClass = column.CssClass,
            Meta = CloneMeta(column.Meta),
            Items = CloneMenuItems(column.Items)
        }).ToArray();
    }
}
