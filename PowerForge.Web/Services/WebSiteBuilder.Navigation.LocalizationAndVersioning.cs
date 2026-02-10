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
        var currentPath = string.IsNullOrWhiteSpace(page.OutputPath) ? "/" : page.OutputPath;

        var languages = new List<LocalizationLanguageRuntime>();
        foreach (var language in localization.Languages)
        {
            var url = ResolveLocalizedPageUrl(spec, localization, page, allItems, language.Code, currentCode);
            languages.Add(new LocalizationLanguageRuntime
            {
                Code = language.Code,
                Label = language.Label,
                Prefix = language.Prefix,
                IsDefault = language.IsDefault,
                IsCurrent = language.Code.Equals(currentCode, StringComparison.OrdinalIgnoreCase),
                Url = string.IsNullOrWhiteSpace(url) ? currentPath : url
            });
        }

        if (languages.Count == 0)
        {
            languages.Add(new LocalizationLanguageRuntime
            {
                Code = currentCode,
                Label = currentCode,
                Prefix = currentCode,
                IsDefault = true,
                IsCurrent = true,
                Url = currentPath
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
        if (allItems.Count > 0 && !string.IsNullOrWhiteSpace(page.TranslationKey))
        {
            var translated = allItems
                .Where(i => !i.Draft)
                .Where(i => string.Equals(i.ProjectSlug ?? string.Empty, page.ProjectSlug ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Where(i => !string.IsNullOrWhiteSpace(i.TranslationKey))
                .FirstOrDefault(i =>
                    i.TranslationKey!.Equals(page.TranslationKey, StringComparison.OrdinalIgnoreCase) &&
                    ResolveEffectiveLanguageCode(localization, i.Language).Equals(targetLanguage, StringComparison.OrdinalIgnoreCase));
            if (translated is not null)
                return translated.OutputPath;
        }

        var baseRoute = StripLanguagePrefix(localization, page.OutputPath);
        if (string.IsNullOrWhiteSpace(baseRoute))
            baseRoute = "/";

        if (!ResolveEffectiveLanguageCode(localization, targetLanguage).Equals(currentLanguage, StringComparison.OrdinalIgnoreCase))
            return ApplyLanguagePrefixToRoute(spec, baseRoute, targetLanguage);

        return ApplyLanguagePrefixToRoute(spec, baseRoute, currentLanguage);
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
        if (matter?.Meta is not null)
        {
            if (TryGetMetaString(matter.Meta, "translation_key", out var translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
            if (TryGetMetaString(matter.Meta, "translation.key", out translationKey) && !string.IsNullOrWhiteSpace(translationKey))
                return translationKey.Trim();
            if (TryGetMetaString(matter.Meta, "i18n.key", out translationKey) && !string.IsNullOrWhiteSpace(translationKey))
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

                entries.Add(new ResolvedLocalizationLanguage
                {
                    Code = code,
                    Label = string.IsNullOrWhiteSpace(language.Label) ? code : language.Label.Trim(),
                    Prefix = prefix,
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

    private sealed class ResolvedLocalizationConfig
    {
        public bool Enabled { get; init; }
        public bool DetectFromPath { get; init; }
        public bool PrefixDefaultLanguage { get; init; }
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
                Deprecated = version.Deprecated
            };
        }

        var versions = versionMap.Values.ToArray();
        if (versions.Length == 0)
            return new VersioningRuntime();

        var current = ResolveCurrentVersion(versioning.Current, currentPath, versions);
        var latest = versions.FirstOrDefault(v => v.Latest) ?? versions.FirstOrDefault(v => v.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase)) ?? versions[0];
        var @default = versions.FirstOrDefault(v => v.Default) ?? versions[0];

        foreach (var version in versions)
            version.IsCurrent = version.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase);

        return new VersioningRuntime
        {
            Enabled = true,
            BasePath = NormalizeVersionBasePath(versioning.BasePath),
            Current = current,
            Latest = latest,
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

