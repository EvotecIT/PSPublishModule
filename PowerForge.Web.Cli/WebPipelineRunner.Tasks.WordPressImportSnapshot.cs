using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static readonly HashSet<string> ReservedWordPressPageSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "api",
        "blog",
        "categories",
        "docs",
        "news",
        "projects",
        "search",
        "tags"
    };

    private static void ExecuteWordPressImportSnapshot(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var snapshotPath = ResolvePath(baseDir,
            GetString(step, "snapshotPath") ??
            GetString(step, "snapshot-path") ??
            GetString(step, "snapshot") ??
            GetString(step, "input") ??
            GetString(step, "path"));
        if (string.IsNullOrWhiteSpace(snapshotPath))
            throw new InvalidOperationException("wordpress-import-snapshot: snapshotPath is required.");
        snapshotPath = Path.GetFullPath(snapshotPath);

        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root")) ?? baseDir;
        siteRoot = Path.GetFullPath(siteRoot);

        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"))
                          ?? Path.Combine(siteRoot, "Build", "import-wordpress-last-run.json");
        var redirectCsvPath = ResolvePath(baseDir, GetString(step, "redirectCsvPath") ?? GetString(step, "redirect-csv-path"))
                              ?? Path.Combine(siteRoot, "data", "redirects", "legacy-wordpress-generated.csv");

        var defaultLanguage = (GetString(step, "defaultLanguage") ?? GetString(step, "default-language") ?? "en").Trim();
        if (string.IsNullOrWhiteSpace(defaultLanguage))
            defaultLanguage = "en";

        var clearGenerated = GetBool(step, "clearGenerated") ?? GetBool(step, "clear-generated") ?? false;
        var whatIf = GetBool(step, "whatIf") ?? GetBool(step, "what-if") ?? false;
        var translationKeyMapPath = ResolvePath(baseDir,
            GetString(step, "translationKeyMapPath")
            ?? GetString(step, "translation-key-map-path")
            ?? GetString(step, "translationMapPath")
            ?? GetString(step, "translation-map-path")
            ?? GetString(step, "translationOverridesPath")
            ?? GetString(step, "translation-overrides-path"));
        var translationKeyMap = LoadWordPressTranslationKeyMap(translationKeyMapPath);

        var collections = ResolveWordPressCollections(step);
        var rawRoot = Path.Combine(snapshotPath, "raw");
        if (!Directory.Exists(rawRoot))
            throw new InvalidOperationException($"wordpress-import-snapshot: snapshot raw folder not found: {rawRoot}");

        var languageFolders = Directory.EnumerateDirectories(rawRoot)
            .Select(Path.GetFullPath)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (languageFolders.Count == 0)
            throw new InvalidOperationException($"wordpress-import-snapshot: no language folders found under: {rawRoot}");

        var defaultLanguageFolder = languageFolders.FirstOrDefault(path =>
            Path.GetFileName(path).Equals("default", StringComparison.OrdinalIgnoreCase))
            ?? languageFolders.FirstOrDefault(path =>
                Path.GetFileName(path).Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase));

        var defaultCategoryMap = defaultLanguageFolder is null
            ? new Dictionary<int, string>()
            : LoadWordPressTaxonomyNameMap(Path.Combine(defaultLanguageFolder, "categories.json"));
        var defaultTagMap = defaultLanguageFolder is null
            ? new Dictionary<int, string>()
            : LoadWordPressTaxonomyNameMap(Path.Combine(defaultLanguageFolder, "tags.json"));

        var generatedFiles = new List<string>();
        var skippedFiles = new List<string>();
        var redirects = new List<WordPressRedirectRow>();
        var deletedGeneratedFiles = 0;

        if (clearGenerated)
        {
            var targets = new[]
            {
                Path.Combine(siteRoot, "content", "blog"),
                Path.Combine(siteRoot, "content", "pages")
            };
            foreach (var target in targets)
            {
                if (!Directory.Exists(target))
                    continue;

                foreach (var markdown in Directory.EnumerateFiles(target, "*.md", SearchOption.AllDirectories))
                {
                    if (!IsImportedMarkdownFile(markdown))
                        continue;
                    deletedGeneratedFiles++;
                    if (!whatIf)
                        File.Delete(markdown);
                }
            }
        }

        foreach (var languageFolder in languageFolders)
        {
            var languageFolderName = Path.GetFileName(languageFolder);
            var languageCode = languageFolderName.Equals("default", StringComparison.OrdinalIgnoreCase)
                ? defaultLanguage
                : languageFolderName;

            var languageCategoryMap = LoadWordPressTaxonomyNameMap(Path.Combine(languageFolder, "categories.json"));
            var languageTagMap = LoadWordPressTaxonomyNameMap(Path.Combine(languageFolder, "tags.json"));
            var fallbackCategoryMap = languageCode.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase)
                ? languageCategoryMap
                : defaultCategoryMap;
            var fallbackTagMap = languageCode.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase)
                ? languageTagMap
                : defaultTagMap;

            foreach (var collection in collections)
            {
                var inputPath = Path.Combine(languageFolder, collection + ".json");
                if (!File.Exists(inputPath))
                    continue;

                using var doc = JsonDocument.Parse(SafeReadAllText(inputPath));
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    continue;

                var outputRoot = collection.Equals("posts", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(siteRoot, "content", "blog", languageCode)
                    : Path.Combine(siteRoot, "content", "pages", languageCode);
                if (!whatIf)
                    Directory.CreateDirectory(outputRoot);

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || !TryGetInt(item, "id", out var id))
                        continue;

                    var slug = GetSafeWordPressImportSlug(GetString(item, "slug"), id, collection.TrimEnd('s'));
                    if (collection.Equals("pages", StringComparison.OrdinalIgnoreCase) && ReservedWordPressPageSlugs.Contains(slug))
                        continue;

                    var titleRaw = GetNestedString(item, "title", "raw") ?? GetNestedString(item, "title", "rendered");
                    var title = string.IsNullOrWhiteSpace(titleRaw)
                        ? $"{collection} {id}"
                        : WebUtility.HtmlDecode(titleRaw);
                    var description = ToWordPressPlainText(GetNestedString(item, "excerpt", "raw"))
                                      ?? ToWordPressPlainText(GetNestedString(item, "excerpt", "rendered"));
                    var dateValue = ToIsoDateString(GetString(item, "date"));
                    var body = GetNestedString(item, "content", "raw")
                               ?? GetNestedString(item, "content", "rendered")
                               ?? string.Empty;

                    var canonicalRoute = GetWordPressCanonicalRoute(collection, languageCode, slug, defaultLanguage);
                    var translationPrefix = collection.Equals("posts", StringComparison.OrdinalIgnoreCase) ? "wp-post-" : "wp-page-";
                    var translationKey = ResolveWordPressImportTranslationKey(
                        item,
                        id,
                        translationPrefix,
                        languageCode,
                        defaultLanguage,
                        translationKeyMap);
                    var legacyQueryAlias = collection.Equals("posts", StringComparison.OrdinalIgnoreCase) ? $"/?p={id}" : $"/?page_id={id}";

                    var categoryIds = collection.Equals("posts", StringComparison.OrdinalIgnoreCase)
                        ? GetIntArray(item, "categories")
                        : Array.Empty<int>();
                    var tagIds = collection.Equals("posts", StringComparison.OrdinalIgnoreCase)
                        ? GetIntArray(item, "tags")
                        : Array.Empty<int>();

                    var usedTaxonomyFallback = false;
                    var categoryNames = categoryIds
                        .Select(idValue => ResolveTaxonomyName(idValue, languageCategoryMap, fallbackCategoryMap, ref usedTaxonomyFallback))
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Select(static value => value!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var tagNames = tagIds
                        .Select(idValue => ResolveTaxonomyName(idValue, languageTagMap, fallbackTagMap, ref usedTaxonomyFallback))
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Select(static value => value!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var aliases = new List<string> { legacyQueryAlias };
                    var link = GetString(item, "link");
                    if (!string.IsNullOrWhiteSpace(link) && Uri.TryCreate(link, UriKind.Absolute, out var linkUri))
                    {
                        var pathAlias = string.IsNullOrWhiteSpace(linkUri.AbsolutePath) ? "/" : linkUri.AbsolutePath;
                        var isRootAlias = pathAlias.Equals("/", StringComparison.Ordinal) || pathAlias.Equals("/" + languageCode + "/", StringComparison.Ordinal);
                        if (collection.Equals("pages", StringComparison.OrdinalIgnoreCase) &&
                            !slug.Equals("index", StringComparison.OrdinalIgnoreCase) &&
                            isRootAlias)
                        {
                            pathAlias = string.Empty;
                        }

                        if (!string.IsNullOrWhiteSpace(pathAlias) &&
                            !pathAlias.Equals(canonicalRoute, StringComparison.Ordinal))
                        {
                            aliases.Add(pathAlias);
                        }
                    }

                    aliases = aliases
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var outputPath = Path.Combine(outputRoot, slug + ".md");
                    if (File.Exists(outputPath) && !IsImportedMarkdownFile(outputPath))
                    {
                        skippedFiles.Add(outputPath);
                        continue;
                    }

                    var content = BuildWordPressImportedMarkdown(
                        title,
                        description,
                        dateValue,
                        slug,
                        languageCode,
                        translationKey,
                        aliases,
                        id,
                        GetString(item, "status"),
                        link,
                        GetIntOrDefault(item, "featured_media"),
                        categoryIds,
                        tagIds,
                        usedTaxonomyFallback,
                        categoryNames,
                        tagNames,
                        body);

                    if (!whatIf)
                        File.WriteAllText(outputPath, content, new UTF8Encoding(false));
                    generatedFiles.Add(outputPath);

                    foreach (var alias in aliases.Where(alias => !alias.Equals(canonicalRoute, StringComparison.Ordinal)))
                    {
                        redirects.Add(new WordPressRedirectRow
                        {
                            LegacyUrl = alias,
                            TargetUrl = canonicalRoute,
                            Status = 301,
                            SourceType = collection.TrimEnd('s'),
                            SourceId = id,
                            Language = languageCode,
                            Notes = "generated by import-wordpress-snapshot"
                        });
                    }
                }
            }
        }

        var uniqueRedirects = redirects
            .GroupBy(static row => $"{row.LegacyUrl}|{row.TargetUrl}|{row.SourceType}|{row.SourceId}|{row.Language}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static row => row.LegacyUrl, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.TargetUrl, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceId)
            .ThenBy(static row => row.Language, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!whatIf)
        {
            var redirectDirectory = Path.GetDirectoryName(redirectCsvPath);
            if (!string.IsNullOrWhiteSpace(redirectDirectory))
                Directory.CreateDirectory(redirectDirectory);
            WriteWordPressRedirectCsv(redirectCsvPath, uniqueRedirects);

            var summaryDirectory = Path.GetDirectoryName(summaryPath);
            if (!string.IsNullOrWhiteSpace(summaryDirectory))
                Directory.CreateDirectory(summaryDirectory);
            var summary = new
            {
                generated_files = generatedFiles.Count,
                skipped_files = skippedFiles.Count,
                redirect_entries = uniqueRedirects.Count,
                redirect_csv = Path.GetFullPath(redirectCsvPath),
                deleted_generated_files = deletedGeneratedFiles
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }

        stepResult.Success = true;
        stepResult.Message = $"wordpress-import-snapshot ok: generated={generatedFiles.Count}; skipped={skippedFiles.Count}; redirects={uniqueRedirects.Count}";
    }

    private static string ResolveWordPressImportTranslationKey(
        JsonElement item,
        int id,
        string translationPrefix,
        string languageCode,
        string defaultLanguage,
        IReadOnlyDictionary<string, string> translationKeyMap)
    {
        var translationGroupId = ResolveWordPressTranslationGroupId(item, languageCode, defaultLanguage, id);
        var baseKey = translationPrefix + translationGroupId.ToString(CultureInfo.InvariantCulture);
        return ResolveWordPressTranslationKeyOverride(
            translationKeyMap,
            languageCode,
            baseKey,
            id.ToString(CultureInfo.InvariantCulture),
            translationPrefix);
    }

    private static int ResolveWordPressTranslationGroupId(JsonElement item, string languageCode, string defaultLanguage, int fallbackId)
    {
        var ids = new HashSet<int>();
        var byLanguage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("translations", out var translations) &&
            translations.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in translations.EnumerateObject())
            {
                if (!TryGetWordPressTranslationId(property.Value, out var value))
                    continue;

                ids.Add(value);
                var key = property.Name?.Trim();
                if (!string.IsNullOrWhiteSpace(key) && !byLanguage.ContainsKey(key))
                    byLanguage[key] = value;
            }
        }

        ids.Add(fallbackId);

        if (!string.IsNullOrWhiteSpace(defaultLanguage) && byLanguage.TryGetValue(defaultLanguage, out var defaultId))
            return defaultId;
        if (byLanguage.TryGetValue("default", out var explicitDefaultId))
            return explicitDefaultId;
        if (!string.IsNullOrWhiteSpace(languageCode) && byLanguage.TryGetValue(languageCode, out var currentLanguageId))
            return currentLanguageId;
        return ids.Count > 0 ? ids.Min() : fallbackId;
    }

    private static bool TryGetWordPressTranslationId(JsonElement value, out int translationId)
    {
        translationId = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out translationId) && translationId > 0,
            JsonValueKind.String => int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out translationId) && translationId > 0,
            _ => false
        };
    }

    private static Dictionary<string, string> LoadWordPressTranslationKeyMap(string? mapPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(mapPath))
            return map;

        var resolvedPath = Path.GetFullPath(mapPath);
        if (!File.Exists(resolvedPath))
            return map;

        using var doc = JsonDocument.Parse(SafeReadAllText(resolvedPath));
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"WordPress translation key map must be a JSON object: {resolvedPath}");

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var from = NormalizeWordPressTranslationMapToken(property.Name);
            if (string.IsNullOrWhiteSpace(from))
                continue;

            string? to = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(to))
                continue;

            map[from] = to.Trim();
        }

        return map;
    }

    private static string ResolveWordPressTranslationKeyOverride(
        IReadOnlyDictionary<string, string> translationKeyMap,
        string? languageCode,
        string currentTranslationKey,
        string? wpId,
        string? translationPrefix)
    {
        if (translationKeyMap is null || translationKeyMap.Count == 0 || string.IsNullOrWhiteSpace(currentTranslationKey))
            return currentTranslationKey;

        foreach (var candidate in BuildWordPressTranslationLookupKeys(languageCode, currentTranslationKey, wpId, translationPrefix))
        {
            if (!translationKeyMap.TryGetValue(candidate, out var mappedValue) || string.IsNullOrWhiteSpace(mappedValue))
                continue;

            var normalized = NormalizeWordPressTranslationOverrideValue(mappedValue, currentTranslationKey, translationPrefix);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        return currentTranslationKey;
    }

    private static IEnumerable<string> BuildWordPressTranslationLookupKeys(
        string? languageCode,
        string? currentTranslationKey,
        string? wpId,
        string? translationPrefix)
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            var normalized = NormalizeWordPressTranslationMapToken(value);
            if (string.IsNullOrWhiteSpace(normalized))
                return;
            if (seen.Add(normalized))
                keys.Add(normalized);
        }

        var normalizedLanguage = NormalizeWordPressTranslationMapToken(languageCode);
        var normalizedWpId = NormalizeWordPressTranslationMapToken(wpId);
        var normalizedPrefix = NormalizeWordPressTranslationPrefix(currentTranslationKey, translationPrefix);

        Add(currentTranslationKey);
        if (!string.IsNullOrWhiteSpace(normalizedWpId))
        {
            Add(normalizedWpId);
            if (!string.IsNullOrWhiteSpace(normalizedPrefix))
                Add(normalizedPrefix + normalizedWpId);
        }

        if (!string.IsNullOrWhiteSpace(normalizedLanguage))
        {
            if (!string.IsNullOrWhiteSpace(currentTranslationKey))
                Add(normalizedLanguage + ":" + currentTranslationKey);
            if (!string.IsNullOrWhiteSpace(normalizedWpId))
            {
                Add(normalizedLanguage + ":" + normalizedWpId);
                if (!string.IsNullOrWhiteSpace(normalizedPrefix))
                    Add(normalizedLanguage + ":" + normalizedPrefix + normalizedWpId);
            }
        }

        return keys;
    }

    private static string NormalizeWordPressTranslationOverrideValue(string mappedValue, string currentTranslationKey, string? translationPrefix)
    {
        var trimmed = mappedValue?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return currentTranslationKey;

        var numeric = trimmed.TrimStart('+');
        if (int.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue) && idValue > 0)
        {
            var prefix = NormalizeWordPressTranslationPrefix(currentTranslationKey, translationPrefix);
            return string.IsNullOrWhiteSpace(prefix)
                ? idValue.ToString(CultureInfo.InvariantCulture)
                : prefix + idValue.ToString(CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    private static string NormalizeWordPressTranslationPrefix(string? currentTranslationKey, string? fallbackPrefix = null)
    {
        var key = currentTranslationKey?.Trim();
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (key.StartsWith("wp-post-", StringComparison.OrdinalIgnoreCase))
                return "wp-post-";
            if (key.StartsWith("wp-page-", StringComparison.OrdinalIgnoreCase))
                return "wp-page-";
        }

        var fallback = fallbackPrefix?.Trim();
        if (string.IsNullOrWhiteSpace(fallback))
            return string.Empty;
        if (fallback.StartsWith("wp-post-", StringComparison.OrdinalIgnoreCase))
            return "wp-post-";
        if (fallback.StartsWith("wp-page-", StringComparison.OrdinalIgnoreCase))
            return "wp-page-";
        return fallback.ToLowerInvariant();
    }

    private static string NormalizeWordPressTranslationMapToken(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string[] ResolveWordPressCollections(JsonElement step)
    {
        var values = GetArrayOfStrings(step, "collections");
        if (values is null || values.Length == 0)
            values = new[] { "posts", "pages" };

        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Where(static value => value is "posts" or "pages")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? new[] { "posts", "pages" } : normalized;
    }

    private static string GetSafeWordPressImportSlug(string? slug, int id, string prefix)
        => string.IsNullOrWhiteSpace(slug) ? $"{prefix}-{id}" : slug.Trim().ToLowerInvariant();

    private static string ToIsoDateString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture)
            : value;
    }

    private static string? ToWordPressPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var collapsed = Regex.Replace(decoded, "\\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(collapsed) ? null : collapsed;
    }

    private static Dictionary<int, string> LoadWordPressTaxonomyNameMap(string path)
    {
        var map = new Dictionary<int, string>();
        if (!File.Exists(path))
            return map;
        using var doc = JsonDocument.Parse(SafeReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryGetInt(item, "id", out var id))
                continue;
            var name = GetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;
            map[id] = WebUtility.HtmlDecode(name).Trim();
        }
        return map;
    }

    private static string? ResolveTaxonomyName(int id, Dictionary<int, string> primary, Dictionary<int, string> fallback, ref bool usedFallback)
    {
        if (primary.TryGetValue(id, out var primaryName))
            return primaryName;
        if (fallback.TryGetValue(id, out var fallbackName))
        {
            usedFallback = true;
            return fallbackName;
        }
        return null;
    }

    private static string GetWordPressCanonicalRoute(string collection, string language, string slug, string defaultLanguage)
    {
        var useLanguagePrefix = !language.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase);
        var languagePrefix = useLanguagePrefix ? "/" + language : string.Empty;

        if (collection.Equals("posts", StringComparison.OrdinalIgnoreCase))
            return slug.Equals("index", StringComparison.OrdinalIgnoreCase) ? $"{languagePrefix}/blog/" : $"{languagePrefix}/blog/{slug}/";
        if (slug.Equals("index", StringComparison.OrdinalIgnoreCase))
            return useLanguagePrefix ? $"{languagePrefix}/" : "/";
        return $"{languagePrefix}/{slug}/";
    }

    private static string BuildWordPressImportedMarkdown(
        string title,
        string? description,
        string dateValue,
        string slug,
        string languageCode,
        string translationKey,
        List<string> aliases,
        int id,
        string? status,
        string? link,
        int? featuredMedia,
        int[] categoryIds,
        int[] tagIds,
        bool usedTaxonomyFallback,
        List<string> categoryNames,
        List<string> tagNames,
        string body)
    {
        var lines = new List<string>
        {
            "---",
            $"title: {WordPressYamlQuote(title)}"
        };
        if (!string.IsNullOrWhiteSpace(description))
            lines.Add($"description: {WordPressYamlQuote(description)}");
        lines.Add($"date: {WordPressYamlQuote(dateValue)}");
        lines.Add($"slug: {WordPressYamlQuote(slug)}");
        lines.Add($"language: {WordPressYamlQuote(languageCode)}");
        lines.Add($"translation_key: {WordPressYamlQuote(translationKey)}");

        if (categoryNames.Count > 0)
        {
            lines.Add("categories:");
            lines.AddRange(categoryNames.Select(name => "  - " + WordPressYamlQuote(name)));
        }

        if (tagNames.Count > 0)
        {
            lines.Add("tags:");
            lines.AddRange(tagNames.Select(name => "  - " + WordPressYamlQuote(name)));
        }

        lines.Add("aliases:");
        lines.AddRange(aliases.Select(alias => "  - " + WordPressYamlQuote(alias)));
        lines.Add("meta.generated_by: import-wordpress-snapshot");
        lines.Add("meta.wp_source: wordpress");
        lines.Add($"meta.wp_id: {id}");
        if (!string.IsNullOrWhiteSpace(status))
            lines.Add($"meta.wp_status: {WordPressYamlQuote(status)}");
        if (!string.IsNullOrWhiteSpace(link))
            lines.Add($"meta.wp_link: {WordPressYamlQuote(link)}");
        if (featuredMedia is > 0)
            lines.Add($"meta.wp_featured_media: {featuredMedia.Value}");
        if (categoryIds.Length > 0)
            lines.Add($"meta.wp_category_ids: {WordPressYamlQuote(string.Join(",", categoryIds))}");
        if (tagIds.Length > 0)
            lines.Add($"meta.wp_tag_ids: {WordPressYamlQuote(string.Join(",", tagIds))}");
        if (usedTaxonomyFallback)
            lines.Add("meta.wp_taxonomy_fallback: true");
        lines.Add("meta.raw_html: true");
        lines.Add("---");
        lines.Add(string.Empty);

        return string.Join("\r\n", lines) + body;
    }

    private static void WriteWordPressRedirectCsv(string csvPath, List<WordPressRedirectRow> rows)
    {
        static string Escape(string? value)
        {
            var text = value ?? string.Empty;
            if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
                return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            return text;
        }

        var sb = new StringBuilder();
        sb.AppendLine("legacy_url,target_url,status,source_type,source_id,language,notes");
        foreach (var row in rows)
        {
            sb.Append(Escape(row.LegacyUrl)).Append(',')
                .Append(Escape(row.TargetUrl)).Append(',')
                .Append(row.Status.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(row.SourceType)).Append(',')
                .Append(row.SourceId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(row.Language)).Append(',')
                .Append(Escape(row.Notes))
                .AppendLine();
        }
        File.WriteAllText(csvPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static string WordPressYamlQuote(string? value)
    {
        if (value is null)
            return "\"\"";
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    private static string? GetNestedString(JsonElement element, string first, string second)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(first, out var parent) || parent.ValueKind != JsonValueKind.Object)
            return null;
        if (!parent.TryGetProperty(second, out var child))
            return null;
        return child.ValueKind == JsonValueKind.String ? child.GetString() : child.ToString();
    }

    private static int[] GetIntArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<int>();
        var list = new List<int>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var intValue))
                list.Add(intValue);
            else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var parsed))
                list.Add(parsed);
        }
        return list.ToArray();
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return false;
        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), out value),
            _ => false
        };
    }

    private static int? GetIntOrDefault(JsonElement element, string propertyName)
        => TryGetInt(element, propertyName, out var value) ? value : null;

    private sealed class WordPressRedirectRow
    {
        public string LegacyUrl { get; set; } = string.Empty;
        public string TargetUrl { get; set; } = string.Empty;
        public int Status { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public int SourceId { get; set; }
        public string Language { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}
