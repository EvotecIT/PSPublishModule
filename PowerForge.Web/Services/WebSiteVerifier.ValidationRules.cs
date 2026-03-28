using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Data file and taxonomy verification rules.</summary>
public static partial class WebSiteVerifier
{
    private static readonly Regex ReleaseShortcodeProductRegex = new(
        @"\{\{<\s*release-(?:button|buttons|changelog)(?:-placement)?\b[^>]*\bproduct\s*=\s*""(?<product>[^""]+)""[^>]*>\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private static readonly Regex ReleaseShortcodeAttrsRegex = new(
        @"\{\{<\s*release-(?:button|buttons|changelog)(?:-placement)?\b(?<attrs>[^>]*)>\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private static readonly Regex ShortcodeAttributeRegex = new(
        @"(?<key>[A-Za-z0-9_]+)\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex ReleaseHelperLiteralRegex = new(
        @"pf\.release_(?:button|buttons|changelog)\s+(?:""(?<product_dq>[^""]+)""|'(?<product_sq>[^']+)')",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private const string DefaultReleasePlacementsDataPath = "release_placements";

    private static void ValidateDataFiles(
        SiteSpec spec,
        WebSitePlan plan,
        List<string> warnings,
        HashSet<string> releaseProductReferences,
        List<ReleasePlacementReference> releasePlacementReferences)
    {
        if (spec is null || plan is null) return;

        var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
        var basePath = Path.IsPathRooted(dataRoot) ? dataRoot : Path.Combine(plan.RootPath, dataRoot);

        ValidateKnownDataFile(basePath, "faq.json", "data/faq.json", ValidateFaqJson, warnings);
        ValidateKnownDataFile(basePath, "showcase.json", "data/showcase.json", ValidateShowcaseJson, warnings);
        ValidateKnownDataFile(basePath, "pricing.json", "data/pricing.json", ValidatePricingJson, warnings);
        ValidateKnownDataFile(basePath, "benchmarks.json", "data/benchmarks.json", ValidateBenchmarksJson, warnings);

        foreach (var project in plan.Projects ?? Array.Empty<WebProjectPlan>())
        {
            if (string.IsNullOrWhiteSpace(project.RootPath))
                continue;

            var projectDataRoot = Path.Combine(project.RootPath, "data");
            ValidateKnownDataFile(projectDataRoot, "faq.json", $"projects/{project.Slug}/data/faq.json", ValidateFaqJson, warnings);
            ValidateKnownDataFile(projectDataRoot, "showcase.json", $"projects/{project.Slug}/data/showcase.json", ValidateShowcaseJson, warnings);
            ValidateKnownDataFile(projectDataRoot, "pricing.json", $"projects/{project.Slug}/data/pricing.json", ValidatePricingJson, warnings);
            ValidateKnownDataFile(projectDataRoot, "benchmarks.json", $"projects/{project.Slug}/data/benchmarks.json", ValidateBenchmarksJson, warnings);
        }

        CollectReleaseProductReferencesFromData(basePath, releaseProductReferences);
        ValidateReleaseHubData(basePath, releaseProductReferences, warnings);
        ValidateReleasePlacementData(basePath, releasePlacementReferences, warnings);
    }

    private static void ValidateKnownDataFile(
        string basePath,
        string fileName,
        string label,
        Action<JsonElement, string, List<string>> validator,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return;

        var path = Path.Combine(basePath, fileName);
        if (!File.Exists(path))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            validator(doc.RootElement, label, warnings);
        }
        catch (Exception ex)
        {
            warnings.Add($"Data file '{label}' could not be read: {ex.Message}");
        }
    }

    private static void CollectReleaseProductReferencesFromMarkdown(string markdown, HashSet<string> references)
    {
        if (string.IsNullOrWhiteSpace(markdown) || references is null)
            return;

        foreach (Match match in ReleaseShortcodeProductRegex.Matches(markdown))
        {
            var product = NormalizeReleaseProductReference(match.Groups["product"].Value);
            if (!string.IsNullOrWhiteSpace(product))
                references.Add(product);
        }
    }

