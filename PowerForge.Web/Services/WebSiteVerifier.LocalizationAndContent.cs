using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Web;

/// <summary>Localization resolution and content-path helpers for verification.</summary>
public static partial class WebSiteVerifier
{
    private static readonly StringComparison FileSystemPathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static ResolvedLocalizationConfig ResolveLocalizationConfig(SiteSpec spec, List<string> warnings)
    {
        var localizationSpec = spec.Localization;
        var defaultLanguage = NormalizeLanguageToken(localizationSpec?.DefaultLanguage);
        if (string.IsNullOrWhiteSpace(defaultLanguage))
            defaultLanguage = "en";

        var entries = new List<ResolvedLocalizationLanguage>();
        var duplicateCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicatePrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitDefaultCount = 0;
        if (localizationSpec?.Languages is { Length: > 0 })
        {
            foreach (var language in localizationSpec.Languages)
            {
                if (language is null || language.Disabled)
                    continue;

                var code = NormalizeLanguageToken(language.Code);
                if (string.IsNullOrWhiteSpace(code))
                    continue;
                if (entries.Any(e => e.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                {
                    duplicateCodes.Add(code);
                    continue;
                }

                var prefix = NormalizePath(string.IsNullOrWhiteSpace(language.Prefix) ? code : language.Prefix);
                if (string.IsNullOrWhiteSpace(prefix))
                    prefix = code;

                var languageBaseUrl = NormalizeAbsoluteBaseUrl(language.BaseUrl);
                if (!string.IsNullOrWhiteSpace(language.BaseUrl) && string.IsNullOrWhiteSpace(languageBaseUrl))
                {
                    warnings.Add(
                        $"Localization: language '{code}' defines invalid BaseUrl '{language.BaseUrl}'. Use an absolute http/https URL.");
                }

                if (entries.Any(e => NormalizeLanguageToken(e.Prefix).Equals(NormalizeLanguageToken(prefix), StringComparison.OrdinalIgnoreCase)))
                    duplicatePrefixes.Add(prefix);

                entries.Add(new ResolvedLocalizationLanguage
                {
                    Code = code,
                    Prefix = prefix,
                    BaseUrl = languageBaseUrl,
                    IsDefault = language.Default
                });
                if (language.Default)
                    explicitDefaultCount++;
            }
        }

        var activeLanguagesCount = entries.Count;
        if (localizationSpec?.Enabled == true && entries.Count == 0)
            warnings.Add("Localization is enabled but no active languages are configured.");
        foreach (var duplicateCode in duplicateCodes)
            warnings.Add($"Localization defines duplicate language code '{duplicateCode}'.");
        foreach (var duplicatePrefix in duplicatePrefixes)
            warnings.Add($"Localization defines duplicate language prefix '{duplicatePrefix}'.");
        if (explicitDefaultCount > 1)
            warnings.Add($"Localization defines multiple default languages ({explicitDefaultCount}). Mark only one language as default.");
        if (localizationSpec?.Enabled == true && activeLanguagesCount == 1)
            warnings.Add($"Best practice: localization is enabled but only one active language is configured ('{entries[0].Code}'). Add another language or disable localization.");
        if (localizationSpec?.Enabled == true &&
            !string.IsNullOrWhiteSpace(defaultLanguage) &&
            entries.Count > 0 &&
            !entries.Any(e => e.Code.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"Localization: defaultLanguage '{defaultLanguage}' does not match any active language code. Falling back to '{entries[0].Code}'.");
        }

        if (entries.Count == 0)
        {
            entries.Add(new ResolvedLocalizationLanguage
            {
                Code = defaultLanguage,
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

    private static string ResolveItemLanguage(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        string relativePath,
        FrontMatter? matter,
        out string localizedRelativePath,
        out string localizedRelativeDir)
    {
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

    private static bool TryGetMetaString(Dictionary<string, object?> meta, string key, out string value)
    {
        value = string.Empty;
        if (meta is null || string.IsNullOrWhiteSpace(key))
            return false;

        var parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        object? current = meta;
        foreach (var part in parts)
        {
            if (current is IReadOnlyDictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out current))
                    return false;
                continue;
            }

            return false;
        }

        if (current is null)
            return false;

        value = current.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ApplyLanguagePrefixToRoute(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        string route,
        string? language)
    {
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

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var trimmed = path.Trim();
        if (trimmed == "/") return "/";

        var normalized = trimmed.Replace('\\', '/');
        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .ToList();
        if (segments.Count == 0)
            return string.Empty;

        var stack = new List<string>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }

            stack.Add(segment);
        }

        return string.Join("/", stack);
    }

    private static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var lower = input.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') sb.Append('-');
        }
        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    private static IEnumerable<string> EnumerateCollectionFiles(string rootPath, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();

        var full = Path.IsPathRooted(input) ? input : Path.Combine(rootPath, input);
        if (full.Contains('*'))
            return EnumerateCollectionFilesWithWildcard(full);

        if (!Directory.Exists(full))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(full, "*.md", SearchOption.AllDirectories);
    }

    private static HashSet<string> BuildLeafBundleRoots(IReadOnlyList<string> markdownFiles)
    {
        if (markdownFiles.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var byDir = markdownFiles
            .GroupBy(f => Path.GetDirectoryName(f) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in byDir)
        {
            var dir = kvp.Key;
            var files = kvp.Value;
            var hasIndex = files.Any(IsLeafBundleIndex);
            if (!hasIndex) continue;
            if (files.Any(IsSectionIndex)) continue;

            var hasOtherMarkdown = files.Any(f =>
            {
                var name = Path.GetFileName(f);
                if (name.Equals("index.md", StringComparison.OrdinalIgnoreCase)) return false;
                if (name.Equals("_index.md", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            });

            if (!hasOtherMarkdown)
                roots.Add(dir);
        }

        return roots;
    }

    private static bool IsLeafBundleIndex(string filePath)
        => Path.GetFileName(filePath).Equals("index.md", StringComparison.OrdinalIgnoreCase);

    private static bool IsSectionIndex(string filePath)
        => Path.GetFileName(filePath).Equals("_index.md", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderAnyRoot(string filePath, HashSet<string> roots)
    {
        if (roots.Count == 0) return false;
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (filePath.StartsWith(root + Path.DirectorySeparatorChar, FileSystemPathComparison) ||
                filePath.Equals(root, FileSystemPathComparison))
                return true;
        }
        return false;
    }

    private static string ResolveRelativePath(string? collectionRoot, string filePath)
    {
        if (string.IsNullOrWhiteSpace(collectionRoot))
            return Path.GetFileName(filePath);
        return Path.GetRelativePath(collectionRoot, filePath).Replace('\\', '/');
    }
}

