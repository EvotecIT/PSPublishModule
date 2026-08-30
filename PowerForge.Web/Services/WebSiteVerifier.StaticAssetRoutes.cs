namespace PowerForge.Web;

public static partial class WebSiteVerifier
{
    private static CollectionRoute ProjectBuilderContentRoute(ContentItem item)
    {
        var taxonomyTerm = item.Kind == PageKind.Term &&
                           item.Meta.TryGetValue("term", out var termValue)
            ? termValue?.ToString()
            : null;
        return new CollectionRoute(
            item.Collection,
            item.OutputPath,
            item.SourcePath,
            item.Draft,
            item.Language,
            item.TranslationKey ?? string.Empty,
            item.Kind,
            item.Outputs ?? Array.Empty<string>(),
            TaxonomyTerm: taxonomyTerm,
            Resources: item.Resources ?? Array.Empty<PageResource>(),
            ProjectSlug: item.ProjectSlug,
            SocialCardItem: item);
    }

    private static bool HasStaticAssetInputs(SiteSpec spec, string rootPath)
    {
        if ((spec.StaticAssets ?? Array.Empty<StaticAssetSpec>()).Any(static mapping =>
                mapping is not null && !string.IsNullOrWhiteSpace(mapping.Source)))
        {
            return true;
        }

        return !WebSiteBuilder.HasExplicitConventionalStaticMapping(spec, rootPath) &&
               Directory.Exists(Path.Combine(rootPath, "static"));
    }

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

    private static IEnumerable<string> DiscoverGeneratedRedirectRoutes(IEnumerable<RedirectSpec> redirects)
    {
        foreach (var redirect in redirects ?? Array.Empty<RedirectSpec>())
        {
            if (redirect is null ||
                redirect.MatchType != RedirectMatchType.Exact ||
                string.IsNullOrWhiteSpace(redirect.From) ||
                string.IsNullOrWhiteSpace(redirect.To) ||
                IsExternalNavigationUrl(redirect.From))
            {
                continue;
            }

            var status = redirect.Status <= 0 ? 301 : redirect.Status;
            if (status is >= 300 and < 400)
                yield return redirect.From;
        }
    }

    private static IEnumerable<string> DiscoverGeneratedSocialCardRoutes(
        SiteSpec spec,
        WebSitePlan plan,
        IEnumerable<ContentItem> contentItems)
    {
        if (spec.Social is not { Enabled: true, AutoGenerateCards: true })
            yield break;

        foreach (var item in contentItems.Where(static item => item is not null && !item.Draft))
        {
            if (!WebSiteBuilder.ResolveOutputFormats(spec, item).Any(WebSiteBuilder.RendersHtmlPage))
                continue;
            var cardRoute = WebSiteBuilder.ResolveGeneratedSocialCardRoute(spec, item, plan.RootPath);
            if (string.IsNullOrWhiteSpace(cardRoute))
                continue;

            if (WebSiteBuilder.TryGetGeneratedSocialCardRenderOutcome(cardRoute, plan.RootPath, out var rendered))
            {
                if (rendered)
                    yield return cardRoute;
                continue;
            }

            var conventionalOutputRoot = Path.GetFullPath(Path.Combine(plan.RootPath, "_site"));
            var relativeCardPath = cardRoute.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullCardPath = Path.GetFullPath(Path.Combine(conventionalOutputRoot, relativeCardPath));
            if (fullCardPath.StartsWith(conventionalOutputRoot + Path.DirectorySeparatorChar, FileSystemPathComparison) &&
                File.Exists(fullCardPath))
            {
                yield return cardRoute;
            }
        }
    }

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

    private static bool IsBuiltNotFoundRoute(string route)
    {
        var normalized = NormalizeRouteForNavigationMatch(route);
        return normalized.Equals("/404/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("/404", StringComparison.OrdinalIgnoreCase);
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
                        : normalizedBaseRoute + "/index.html";
                    foreach (var physicalRoute in GetStaticAssetRoutes(physicalOutput))
                        yield return physicalRoute;
                    continue;
                }
                var outputRoute = WebSiteBuilder.ResolveOutputRoute(route.Route, format);
                if (!string.IsNullOrWhiteSpace(outputRoute) &&
                    !NormalizeRouteForNavigationMatch(outputRoute).Equals(
                        NormalizeRouteForNavigationMatch(route.Route),
                        StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var physicalRoute in GetStaticAssetRoutes(outputRoute))
                        yield return physicalRoute;
                }
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
                var destination = string.IsNullOrWhiteSpace(baseRoute)
                    ? "/" + relative
                    : baseRoute + "/" + relative;
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
                var routeLanguageKey = language.Code + "|" + NormalizeRouteForNavigationMatch(fallbackRoute);
                if (!existingRouteLanguages.Add(routeLanguageKey))
                    continue;

                if (!string.IsNullOrWhiteSpace(source.TranslationKey))
                    existingTranslations.Add(language.Code + "|" + source.TranslationKey.Trim());

                yield return source with
                {
                    Route = fallbackRoute,
                    Language = language.Code,
                    Draft = false,
                    IsGeneratedFallback = true,
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