    private static void CollectReleasePlacementReferencesFromMarkdown(string markdown, List<ReleasePlacementReference> references)
    {
        if (string.IsNullOrWhiteSpace(markdown) || references is null)
            return;

        foreach (Match shortcodeMatch in ReleaseShortcodeAttrsRegex.Matches(markdown))
        {
            var attrs = shortcodeMatch.Groups["attrs"].Value;
            if (string.IsNullOrWhiteSpace(attrs))
                continue;

            string? placement = null;
            string? placementsRoot = null;
            foreach (Match attrMatch in ShortcodeAttributeRegex.Matches(attrs))
            {
                var key = attrMatch.Groups["key"].Value;
                var value = attrMatch.Groups["value"].Value.Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (key.Equals("placement", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("slot", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("preset", StringComparison.OrdinalIgnoreCase))
                {
                    placement ??= value;
                    continue;
                }

                if (key.Equals("placementsData", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("placements_data", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("placementsPath", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("placements_path", StringComparison.OrdinalIgnoreCase))
                {
                    placementsRoot ??= value;
                }
            }

            if (!string.IsNullOrWhiteSpace(placement))
            {
                references.Add(new ReleasePlacementReference(
                    placement.Trim(),
                    string.IsNullOrWhiteSpace(placementsRoot) ? null : placementsRoot.Trim()));
            }
        }
    }

    private static void CollectReleaseProductReferencesFromThemeTemplates(SiteSpec spec, WebSitePlan plan, HashSet<string> references)
    {
        if (spec is null || plan is null || references is null)
            return;

        var themeRoot = ResolveThemeRoot(spec, plan.RootPath, plan.ThemesRoot);
        if (string.IsNullOrWhiteSpace(themeRoot) || !Directory.Exists(themeRoot))
            return;

        var extensions = new[] { ".html", ".scriban", ".sbn", ".txt" };
        foreach (var file in Directory.EnumerateFiles(themeRoot, "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            foreach (Match match in ReleaseHelperLiteralRegex.Matches(content))
            {
                var product = match.Groups["product_dq"].Success
                    ? match.Groups["product_dq"].Value.Trim()
                    : match.Groups["product_sq"].Value.Trim();
                product = NormalizeReleaseProductReference(product);
                if (!string.IsNullOrWhiteSpace(product))
                    references.Add(product);
            }
        }
    }

    private static void CollectReleaseProductReferencesFromData(string dataRootPath, HashSet<string> references)
    {
        if (string.IsNullOrWhiteSpace(dataRootPath) || references is null || !Directory.Exists(dataRootPath))
            return;

        var files = Directory.EnumerateFiles(dataRootPath, "*.json", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                CollectReleaseProductReferencesFromJson(doc.RootElement, references);
            }
            catch
            {
                // Ignore parse failures here - known data files are validated separately.
            }
        }
    }

    private static void CollectReleaseProductReferencesFromJson(JsonElement element, HashSet<string> references)
    {
        if (references is null)
            return;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("releaseProduct") ||
                        property.NameEquals("release_product") ||
                        property.NameEquals("releaseProductId") ||
                        property.NameEquals("release_product_id"))
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            var product = NormalizeReleaseProductReference(property.Value.GetString());
                            if (!string.IsNullOrWhiteSpace(product))
                                references.Add(product);
                        }
                    }

                    CollectReleaseProductReferencesFromJson(property.Value, references);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectReleaseProductReferencesFromJson(item, references);
                break;
        }
    }

    private static void ValidateReleaseHubData(string dataRootPath, HashSet<string> releaseProductReferences, List<string> warnings)
    {
        var releaseHubPath = ResolveReleaseHubDataPath(dataRootPath);
        if (string.IsNullOrWhiteSpace(releaseHubPath))
        {
            if (releaseProductReferences.Count > 0)
            {
                var sample = string.Join(", ", releaseProductReferences.OrderBy(static p => p, StringComparer.OrdinalIgnoreCase).Take(5));
                warnings.Add($"[PFWEB.RELEASE.NO_MATCH] Release lint: release selectors reference products ({sample}) but 'data/release-hub.json' was not found.");
            }

            return;
        }

        var catalogProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assetProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateAssetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assetKeySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(releaseHubPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("[PFWEB.RELEASE.DATA] Release lint: release-hub data root should be an object.");
                return;
            }

            if (doc.RootElement.TryGetProperty("products", out var productsEl) && productsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var productEl in productsEl.EnumerateArray())
                {
                    if (productEl.ValueKind != JsonValueKind.Object)
                        continue;
                    var productId = ReadJsonString(productEl, "id", "product");
                    if (!string.IsNullOrWhiteSpace(productId))
                        catalogProducts.Add(productId);
                }
            }

            var releasesEl = default(JsonElement);
            var hasReleases = doc.RootElement.TryGetProperty("releases", out releasesEl) && releasesEl.ValueKind == JsonValueKind.Array;
            if (!hasReleases)
                hasReleases = doc.RootElement.TryGetProperty("items", out releasesEl) && releasesEl.ValueKind == JsonValueKind.Array;

            if (hasReleases)
            {
                foreach (var releaseEl in releasesEl.EnumerateArray())
                {
                    if (releaseEl.ValueKind != JsonValueKind.Object)
                        continue;

                    var releaseTag = ReadJsonString(releaseEl, "tag", "tag_name", "title", "name") ?? "release";
                    if (!releaseEl.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var assetEl in assetsEl.EnumerateArray())
                    {
                        if (assetEl.ValueKind != JsonValueKind.Object)
                            continue;

                        var product = ReadJsonString(assetEl, "product", "id");
                        if (!string.IsNullOrWhiteSpace(product))
                            assetProducts.Add(product);

                        var assetName = ReadJsonString(assetEl, "name") ?? string.Empty;
                        var downloadUrl = ReadJsonString(assetEl, "downloadUrl", "download_url", "browser_download_url", "url") ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(assetName) && string.IsNullOrWhiteSpace(downloadUrl))
                            continue;

                        var assetKey = $"{releaseTag}|{assetName}|{downloadUrl}";
                        if (!assetKeySet.Add(assetKey))
                            duplicateAssetKeys.Add(assetKey);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"[PFWEB.RELEASE.DATA] Release lint: failed to parse release-hub data: {ex.Message}");
            return;
        }

        foreach (var product in releaseProductReferences.OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (!assetProducts.Contains(product))
                warnings.Add($"[PFWEB.RELEASE.NO_MATCH] Release lint: referenced product '{product}' has no matching assets in release-hub data.");
        }

        foreach (var product in assetProducts.Where(static p => !string.Equals(p, "unknown", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (!catalogProducts.Contains(product))
                warnings.Add($"[PFWEB.RELEASE.PRODUCT_MISSING] Release lint: release-hub assets reference product '{product}' but products catalog does not define it.");
        }

        foreach (var collision in duplicateAssetKeys.OrderBy(static c => c, StringComparer.OrdinalIgnoreCase).Take(20))
        {
            warnings.Add($"[PFWEB.RELEASE.ASSET_COLLISION] Release lint: duplicate asset entry '{collision}' detected in release-hub data.");
        }
    }

    private static void ValidateReleasePlacementData(string dataRootPath, List<ReleasePlacementReference> releasePlacementReferences, List<string> warnings)
    {
        if (releasePlacementReferences is null || releasePlacementReferences.Count == 0)
            return;

        using var doc = TryLoadReleasePlacementsDocument(dataRootPath, out var releasePlacementsPath, out var loadError);
        if (doc is null)
        {
            var sample = string.Join(", ", releasePlacementReferences
                .Select(static item => item.Placement)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .Take(5));

            if (!string.IsNullOrWhiteSpace(loadError))
            {
                warnings.Add($"[PFWEB.RELEASE.PLACEMENT_MISSING] Release lint: release placement selectors reference ({sample}) but placement data could not be read ({loadError}).");
            }
            else
            {
                warnings.Add($"[PFWEB.RELEASE.PLACEMENT_MISSING] Release lint: release placement selectors reference ({sample}) but 'data/release_placements.json' was not found.");
            }

            return;
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            warnings.Add("[PFWEB.RELEASE.DATA] Release lint: release placement data root should be an object.");
            return;
        }

        var uniqueReferences = releasePlacementReferences
            .Where(static item => !string.IsNullOrWhiteSpace(item.Placement))
            .Distinct()
            .OrderBy(static item => item.Placement, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var reference in uniqueReferences)
        {
            var rootPath = string.IsNullOrWhiteSpace(reference.PlacementsRoot)
                ? DefaultReleasePlacementsDataPath
                : reference.PlacementsRoot.Trim();

            if (!rootPath.Equals(DefaultReleasePlacementsDataPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsPlacementDefined(doc.RootElement, reference.Placement))
            {
                var source = string.IsNullOrWhiteSpace(releasePlacementsPath)
                    ? "data/release_placements.json"
                    : Path.GetRelativePath(dataRootPath, releasePlacementsPath).Replace('\\', '/');
                warnings.Add($"[PFWEB.RELEASE.PLACEMENT_MISSING] Release lint: placement '{reference.Placement}' was not found in '{source}'.");
            }
        }
    }

    private static JsonDocument? TryLoadReleasePlacementsDocument(string dataRootPath, out string? path, out string? error)
    {
        path = ResolveReleasePlacementsDataPath(dataRootPath);
        error = null;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static bool IsPlacementDefined(JsonElement root, string placementPath)
    {
        if (string.IsNullOrWhiteSpace(placementPath))
            return false;

        JsonElement current = root;
        foreach (var part in placementPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object)
                return false;
            if (!TryGetObjectPropertyCaseInsensitive(current, part, out current))
                return false;
        }

        return current.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetObjectPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string? ResolveReleaseHubDataPath(string dataRootPath)
    {
        if (string.IsNullOrWhiteSpace(dataRootPath))
            return null;

        var candidates = new[]
        {
            Path.Combine(dataRootPath, "release-hub.json"),
            Path.Combine(dataRootPath, "release_hub.json")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ResolveReleasePlacementsDataPath(string dataRootPath)
    {
        if (string.IsNullOrWhiteSpace(dataRootPath))
            return null;

        var candidates = new[]
        {
            Path.Combine(dataRootPath, "release_placements.json"),
            Path.Combine(dataRootPath, "release-placements.json"),
            Path.Combine(dataRootPath, "releasePlacements.json")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? NormalizeReleaseProductReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        if (trimmed.Equals("*", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("any", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return null;

        return trimmed;
    }

    private static string? ReadJsonString(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var value))
                continue;
            if (value.ValueKind != JsonValueKind.String)
                continue;
            var text = value.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static void CollectTaxonomyTerms(
        TaxonomySpec[]? taxonomies,
        FrontMatter? matter,
        string language,
        Dictionary<string, Dictionary<string, HashSet<string>>> taxonomyTermsByLanguage,
        HashSet<string> usedTaxonomyNames)
    {
        if (matter is null)
            return;

        var normalizedLanguage = NormalizeLanguageToken(language);
        if (string.IsNullOrWhiteSpace(normalizedLanguage))
            normalizedLanguage = "en";

        if (matter.Tags is { Length: > 0 })
        {
            usedTaxonomyNames.Add("tags");

            foreach (var value in matter.Tags)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    RecordTaxonomyTerm(taxonomyTermsByLanguage, "tags", normalizedLanguage, value.Trim());
            }
        }

        if (matter.Meta is not null)
        {
            if (matter.Meta.TryGetValue("categories", out var categoriesValue) &&
                TryGetMetaValues(categoriesValue, out var categories))
            {
                usedTaxonomyNames.Add("categories");
                foreach (var value in categories)
                    RecordTaxonomyTerm(taxonomyTermsByLanguage, "categories", normalizedLanguage, value);
            }
        }

        foreach (var taxonomy in taxonomies ?? Array.Empty<TaxonomySpec>())
        {
            if (taxonomy is null || string.IsNullOrWhiteSpace(taxonomy.Name) || matter.Meta is null)
                continue;

            if (!matter.Meta.TryGetValue(taxonomy.Name, out var metaValue))
                continue;

            if (!TryGetMetaValues(metaValue, out var values))
                continue;

            usedTaxonomyNames.Add(taxonomy.Name);

            foreach (var value in values)
                RecordTaxonomyTerm(taxonomyTermsByLanguage, taxonomy.Name, normalizedLanguage, value);
        }
    }

    private static void RecordTaxonomyTerm(
        Dictionary<string, Dictionary<string, HashSet<string>>> taxonomyTermsByLanguage,
        string taxonomyName,
        string language,
        string term)
    {
        if (string.IsNullOrWhiteSpace(taxonomyName) || string.IsNullOrWhiteSpace(term))
            return;

        if (!taxonomyTermsByLanguage.TryGetValue(taxonomyName, out var termsByLanguage))
        {
            termsByLanguage = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            taxonomyTermsByLanguage[taxonomyName] = termsByLanguage;
        }

        if (!termsByLanguage.TryGetValue(language, out var terms))
        {
            terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            termsByLanguage[language] = terms;
        }

        terms.Add(term);
    }

    private static bool TryGetMetaValues(object? value, out string[] values)
    {
        values = Array.Empty<string>();
        if (value is null)
            return false;

        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            values = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            return values.Length > 0;
        }

        if (value is IEnumerable<object?> list)
        {
            values = list
                .Select(item => item?.ToString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray();
            return values.Length > 0;
        }

        var scalar = value.ToString();
        if (string.IsNullOrWhiteSpace(scalar))
            return false;

        values = new[] { scalar.Trim() };
        return true;
    }

    private static void AddSyntheticTaxonomyRoutes(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        Dictionary<string, string> routes,
        Dictionary<string, Dictionary<string, HashSet<string>>> taxonomyTermsByLanguage,
        List<string> warnings)
    {
        if (spec.Taxonomies is null || spec.Taxonomies.Length == 0)
            return;

        foreach (var taxonomy in spec.Taxonomies)
        {
            if (taxonomy is null || string.IsNullOrWhiteSpace(taxonomy.Name) || string.IsNullOrWhiteSpace(taxonomy.BasePath))
                continue;

            var languages = localization.Enabled
                ? localization.Languages.Select(language => language.Code).ToArray()
                : new[] { localization.DefaultLanguage };
            foreach (var language in languages)
            {
                var listRoute = BuildRoute(taxonomy.BasePath, string.Empty, spec.TrailingSlash);
                listRoute = ApplyLanguagePrefixToRoute(spec, localization, listRoute, language);
                if (routes.TryGetValue(listRoute, out var existingListRoute))
                {
                    if (!existingListRoute.StartsWith("[taxonomy:", StringComparison.OrdinalIgnoreCase))
                        warnings.Add($"Taxonomy route '{listRoute}' overlaps content route '{existingListRoute}'.");
                }
                else
                {
                    routes[listRoute] = $"[taxonomy:{taxonomy.Name}:{language}]";
                }

                if (!taxonomyTermsByLanguage.TryGetValue(taxonomy.Name, out var termsByLanguage) ||
                    !termsByLanguage.TryGetValue(language, out var terms) ||
                    terms.Count == 0)
                    continue;

                foreach (var term in terms.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    var slug = Slugify(term);
                    if (string.IsNullOrWhiteSpace(slug))
                        continue;

                    var termRoute = BuildRoute(taxonomy.BasePath, slug, spec.TrailingSlash);
                    termRoute = ApplyLanguagePrefixToRoute(spec, localization, termRoute, language);
                    if (routes.TryGetValue(termRoute, out var existingTermRoute))
                    {
                        if (!existingTermRoute.StartsWith("[taxonomy:", StringComparison.OrdinalIgnoreCase))
                            warnings.Add($"Taxonomy term route '{termRoute}' overlaps content route '{existingTermRoute}'.");
                        continue;
                    }

                    routes[termRoute] = $"[taxonomy:{taxonomy.Name}:{language}:{term}]";
                }
            }
        }
    }

    private static void ValidateBlogAndTaxonomySupport(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        IReadOnlyDictionary<string, List<CollectionRoute>> collectionRoutes,
        HashSet<string> usedTaxonomyNames,
        List<string> warnings)
    {
        if (spec.Collections is null || spec.Collections.Length == 0)
            return;

        foreach (var collection in spec.Collections)
        {
            if (collection is null || string.IsNullOrWhiteSpace(collection.Name))
                continue;
            var effectiveCollection = CollectionPresetDefaults.Apply(collection);

            var isEditorialCollection = CollectionPresetDefaults.IsEditorialCollection(effectiveCollection, collection.Name);
            if (!isEditorialCollection)
                continue;
            if (effectiveCollection.AutoGenerateSectionIndex)
                continue;

            if (!collectionRoutes.TryGetValue(collection.Name, out var routes) || routes.Count == 0)
                continue;

            var hasNonDraftContent = routes.Any(route => !route.Draft);
            if (!hasNonDraftContent)
                continue;

            var expectedLanguages = localization.Enabled
                ? localization.Languages.Select(language => language.Code).ToArray()
                : new[] { localization.DefaultLanguage };
            foreach (var language in expectedLanguages)
            {
                var expectedRoute = BuildRoute(effectiveCollection.Output, string.Empty, spec.TrailingSlash);
                expectedRoute = ApplyLanguagePrefixToRoute(spec, localization, expectedRoute, language);
                expectedRoute = NormalizeRouteForNavigationMatch(expectedRoute);
                var hasLanding = routes
                    .Where(route => !route.Draft)
                    .Where(route => ResolveEffectiveLanguageCode(localization, route.Language).Equals(language, StringComparison.OrdinalIgnoreCase))
                    .Select(route => NormalizeRouteForNavigationMatch(route.Route))
                    .Any(route => string.Equals(route, expectedRoute, StringComparison.OrdinalIgnoreCase));
                if (!hasLanding)
                {
                    warnings.Add($"Collection '{collection.Name}' looks like an editorial stream (blog/news/changelog) but has no landing page at '{expectedRoute}' for language '{language}'. Add '_index.md', a page with slug 'index', or enable AutoGenerateSectionIndex / Preset.");
                }
            }
        }

        if (usedTaxonomyNames.Contains("tags") &&
            (spec.Taxonomies is null || !spec.Taxonomies.Any(t => t is not null && t.Name.Equals("tags", StringComparison.OrdinalIgnoreCase))))
        {
            warnings.Add("Content uses tags but SiteSpec.Taxonomies does not define 'tags'. Add taxonomy mapping (for example base path '/tags').");
        }

        if (usedTaxonomyNames.Contains("categories") &&
            (spec.Taxonomies is null || !spec.Taxonomies.Any(t => t is not null && t.Name.Equals("categories", StringComparison.OrdinalIgnoreCase))))
        {
            warnings.Add("Content uses categories but SiteSpec.Taxonomies does not define 'categories'. Add taxonomy mapping (for example base path '/categories').");
        }
    }

    private static void ValidateLocalizationTranslationMappings(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        IReadOnlyDictionary<string, List<CollectionRoute>> collectionRoutes,
        List<string> warnings)
    {
        if (!localization.Enabled || localization.Languages.Length <= 1 || collectionRoutes.Count == 0)
            return;

        var expectedLanguages = localization.Languages
            .Select(static language => NormalizeLanguageToken(language.Code))
            .Where(static language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (expectedLanguages.Length <= 1)
            return;

        var entries = collectionRoutes.Values
            .SelectMany(static routes => routes)
            .Where(static route => !route.Draft)
            .Where(static route => !string.IsNullOrWhiteSpace(route.TranslationKey))
            .Select(route => new
            {
                route.Collection,
                route.Route,
                route.File,
                Language = NormalizeLanguageToken(route.Language),
                route.TranslationKey
            })
            .Where(static route => !string.IsNullOrWhiteSpace(route.Language))
            .ToArray();
        if (entries.Length == 0)
            return;

        var duplicates = entries
            .GroupBy(
                static entry => $"{entry.TranslationKey}|{entry.Language}",
                StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .ToArray();
        foreach (var duplicate in duplicates)
        {
            var sampleFiles = duplicate
                .Select(static entry => entry.File)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .Select(Path.GetFileName)
                .ToArray();
            warnings.Add(
                $"Localization: duplicate translation mapping for key '{duplicate.First().TranslationKey}' in language '{duplicate.First().Language}' ({string.Join(", ", sampleFiles)}).");
        }

        var defaultExpectedLanguages = expectedLanguages;
        var expectedLanguagesByCollection = (spec.Collections ?? Array.Empty<CollectionSpec>())
            .Where(static collection => collection is not null && !string.IsNullOrWhiteSpace(collection.Name))
            .ToDictionary(
                static collection => collection.Name,
                collection => ResolveExpectedTranslationLanguagesForCollection(localization, collection, defaultExpectedLanguages),
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in entries.GroupBy(static entry => entry.TranslationKey, StringComparer.OrdinalIgnoreCase))
        {
            var presentLanguages = group
                .Select(static entry => entry.Language)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static language => language, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (presentLanguages.Length < 2)
                continue;

            var expectedLanguagesForGroup = group
                .Select(entry => expectedLanguagesByCollection.TryGetValue(entry.Collection, out var configuredLanguages)
                    ? configuredLanguages
                    : defaultExpectedLanguages)
                .SelectMany(static languages => languages)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static language => language, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (expectedLanguagesForGroup.Length <= 1)
                continue;

            var missingLanguages = expectedLanguagesForGroup
                .Where(language => !presentLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (missingLanguages.Length == 0)
                continue;

            warnings.Add(
                $"Localization: translation '{group.Key}' is missing languages [{string.Join(", ", missingLanguages)}] (present: [{string.Join(", ", presentLanguages)}]).");
        }
    }

    private static string[] ResolveExpectedTranslationLanguagesForCollection(
        ResolvedLocalizationConfig localization,
        CollectionSpec collection,
        string[] defaultExpectedLanguages)
    {
        var configuredSource = collection.LocalizedLanguages is { Length: > 0 }
            ? collection.LocalizedLanguages
            : collection.ExpectedTranslationLanguages;
        if (configuredSource is not { Length: > 0 })
            return defaultExpectedLanguages;

        var configuredLanguages = configuredSource
            .Select(NormalizeLanguageToken)
            .Where(static language => !string.IsNullOrWhiteSpace(language))
            .Where(language => localization.ByCode.ContainsKey(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static language => language, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return configuredLanguages.Length > 0 ? configuredLanguages : defaultExpectedLanguages;
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
        public string Prefix { get; init; } = string.Empty;
        public string? BaseUrl { get; init; }
        public bool IsDefault { get; set; }
    }

    private sealed record ReleasePlacementReference(string Placement, string? PlacementsRoot);

    private sealed record CollectionRoute(string Collection, string Route, string File, bool Draft, string Language, string TranslationKey);
}
