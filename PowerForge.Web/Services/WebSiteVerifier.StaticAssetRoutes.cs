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

        foreach (var route in GetStaticAssetRoutes("search/index.html"))
            yield return route;
    }

    private static IEnumerable<string> DiscoverGeneratedSearchDataRoutes(
        IEnumerable<CollectionRoute> contentRoutes)
    {
        var routes = contentRoutes
            .Where(route => route is not null && WebSiteBuilder.IsSearchableContent(route.Draft, route.Route))
            .ToArray();
        if (routes.Length == 0)
            yield break;

        yield return "/search/index.json";
        yield return "/search/manifest.json";
        foreach (var language in routes
                     .Select(route => WebSiteBuilder.NormalizeLanguageToken(route.Language))
                     .Where(static language => !string.IsNullOrWhiteSpace(language))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return $"/search/{Uri.EscapeDataString(language)}/index.json";
        }
        foreach (var collection in routes
                     .Select(static route => route.Collection)
                     .Where(static collection => !string.IsNullOrWhiteSpace(collection))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var token = WebSiteBuilder.Slugify(collection);
            if (!string.IsNullOrWhiteSpace(token))
                yield return $"/search/collections/{Uri.EscapeDataString(token)}/index.json";
        }
    }

    private static IEnumerable<string> DiscoverGeneratedThemeAssetRoutes(SiteSpec spec, string rootPath)
    {
        foreach (var mapping in WebSiteBuilder.ResolveThemeAssetMappings(spec, rootPath))
        {
            foreach (var file in EnumerateStaticFiles(mapping.SourceRoot))
            {
                foreach (var route in GetStaticAssetRoutes(Path.Combine(mapping.DestinationRelativePath, file)))
                    yield return route;
            }
        }
    }

    private static IEnumerable<string> DiscoverGeneratedSiteDataRoutes(SiteSpec spec)
    {
        foreach (var route in GetStaticAssetRoutes(WebSiteBuilder.ResolveSiteNavRelativePath(spec)))
            yield return route;
    }

    private static IEnumerable<string> DiscoverGeneratedSocialCardRoutes(
        SiteSpec spec,
        WebSitePlan plan,
        IEnumerable<CollectionRoute> contentRoutes)
    {
        var renderedItems = spec.Social is { Enabled: true, AutoGenerateCards: true }
            ? WebSiteBuilder.BuildContentItemsForVerification(spec, plan)
                .GroupBy(static item => BuildSocialCardItemKey(item.SourcePath, item.Collection), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ContentItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in contentRoutes.Where(static route => route is not null && !route.Draft && route.SocialCardItem is not null))
        {
            var sourceItem = route.SocialCardItem!;
            var item = WebSiteBuilder.CloneContentItem(sourceItem);
            if (renderedItems.TryGetValue(BuildSocialCardItemKey(sourceItem.SourcePath, route.Collection), out var renderedItem))
            {
                item.Title = renderedItem.Title;
                item.Description = renderedItem.Description;
                item.HtmlContent = renderedItem.HtmlContent;
            }
            item.OutputPath = route.Route;
            item.Language = route.Language;
            item.TranslationKey = route.TranslationKey;
            item.Kind = route.Kind;
            item.Outputs = route.Outputs ?? Array.Empty<string>();
            if (!WebSiteBuilder.ResolveOutputFormats(spec, item).Any(EmitsIndexHtml))
                continue;
            var cardRoute = WebSiteBuilder.ResolveGeneratedSocialCardRoute(spec, item, plan.RootPath);
            if (!string.IsNullOrWhiteSpace(cardRoute))
                yield return cardRoute;
        }
    }

    private static string BuildSocialCardItemKey(string? sourcePath, string? collection) =>
        $"{sourcePath ?? string.Empty}\0{collection ?? string.Empty}";

    private static IEnumerable<CollectionRoute> DiscoverAutoGeneratedSectionRoutes(
        SiteSpec spec,
        ResolvedLocalizationConfig localization,
        IEnumerable<CollectionRoute> contentRoutes)
    {
        var routes = contentRoutes.Where(static route => route is not null && !route.Draft).ToArray();
        foreach (var collection in (spec.Collections ?? Array.Empty<CollectionSpec>())
                     .Where(static collection => collection is not null)
                     .Select(CollectionPresetDefaults.Apply)
                     .Where(static collection => collection.AutoGenerateSectionIndex))
        {
            var collectionItems = routes
                .Where(route => route.Collection.Equals(collection.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var projectGroup in collectionItems.GroupBy(static route => route.ProjectSlug ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var projectSlug = string.IsNullOrWhiteSpace(projectGroup.Key) ? null : projectGroup.Key;
                var baseOutput = ReplaceProjectPlaceholder(collection.Output, projectSlug);
                if (string.IsNullOrWhiteSpace(baseOutput))
                    continue;

                var languages = projectGroup
                    .Select(route => ResolveEffectiveLanguageCode(localization, route.Language))
                    .Where(static language => !string.IsNullOrWhiteSpace(language))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .DefaultIfEmpty(localization.DefaultLanguage);
                foreach (var language in languages)
                {
                    var expectedRoute = BuildRoute(baseOutput, string.Empty, spec.TrailingSlash);
                    expectedRoute = ApplyLanguagePrefixToRoute(spec, localization, expectedRoute, language);
                    if (projectGroup.Any(route =>
                            route.Kind is PageKind.Section or PageKind.Home or PageKind.Page &&
                            NormalizeRouteForNavigationMatch(route.Route).Equals(
                                NormalizeRouteForNavigationMatch(expectedRoute),
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var socialCardItem = WebSiteBuilder.CreateAutoGeneratedSectionIndexItem(
                        collection,
                        expectedRoute,
                        language,
                        projectSlug);
                    yield return new CollectionRoute(
                        collection.Name,
                        expectedRoute,
                        $"[generated:{collection.Name}]",
                        false,
                        language,
                        string.IsNullOrWhiteSpace(projectSlug)
                            ? $"{collection.Name}:_index"
                            : $"{collection.Name}:{projectSlug}/_index",
                        PageKind.Section,
                        collection.Outputs ?? Array.Empty<string>(),
                        ProjectSlug: projectSlug,
                        SocialCardItem: socialCardItem);
                }
            }
        }
    }

    private static string ResolveBuiltNavigationRoute(string route)
    {
        var normalized = NormalizeRouteForNavigationMatch(route);
        return normalized.Equals("/404/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("/404", StringComparison.OrdinalIgnoreCase)
            ? "/404.html"
            : route;
    }

    private static IEnumerable<CollectionRoute> DiscoverGeneratedPaginationRoutes(
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
            .Where(static collection => (collection.PageSize ?? 0) > 0)
            .ToDictionary(
                static collection => collection.Name,
                collection => collection.PageSize!.Value,
                StringComparer.OrdinalIgnoreCase);
        var taxonomyPageSizes = (spec.Taxonomies ?? Array.Empty<TaxonomySpec>())
            .Where(static taxonomy => taxonomy is not null && !string.IsNullOrWhiteSpace(taxonomy.Name))
            .Where(static taxonomy => (taxonomy.PageSize ?? 0) > 0)
            .ToDictionary(
                static taxonomy => taxonomy.Name,
                taxonomy => taxonomy.PageSize!.Value,
                StringComparer.OrdinalIgnoreCase);
        var knownRoutes = new HashSet<string>(
            routes.Select(static route => NormalizeRouteForNavigationMatch(route.Route)),
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
                var pagedRoute = WebSiteBuilder.BuildPaginationRoute(
                    section.Route,
                    pageSegment,
                    page,
                    spec.TrailingSlash);
                if (!knownRoutes.Add(NormalizeRouteForNavigationMatch(pagedRoute)))
                    continue;

                yield return section with
                {
                    Route = pagedRoute,
                    TranslationKey = string.IsNullOrWhiteSpace(section.TranslationKey)
                        ? string.Empty
                        : $"{section.TranslationKey}:page:{page}",
                    Outputs = ["html"]
                };
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
                if (EmitsIndexHtml(format))
                {
                    var normalizedBaseRoute = NormalizeRouteForNavigationMatch(route.Route).Trim('/');
                    if (normalizedBaseRoute.Equals("404", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return "/404.html";
                        continue;
                    }

                    var physicalOutput = string.IsNullOrWhiteSpace(normalizedBaseRoute)
                        ? "index.html"
                        : Uri.UnescapeDataString(normalizedBaseRoute) + "/index.html";
                    foreach (var physicalRoute in GetStaticAssetRoutes(physicalOutput))
                        yield return physicalRoute;
                    continue;
                }
                var outputRoute = WebSiteBuilder.ResolveOutputRoute(route.Route, format);
                if (!string.IsNullOrWhiteSpace(outputRoute) &&
                    !NormalizeRouteForNavigationMatch(outputRoute).Equals(
                        NormalizeRouteForNavigationMatch(route.Route),
                        StringComparison.OrdinalIgnoreCase))
                    yield return outputRoute;
            }
        }
    }

    private static IEnumerable<string> DiscoverGeneratedResourceRoutes(
        IEnumerable<CollectionRoute> contentRoutes)
    {
        foreach (var route in contentRoutes.Where(static route => route is not null && !route.Draft))
        {
            foreach (var resource in route.Resources ?? Array.Empty<PageResource>())
            {
                var relative = string.IsNullOrWhiteSpace(resource.RelativePath)
                    ? resource.Name
                    : resource.RelativePath;
                if (string.IsNullOrWhiteSpace(relative))
                    continue;

                var normalizedRoute = NormalizeRouteForNavigationMatch(route.Route).TrimEnd('/');
                var baseRoute = normalizedRoute.Equals("/404", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : normalizedRoute;
                var rawBaseRoute = Uri.UnescapeDataString(baseRoute);
                var destination = string.IsNullOrWhiteSpace(rawBaseRoute)
                    ? "/" + relative
                    : rawBaseRoute + "/" + relative;
                foreach (var resourceRoute in GetStaticAssetRoutes(destination))
                    yield return resourceRoute;
            }
        }
    }

    private static bool EmitsIndexHtml(SiteSpec spec, CollectionRoute route)
    {
        var item = new ContentItem
        {
            Collection = route.Collection,
            OutputPath = route.Route,
            Kind = route.Kind,
            Outputs = route.Outputs ?? Array.Empty<string>()
        };
        return WebSiteBuilder.ResolveOutputFormats(spec, item).Any(EmitsIndexHtml);
    }

    private static bool EmitsIndexHtml(OutputFormatSpec format)
        => format is not null &&
           (string.IsNullOrWhiteSpace(format.Suffix) ||
            format.Suffix.Equals("html", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<CollectionRoute> DiscoverGeneratedLocalizedFallbackRoutes(
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

                yield return source with
                {
                    Route = fallbackRoute,
                    Language = language.Code,
                    Draft = false,
                    SocialCardItem = source.SocialCardItem is null
                        ? null
                        : WebSiteBuilder.CloneFallbackItem(spec, source.SocialCardItem, fallbackRoute, language.Code)
                };
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
