using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Data file and taxonomy verification rules.</summary>
public static partial class WebSiteVerifier
{
    private static void ValidateDataFiles(SiteSpec spec, WebSitePlan plan, List<string> warnings)
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

            var isEditorialCollection = collection.Name.Equals("blog", StringComparison.OrdinalIgnoreCase) ||
                                        collection.Name.Equals("news", StringComparison.OrdinalIgnoreCase) ||
                                        collection.Output.Contains("blog", StringComparison.OrdinalIgnoreCase) ||
                                        collection.Output.Contains("news", StringComparison.OrdinalIgnoreCase);
            if (!isEditorialCollection)
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
                var expectedRoute = BuildRoute(collection.Output, string.Empty, spec.TrailingSlash);
                expectedRoute = ApplyLanguagePrefixToRoute(spec, localization, expectedRoute, language);
                expectedRoute = NormalizeRouteForNavigationMatch(expectedRoute);
                var hasLanding = routes
                    .Where(route => !route.Draft)
                    .Where(route => ResolveEffectiveLanguageCode(localization, route.Language).Equals(language, StringComparison.OrdinalIgnoreCase))
                    .Select(route => NormalizeRouteForNavigationMatch(route.Route))
                    .Any(route => string.Equals(route, expectedRoute, StringComparison.OrdinalIgnoreCase));
                if (!hasLanding)
                {
                    warnings.Add($"Collection '{collection.Name}' looks like an editorial stream (blog/news) but has no landing page at '{expectedRoute}' for language '{language}'. Add '_index.md' or a page with slug 'index'.");
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

        foreach (var group in entries.GroupBy(static entry => entry.TranslationKey, StringComparer.OrdinalIgnoreCase))
        {
            var presentLanguages = group
                .Select(static entry => entry.Language)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static language => language, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (presentLanguages.Length < 2)
                continue;

            var missingLanguages = expectedLanguages
                .Where(language => !presentLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (missingLanguages.Length == 0)
                continue;

            warnings.Add(
                $"Localization: translation '{group.Key}' is missing languages [{string.Join(", ", missingLanguages)}] (present: [{string.Join(", ", presentLanguages)}]).");
        }
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

    private sealed record CollectionRoute(string Route, string File, bool Draft, string Language, string TranslationKey);
}
