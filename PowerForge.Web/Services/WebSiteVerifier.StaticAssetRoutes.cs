namespace PowerForge.Web;

public static partial class WebSiteVerifier
{
    private static IEnumerable<string> DiscoverStaticAssetRoutes(SiteSpec spec, string rootPath)
    {
        var projectionRoot = Path.GetFullPath(Path.Combine(rootPath, ".powerforge-static-route-projection"));
        if (!WebSiteBuilder.HasExplicitConventionalStaticMapping(spec, rootPath))
        {
            var conventionalRoot = Path.GetFullPath(Path.Combine(rootPath, "static"));
            if (Directory.Exists(conventionalRoot))
            {
                foreach (var file in EnumerateStaticFiles(conventionalRoot))
                {
                    foreach (var route in GetStaticAssetRoutes(file))
                        yield return route;
                }
            }
        }

        foreach (var mapping in spec.StaticAssets ?? Array.Empty<StaticAssetSpec>())
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Source))
                continue;

            string sourcePath;
            try
            {
                sourcePath = Path.IsPathRooted(mapping.Source)
                    ? Path.GetFullPath(mapping.Source)
                    : Path.GetFullPath(Path.Combine(rootPath, mapping.Source));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (File.Exists(sourcePath))
            {
                WebSiteBuilder.RejectLinkedAsset(new FileInfo(sourcePath));
                if (!WebSiteBuilder.TryResolveStaticAssetTargetPath(
                        projectionRoot,
                        sourcePath,
                        mapping.Destination,
                        out var targetPath))
                    continue;

                foreach (var route in GetStaticAssetRoutes(Path.GetRelativePath(projectionRoot, targetPath)))
                    yield return route;
                continue;
            }

            if (!Directory.Exists(sourcePath))
                continue;

            if (!WebSiteBuilder.TryResolveStaticAssetTargetPath(
                    projectionRoot,
                    sourcePath,
                    mapping.Destination,
                    out var targetRoot))
                continue;

            foreach (var file in EnumerateStaticFiles(sourcePath))
            {
                var projectedFile = Path.Combine(targetRoot, file);
                foreach (var route in GetStaticAssetRoutes(Path.GetRelativePath(projectionRoot, projectedFile)))
                    yield return route;
            }
        }
    }

    private static IEnumerable<string> EnumerateStaticFiles(string sourceRoot)
    {
        var root = new DirectoryInfo(sourceRoot);
        WebSiteBuilder.RejectLinkedAsset(root);

        foreach (var file in root.EnumerateFiles())
        {
            WebSiteBuilder.RejectLinkedAsset(file);
            yield return file.Name;
        }

        foreach (var directory in root.EnumerateDirectories())
        {
            WebSiteBuilder.RejectLinkedAsset(directory);
            foreach (var file in EnumerateStaticFiles(directory.FullName))
            {
                yield return Path.Combine(directory.Name, file);
            }
        }
    }

    private static IEnumerable<string> GetStaticAssetRoutes(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            yield break;

        var normalized = destination.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0 ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment == ".."))
            yield break;

        var encoded = string.Join(
            "/",
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var fileRoute = "/" + encoded;
        yield return fileRoute;

        var fileName = Path.GetFileName(normalized);
        var extension = Path.GetExtension(fileName);
        if (string.Equals(extension, ".html", StringComparison.Ordinal) ||
            string.Equals(extension, ".htm", StringComparison.Ordinal))
        {
            yield return "/" + encoded[..^extension.Length];
        }

        if (!string.Equals(fileName, "index.html", StringComparison.Ordinal))
            yield break;

        var separatorIndex = encoded.LastIndexOf('/');
        var directory = separatorIndex < 0 ? string.Empty : encoded[..separatorIndex].Trim('/');
        yield return string.IsNullOrWhiteSpace(directory) ? "/" : "/" + directory + "/";
    }

    private static IEnumerable<string> DiscoverGeneratedFeatureRoutes(
        SiteSpec spec,
        IEnumerable<CollectionRoute> contentRoutes)
    {
        var searchEnabled = (spec.Features ?? Array.Empty<string>()).Any(feature =>
            string.Equals(feature?.Trim(), "search", StringComparison.OrdinalIgnoreCase));
        if (!searchEnabled || !contentRoutes.Any(route =>
                WebSiteBuilder.IsSearchableContent(route.Draft, route.Route)))
            yield break;

        yield return "/search/index.html";
        yield return "/search/";
    }

    private static IEnumerable<string> DiscoverGeneratedPaginationRoutes(
        SiteSpec spec,
        IEnumerable<CollectionRoute> contentRoutes,
        IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, int>>> taxonomyTermCountsByLanguage)
    {
        if (spec.Pagination is { Enabled: false })
            yield break;

        var routes = contentRoutes
            .Where(static route => route is not null)
            .ToArray();
        if (routes.Length == 0)
            yield break;

        var pageSegment = WebSiteBuilder.NormalizePaginationSegment(spec.Pagination?.PathSegment);
        var defaultPageSize = Math.Max(0, spec.Pagination?.DefaultPageSize ?? 0);
        var collectionPageSizes = (spec.Collections ?? Array.Empty<CollectionSpec>())
            .Where(static collection => collection is not null && !string.IsNullOrWhiteSpace(collection.Name))
            .Select(CollectionPresetDefaults.Apply)
            .ToDictionary(
                static collection => collection.Name,
                collection => Math.Max(0, collection.PageSize ?? defaultPageSize),
                StringComparer.OrdinalIgnoreCase);
        var taxonomyPageSizes = (spec.Taxonomies ?? Array.Empty<TaxonomySpec>())
            .Where(static taxonomy => taxonomy is not null && !string.IsNullOrWhiteSpace(taxonomy.Name))
            .ToDictionary(
                static taxonomy => taxonomy.Name,
                taxonomy => Math.Max(0, taxonomy.PageSize ?? defaultPageSize),
                StringComparer.OrdinalIgnoreCase);

        foreach (var section in routes.Where(static route => !route.Draft && route.Kind is PageKind.Section or PageKind.Taxonomy or PageKind.Term))
        {
            var pageSize = section.Kind == PageKind.Section
                ? collectionPageSizes.TryGetValue(section.Collection, out var configuredCollectionPageSize)
                    ? configuredCollectionPageSize
                    : defaultPageSize
                : taxonomyPageSizes.TryGetValue(section.Collection, out var configuredTaxonomyPageSize)
                    ? configuredTaxonomyPageSize
                    : defaultPageSize;
            if (pageSize <= 0)
                continue;

            var sectionRoute = NormalizeRouteForNavigationMatch(section.Route);
            var totalItems = section.Kind switch
            {
                PageKind.Section => routes.Count(candidate =>
                    !candidate.Draft &&
                    candidate.Kind is PageKind.Page or PageKind.Home &&
                    string.Equals(candidate.Collection, section.Collection, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(candidate.Route, section.Route, StringComparison.OrdinalIgnoreCase) &&
                    NormalizeRouteForNavigationMatch(candidate.Route).StartsWith(sectionRoute, StringComparison.OrdinalIgnoreCase)),
                PageKind.Taxonomy => routes.Count(candidate =>
                    !candidate.Draft &&
                    candidate.Kind == PageKind.Term &&
                    string.Equals(candidate.Collection, section.Collection, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Language, section.Language, StringComparison.OrdinalIgnoreCase)),
                PageKind.Term => ResolveTaxonomyTermCount(taxonomyTermCountsByLanguage, section),
                _ => 0
            };
            var totalPages = totalItems <= 0 ? 1 : (int)Math.Ceiling(totalItems / (double)pageSize);
            for (var page = 2; page <= totalPages; page++)
            {
                yield return WebSiteBuilder.BuildPaginationRoute(
                    section.Route,
                    pageSegment,
                    page,
                    spec.TrailingSlash);
            }
        }
    }

    private static int ResolveTaxonomyTermCount(
        IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, int>>> taxonomyTermCountsByLanguage,
        CollectionRoute route)
    {
        if (string.IsNullOrWhiteSpace(route.TaxonomyTerm) ||
            !taxonomyTermCountsByLanguage.TryGetValue(route.Collection, out var countsByLanguage) ||
            !countsByLanguage.TryGetValue(route.Language, out var counts) ||
            !counts.TryGetValue(route.TaxonomyTerm, out var count))
        {
            return 0;
        }

        return count;
    }

    private static IEnumerable<string> DiscoverGeneratedOutputRoutes(
        SiteSpec spec,
        IEnumerable<CollectionRoute> contentRoutes)
    {
        foreach (var route in contentRoutes.Where(static route => route is not null && !route.Draft))
        {
            var item = new ContentItem
            {
                Collection = route.Collection,
                OutputPath = route.Route,
                Kind = route.Kind,
                Outputs = route.Outputs ?? Array.Empty<string>()
            };
            foreach (var format in WebSiteBuilder.ResolveOutputFormats(spec, item))
            {
                var outputRoute = WebSiteBuilder.ResolveOutputRoute(route.Route, format);
                if (!string.IsNullOrWhiteSpace(outputRoute))
                    yield return outputRoute;
            }
        }
    }

    private static IEnumerable<string> DiscoverGeneratedLocalizedFallbackRoutes(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        IEnumerable<CollectionRoute> contentRoutes)
    {
        if (!localization.Enabled ||
            !localization.FallbackToDefaultLanguage ||
            !localization.MaterializeFallbackPages ||
            localization.Languages.Length <= 1)
        {
            yield break;
        }

        var routes = contentRoutes.Where(static route => route is not null && !route.Draft).ToArray();
        var existingRouteLanguages = routes
            .Select(route => ResolveEffectiveLanguageCode(localization, route.Language) + "|" + NormalizeRouteForNavigationMatch(route.Route))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTranslations = routes
            .Where(static route => !string.IsNullOrWhiteSpace(route.TranslationKey))
            .Select(route => ResolveEffectiveLanguageCode(localization, route.Language) + "|" + route.TranslationKey.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in routes.Where(route =>
                     route.Kind is PageKind.Page or PageKind.Home or PageKind.Section &&
                     ResolveEffectiveLanguageCode(localization, route.Language)
                         .Equals(localization.DefaultLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            var strippedRoute = StripLanguagePrefix(localization, source.Route);
            foreach (var language in localization.Languages.Where(static language => !language.IsDefault))
            {
                if (!CollectionSupportsFallbackLanguage(spec, localization, source.Collection, language.Code))
                    continue;
                if (!string.IsNullOrWhiteSpace(source.TranslationKey) &&
                    existingTranslations.Contains(language.Code + "|" + source.TranslationKey.Trim()))
                {
                    continue;
                }

                var fallbackRoute = ApplyLanguagePrefixToRoute(spec, localization, strippedRoute, language.Code);
                if (existingRouteLanguages.Contains(language.Code + "|" + NormalizeRouteForNavigationMatch(fallbackRoute)))
                    continue;

                yield return fallbackRoute;
            }
        }
    }

    private static bool CollectionSupportsFallbackLanguage(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        string collectionName,
        string languageCode)
    {
        var collection = (spec.Collections ?? Array.Empty<CollectionSpec>())
            .Where(static candidate => candidate is not null)
            .Select(CollectionPresetDefaults.Apply)
            .FirstOrDefault(candidate => candidate.Name.Equals(collectionName, StringComparison.OrdinalIgnoreCase));
        if (collection?.MaterializeFallbackPages == false)
            return false;

        var configured = collection?.FallbackLanguages;
        if (configured is null || configured.Length == 0)
            return localization.ByCode.ContainsKey(languageCode);

        var supportedLanguages = configured
            .Select(NormalizeLanguageToken)
            .Where(static language => !string.IsNullOrWhiteSpace(language))
            .Where(localization.ByCode.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (supportedLanguages.Length == 0)
            return localization.ByCode.ContainsKey(languageCode);

        return supportedLanguages.Contains(NormalizeLanguageToken(languageCode), StringComparer.OrdinalIgnoreCase);
    }
}
