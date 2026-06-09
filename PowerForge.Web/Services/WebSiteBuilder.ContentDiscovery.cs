using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PowerForge.Web;

/// <summary>Content discovery and item construction helpers.</summary>
public static partial class WebSiteBuilder
{
    // The git freshness cache maps full source paths to author dates; this marker
    // records that the one-shot bulk git query already ran for the current build.
    private const string GitLastModifiedCacheLoadedKey = "\0powerforge-git-lastmod-loaded";

    private static IEnumerable<ProjectSpec> LoadProjectSpecs(string? projectsRoot, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(projectsRoot) || !Directory.Exists(projectsRoot))
            yield break;

        foreach (var dir in Directory.GetDirectories(projectsRoot))
        {
            var projectFile = Path.Combine(dir, "project.json");
            if (!File.Exists(projectFile))
                continue;

            ProjectSpec? project = null;
            try
            {
                project = WebSiteSpecLoader.LoadProjectWithPath(projectFile, options).Spec;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Invalid project spec {projectFile}: {ex.GetType().Name}: {ex.Message}");
            }

            if (project is not null)
                yield return project;
        }
    }

    private static List<ContentItem> BuildContentItems(
        SiteSpec spec,
        WebSitePlan plan,
        List<RedirectSpec> redirects,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, ProjectSpec> projectMap,
        IReadOnlyDictionary<string, ProjectContentSpec> projectContentMap,
        string? cacheRoot)
    {
        var items = new List<ContentItem>();
        if (spec.Collections is null || spec.Collections.Length == 0)
            return items;

        var localization = ResolveLocalizationConfig(spec);
        var contentRoots = BuildContentRoots(plan);
        var gitLastModifiedCache = new Dictionary<string, DateTimeOffset?>(StringComparer.OrdinalIgnoreCase);

        foreach (var collection in spec.Collections)
        {
            if (collection is null) continue;
            var resolvedCollection = CollectionPresetDefaults.Apply(collection);
            var collectionItems = new List<ContentItem>();
            var include = resolvedCollection.Include;
            var exclude = resolvedCollection.Exclude;
            var themeRoot = ResolveThemeRoot(spec, plan.RootPath);
            var loader = new ThemeLoader();
            ThemeManifest? manifest = null;
            ITemplateEngine? shortcodeEngine = null;
            Func<string, string?>? partialResolver = null;
            if (!string.IsNullOrWhiteSpace(themeRoot) && Directory.Exists(themeRoot))
            {
                manifest = loader.Load(themeRoot, ResolveThemesRoot(spec, plan.RootPath));
                shortcodeEngine = ThemeEngineRegistry.Resolve(spec.ThemeEngine ?? manifest?.Engine);
                partialResolver = name =>
                {
                    var path = loader.ResolvePartialPath(themeRoot, manifest, name);
                    return path is null ? null : File.ReadAllText(path);
                };
            }

            var markdownFiles = EnumerateCollectionFiles(plan.RootPath, contentRoots, resolvedCollection.Input, include, exclude).ToList();
            var leafBundleRoots = BuildLeafBundleRoots(markdownFiles);

            foreach (var file in markdownFiles)
            {
                if (IsUnderAnyRoot(file, leafBundleRoots) && !IsLeafBundleIndex(file))
                    continue;

                var collectionRoot = ResolveCollectionRootForFile(plan.RootPath, contentRoots, resolvedCollection.Input, file);
                var markdown = File.ReadAllText(file);
                var (matter, body) = FrontMatterParser.Parse(markdown);
                var effectiveBody = IncludePreprocessor.Apply(body, plan.RootPath, file, collectionRoot);
                var projectSlug = ResolveProjectSlug(plan, file);
                projectMap.TryGetValue(projectSlug ?? string.Empty, out var projectSpec);
                var editUrl = EditLinkResolver.Resolve(spec, projectSpec, plan.RootPath, file, matter?.EditPath);
                if (matter is not null)
                    matter.EditUrl = editUrl;
                var dataForShortcodes = ResolveDataForProject(data, projectSlug);
                var shortcodeContext = new ShortcodeRenderContext
                {
                    Site = spec,
                    RootPath = plan.RootPath,
                    FrontMatter = matter,
                    EditUrl = editUrl,
                    Project = projectSpec,
                    SourcePath = file,
                    Data = dataForShortcodes,
                    ThemeManifest = manifest,
                    ThemeRoot = themeRoot,
                    Engine = shortcodeEngine,
                    PartialResolver = partialResolver
                };
                var processedBody = ShortcodeProcessor.Apply(effectiveBody, shortcodeContext);
                var skipMarkdown = false;
                if (TryRenderDataOverride(matter?.Meta, shortcodeContext, out var dataOverride, out var dataMode))
                {
                    var mode = NormalizeDataRenderMode(dataMode);
                    if (mode == DataRenderMode.Append)
                    {
                        processedBody = string.IsNullOrWhiteSpace(processedBody)
                            ? dataOverride
                            : $"{processedBody}{Environment.NewLine}{dataOverride}";
                    }
                    else if (mode == DataRenderMode.Prepend)
                    {
                        processedBody = string.IsNullOrWhiteSpace(processedBody)
                            ? dataOverride
                            : $"{dataOverride}{Environment.NewLine}{processedBody}";
                    }
                    else
                    {
                        processedBody = dataOverride;
                        skipMarkdown = true;
                    }
                }

                processedBody = ApplyContentTemplate(processedBody, matter?.Meta, shortcodeContext);

                var title = matter?.Title ?? FrontMatterParser.ExtractTitleFromMarkdown(processedBody) ?? Path.GetFileNameWithoutExtension(file);
                var description = matter?.Description ?? string.Empty;
                var relativePath = ResolveRelativePath(collectionRoot, file);
                var resolvedLanguage = ResolveItemLanguage(spec, relativePath, matter, out var localizedRelativePath, out var localizedRelativeDir);
                var resolvedAliases = ResolveAliasesForLanguage(matter, resolvedLanguage, localization);
                var relativeDir = localizedRelativeDir;
                var isSectionIndex = IsSectionIndex(file);
                var isBundleIndex = IsLeafBundleIndex(file);
                var slugPath = ResolveSlugPath(localizedRelativePath, relativeDir, matter?.Slug);
                if (isSectionIndex || isBundleIndex)
                    slugPath = ApplySlugOverride(relativeDir, matter?.Slug);
                var baseOutput = ReplaceProjectPlaceholder(resolvedCollection.Output, projectSlug);
                var route = BuildRoute(baseOutput, slugPath, spec.TrailingSlash);
                route = ApplyLanguagePrefixToRoute(spec, route, resolvedLanguage);
                var kind = ResolvePageKind(route, resolvedCollection, isSectionIndex);
                var layout = matter?.Layout;
                if (string.IsNullOrWhiteSpace(layout))
                {
                    layout = kind == PageKind.Section
                        ? (string.IsNullOrWhiteSpace(resolvedCollection.ListLayout) ? resolvedCollection.DefaultLayout : resolvedCollection.ListLayout)
                        : resolvedCollection.DefaultLayout;
                }

                if (resolvedAliases.Length > 0)
                {
                    var seenAliasSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var alias in resolvedAliases)
                    {
                        if (string.IsNullOrWhiteSpace(alias)) continue;
                        foreach (var aliasSource in ExpandAliasRedirectSources(alias))
                        {
                            if (!seenAliasSources.Add(aliasSource))
                                continue;
                            if (IsAliasRedirectSourceEquivalentToRoute(aliasSource, route))
                                continue;
                            redirects.Add(new RedirectSpec
                            {
                                From = aliasSource,
                                To = route,
                                Status = 301,
                                MatchType = RedirectMatchType.Exact,
                                PreserveQuery = true
                            });
                        }
                    }
                }

                var renderMarkdown = ShouldRenderMarkdown(matter?.Meta);
                if (skipMarkdown)
                    renderMarkdown = false;
                var htmlContent = renderMarkdown
                    ? RenderMarkdown(processedBody, file, spec.Cache, cacheRoot, spec.Markdown)
                    : processedBody;
                var meta = matter?.Meta ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                htmlContent = NormalizeCodeBlockClasses(htmlContent, ResolvePrismDefaultLanguage(meta, spec));
                EnsurePrismAssets(meta, htmlContent, spec, plan.RootPath);
                var toc = BuildTableOfContents(htmlContent);
                if (resolvedCollection.Name.Equals("projects", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(projectSlug) &&
                    projectContentMap.TryGetValue(projectSlug, out var perProject))
                {
                    var projectInclude = perProject.Include;
                    var projectExclude = perProject.Exclude;
                    if (!MatchesFile(plan.RootPath, contentRoots, file, resolvedCollection.Input, projectInclude, projectExclude))
                        continue;
                }
                if (!string.IsNullOrWhiteSpace(resolvedCollection.Preset) &&
                    !TryGetMetaValue(meta, "collection_preset", out _))
                {
                    meta["collection_preset"] = resolvedCollection.Preset;
                }
                var contentItem = new ContentItem
                {
                    SourcePath = file,
                    Collection = resolvedCollection.Name,
                    OutputPath = route,
                    Language = resolvedLanguage,
                    TranslationKey = ResolveTranslationKey(matter, resolvedCollection.Name, localizedRelativePath),
                    Title = title,
                    Description = description,
                    Date = matter?.Date,
                    LastModifiedUtc = ResolveContentLastModifiedUtc(plan.RootPath, file, matter, meta, resolvedCollection, gitLastModifiedCache),
                    Order = matter?.Order,
                    Slug = slugPath,
                    Tags = matter?.Tags ?? Array.Empty<string>(),
                    Categories = ResolveCategoriesFromFrontMatter(matter),
                    Aliases = resolvedAliases,
                    Draft = matter?.Draft ?? false,
                    Canonical = matter?.Canonical,
                    EditPath = matter?.EditPath,
                    EditUrl = editUrl,
                    Layout = layout,
                    Template = matter?.Template,
                    Kind = kind,
                    HtmlContent = htmlContent,
                    TocHtml = toc,
                    Resources = isSectionIndex || isBundleIndex
                        ? BuildBundleResources(Path.GetDirectoryName(file) ?? string.Empty)
                        : Array.Empty<PageResource>(),
                    ProjectSlug = projectSlug,
                    Meta = meta,
                    Outputs = ResolveOutputs(matter?.Meta, resolvedCollection)
                };
                items.Add(contentItem);
                collectionItems.Add(contentItem);
            }

            AddAutoGeneratedSectionIndexes(spec, resolvedCollection, collectionItems, items);
        }

        return items;
    }

    private static List<ContentItem> MaterializeLocalizedFallbackPages(SiteSpec spec, IReadOnlyList<ContentItem> items)
    {
        if (spec is null || items is null || items.Count == 0)
            return items?.ToList() ?? new List<ContentItem>();

        var localization = ResolveLocalizationConfig(spec);
        if (!localization.Enabled ||
            !localization.FallbackToDefaultLanguage ||
            !localization.MaterializeFallbackPages ||
            localization.Languages.Length <= 1)
        {
            return items.ToList();
        }

        var output = items.ToList();
        var defaultLanguageBaseUrl = ResolveLanguageBaseUrl(spec, localization, localization.DefaultLanguage);
        var existingRouteLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingTranslations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.Where(static value => value is not null && !value.Draft))
        {
            existingRouteLanguages.Add(BuildRouteLanguageKey(
                item.OutputPath,
                ResolveEffectiveLanguageCode(localization, item.Language)));
            if (!string.IsNullOrWhiteSpace(item.TranslationKey))
                existingTranslations.Add(BuildTranslationLanguageKey(item.TranslationKey!, ResolveEffectiveLanguageCode(localization, item.Language)));
        }

        var defaultLanguageItems = items
            .Where(static item => item is not null && !item.Draft)
            .Where(item => ResolveEffectiveLanguageCode(localization, item.Language)
                .Equals(localization.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            .Where(static item => item.Kind is PageKind.Page or PageKind.Home or PageKind.Section)
            .ToList();

        foreach (var source in defaultLanguageItems)
        {
            var strippedRoute = StripLanguagePrefix(localization, source.OutputPath);
            if (string.IsNullOrWhiteSpace(strippedRoute))
                strippedRoute = "/";

            foreach (var language in localization.Languages)
            {
                if (language.IsDefault)
                    continue;

                var targetLanguage = ResolveEffectiveLanguageCode(localization, language.Code);
                if (!CollectionSupportsFallbackLanguage(spec, localization, source.Collection, targetLanguage))
                    continue;
                if (!string.IsNullOrWhiteSpace(source.TranslationKey))
                {
                    var translationKey = BuildTranslationLanguageKey(source.TranslationKey!, targetLanguage);
                    if (existingTranslations.Contains(translationKey))
                        continue;
                }

                var fallbackRoute = ApplyLanguagePrefixToRoute(spec, strippedRoute, targetLanguage);
                var fallbackRouteLanguage = BuildRouteLanguageKey(fallbackRoute, targetLanguage);
                if (existingRouteLanguages.Contains(fallbackRouteLanguage))
                    continue;

                var fallback = CloneFallbackItem(
                    source,
                    fallbackRoute,
                    targetLanguage,
                    localization.DefaultLanguage,
                    defaultLanguageBaseUrl);
                output.Add(fallback);
                existingRouteLanguages.Add(fallbackRouteLanguage);
                if (!string.IsNullOrWhiteSpace(fallback.TranslationKey))
                    existingTranslations.Add(BuildTranslationLanguageKey(fallback.TranslationKey!, targetLanguage));
            }
        }

        return output;
    }

    private static string BuildRouteLanguageKey(string? route, string? language)
    {
        var normalizedRoute = NormalizeRouteForMatch(route);
        var normalizedLanguage = NormalizeLanguageToken(language);
        return normalizedLanguage + "|" + normalizedRoute;
    }

    private static string BuildTranslationLanguageKey(string translationKey, string language)
    {
        var key = string.IsNullOrWhiteSpace(translationKey) ? string.Empty : translationKey.Trim().ToLowerInvariant();
        var lang = string.IsNullOrWhiteSpace(language) ? string.Empty : language.Trim().ToLowerInvariant();
        return key + "|" + lang;
    }

    private static ContentItem CloneFallbackItem(
        ContentItem source,
        string outputPath,
        string targetLanguage,
        string defaultLanguage,
        string? defaultLanguageBaseUrl)
    {
        return new ContentItem
        {
            SourcePath = source.SourcePath,
            Collection = source.Collection,
            OutputPath = outputPath,
            Language = targetLanguage,
            TranslationKey = source.TranslationKey,
            Title = source.Title,
            Description = source.Description,
            Date = source.Date,
            LastModifiedUtc = source.LastModifiedUtc,
            Order = source.Order,
            Slug = source.Slug,
            Tags = source.Tags?.ToArray() ?? Array.Empty<string>(),
            Categories = source.Categories?.ToArray() ?? Array.Empty<string>(),
            Aliases = Array.Empty<string>(),
            Draft = false,
            // Materialized fallback pages should be treated as real localized routes.
            // Let the normal head-generation path self-canonicalize to the localized output route
            // instead of inheriting the default-language canonical.
            Canonical = null,
            EditPath = source.EditPath,
            EditUrl = source.EditUrl,
            Layout = source.Layout,
            Template = source.Template,
            Kind = source.Kind,
            HtmlContent = source.HtmlContent,
            TocHtml = source.TocHtml,
            Resources = source.Resources?.Select(static resource => new PageResource
            {
                SourcePath = resource.SourcePath,
                Name = resource.Name,
                RelativePath = resource.RelativePath,
                MediaType = resource.MediaType
            }).ToArray() ?? Array.Empty<PageResource>(),
            ProjectSlug = source.ProjectSlug,
            Meta = CloneFallbackMeta(source.Meta, defaultLanguage, targetLanguage),
            Outputs = source.Outputs?.ToArray() ?? Array.Empty<string>()
        };
    }

    private static Dictionary<string, object?> CloneFallbackMeta(
        IReadOnlyDictionary<string, object?>? source,
        string defaultLanguage,
        string targetLanguage)
    {
        var clone = source is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : source.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        clone["i18n.fallback_copy"] = true;
        clone["i18n.fallback_source_language"] = defaultLanguage;
        clone["i18n.requested_language"] = targetLanguage;
        return clone;
    }

    private static void AddAutoGeneratedSectionIndexes(
        SiteSpec spec,
        CollectionSpec collection,
        IReadOnlyList<ContentItem> collectionItems,
        List<ContentItem> allItems)
    {
        if (spec is null || collection is null || collectionItems is null || allItems is null)
            return;
        if (!collection.AutoGenerateSectionIndex)
            return;
        if (collectionItems.Count == 0)
            return;

        var publishedItems = collectionItems
            .Where(static item => item is not null && !item.Draft)
            .ToList();
        if (publishedItems.Count == 0)
            return;

        var localization = ResolveLocalizationConfig(spec);
        var groupedByProject = publishedItems
            .GroupBy(item => item.ProjectSlug ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        foreach (var projectGroup in groupedByProject)
        {
            var projectSlug = string.IsNullOrWhiteSpace(projectGroup.Key) ? null : projectGroup.Key;
            var baseOutput = ReplaceProjectPlaceholder(collection.Output, projectSlug);
            if (string.IsNullOrWhiteSpace(baseOutput))
                continue;

            var languages = projectGroup
                .Select(item => ResolveEffectiveLanguageCode(localization, item.Language))
                .Where(language => !string.IsNullOrWhiteSpace(language))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (languages.Count == 0)
                languages.Add(localization.DefaultLanguage);

            foreach (var language in languages)
            {
                var expectedRoute = BuildRoute(baseOutput, string.Empty, spec.TrailingSlash);
                expectedRoute = ApplyLanguagePrefixToRoute(spec, expectedRoute, language);
                var normalizedExpected = NormalizeRouteForMatch(expectedRoute);

                var hasLanding = projectGroup
                    .Where(item => item.Kind == PageKind.Section || item.Kind == PageKind.Home || item.Kind == PageKind.Page)
                    .Select(item => NormalizeRouteForMatch(item.OutputPath))
                    .Any(route => string.Equals(route, normalizedExpected, StringComparison.OrdinalIgnoreCase));
                if (hasLanding)
                    continue;

                var generatedTitle = string.IsNullOrWhiteSpace(collection.AutoSectionTitle)
                    ? HumanizeSegment(collection.Name)
                    : collection.AutoSectionTitle!.Trim();
                var generatedDescription = string.IsNullOrWhiteSpace(collection.AutoSectionDescription)
                    ? string.Empty
                    : collection.AutoSectionDescription!.Trim();

                allItems.Add(new ContentItem
                {
                    SourcePath = $"[generated:{collection.Name}]",
                    Collection = collection.Name,
                    OutputPath = expectedRoute,
                    Language = language,
                    TranslationKey = string.IsNullOrWhiteSpace(projectSlug)
                        ? $"{collection.Name}:_index"
                        : $"{collection.Name}:{projectSlug}/_index",
                    Title = generatedTitle,
                    Description = generatedDescription,
                    LastModifiedUtc = MaxLastModifiedUtc(projectGroup
                        .Where(item => ResolveEffectiveLanguageCode(localization, item.Language).Equals(language, StringComparison.OrdinalIgnoreCase))
                        .Select(static item => item.LastModifiedUtc)),
                    Slug = "index",
                    Kind = PageKind.Section,
                    Layout = string.IsNullOrWhiteSpace(collection.ListLayout) ? collection.DefaultLayout : collection.ListLayout,
                    ProjectSlug = projectSlug,
                    Meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["auto_generated_section_index"] = true
                    },
                    Outputs = collection.Outputs
                });
            }
        }
    }

    private static List<ContentItem> BuildTaxonomyItems(SiteSpec spec, IReadOnlyList<ContentItem> items)
    {
        var results = new List<ContentItem>();
        if (spec.Taxonomies is null || spec.Taxonomies.Length == 0)
            return results;

        var localization = ResolveLocalizationConfig(spec);
        var sourceItems = items
            .Where(i => i.Kind == PageKind.Page || i.Kind == PageKind.Home)
            .Where(i => !i.Draft)
            .ToList();

        foreach (var taxonomy in spec.Taxonomies)
        {
            if (taxonomy is null || string.IsNullOrWhiteSpace(taxonomy.Name) || string.IsNullOrWhiteSpace(taxonomy.BasePath))
                continue;

            var termMap = new Dictionary<string, Dictionary<string, List<ContentItem>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in sourceItems)
            {
                var language = ResolveEffectiveLanguageCode(localization, item.Language);
                if (!termMap.TryGetValue(language, out var languageMap))
                {
                    languageMap = new Dictionary<string, List<ContentItem>>(StringComparer.OrdinalIgnoreCase);
                    termMap[language] = languageMap;
                }

                foreach (var term in GetTaxonomyValues(item, taxonomy))
                {
                    if (!languageMap.TryGetValue(term, out var list))
                    {
                        list = new List<ContentItem>();
                        languageMap[term] = list;
                    }
                    list.Add(item);
                }
            }

            foreach (var languageEntry in termMap)
            {
                var language = languageEntry.Key;
                var languageTerms = languageEntry.Value;
                if (languageTerms.Count == 0)
                    continue;

                var taxRoute = BuildRoute(taxonomy.BasePath, string.Empty, spec.TrailingSlash);
                taxRoute = ApplyLanguagePrefixToRoute(spec, taxRoute, language);
                results.Add(new ContentItem
                {
                    Collection = taxonomy.Name,
                    OutputPath = taxRoute,
                    Language = language,
                    TranslationKey = $"taxonomy:{taxonomy.Name}",
                    Title = HumanizeSegment(taxonomy.Name),
                    Description = string.Empty,
                    LastModifiedUtc = MaxLastModifiedUtc(languageTerms
                        .SelectMany(static term => term.Value)
                        .Select(static item => item.LastModifiedUtc)),
                    Kind = PageKind.Taxonomy,
                    Layout = taxonomy.ListLayout ?? "taxonomy",
                    Outputs = taxonomy.Outputs,
                    Meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["taxonomy"] = taxonomy.Name
                    }
                });

                foreach (var term in languageTerms.Keys.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                {
                    var slug = Slugify(term);
                    var termRoute = BuildRoute(taxonomy.BasePath, slug, spec.TrailingSlash);
                    termRoute = ApplyLanguagePrefixToRoute(spec, termRoute, language);
                    results.Add(new ContentItem
                    {
                        Collection = taxonomy.Name,
                        OutputPath = termRoute,
                        Language = language,
                        TranslationKey = $"taxonomy:{taxonomy.Name}:term:{slug}",
                        Title = term,
                        Description = string.Empty,
                        LastModifiedUtc = MaxLastModifiedUtc(languageTerms[term]
                            .Select(static item => item.LastModifiedUtc)),
                        Kind = PageKind.Term,
                        Layout = taxonomy.TermLayout ?? "term",
                        Outputs = taxonomy.Outputs,
                        Meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["taxonomy"] = taxonomy.Name,
                            ["term"] = term
                        }
                    });
                }
            }
        }

        return results;
    }

    private static IEnumerable<string> GetTaxonomyValues(ContentItem item, TaxonomySpec taxonomy)
    {
        if (taxonomy.Name.Equals("tags", StringComparison.OrdinalIgnoreCase))
            return item.Tags ?? Array.Empty<string>();

        if (taxonomy.Name.Equals("categories", StringComparison.OrdinalIgnoreCase) &&
            item.Categories is { Length: > 0 })
        {
            return item.Categories;
        }

        if (item.Meta is not null && TryGetMetaValue(item.Meta, taxonomy.Name, out var value))
        {
            if (value is IEnumerable<object?> list)
                return list.Select(v => v?.ToString() ?? string.Empty)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray();

            if (value is string s && !string.IsNullOrWhiteSpace(s))
                return new[] { s };
        }

        return Array.Empty<string>();
    }

    private static string[] ResolveCategoriesFromFrontMatter(FrontMatter? matter)
    {
        if (matter is null)
            return Array.Empty<string>();

        if (matter.Categories is { Length: > 0 })
        {
            return matter.Categories
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (matter.Meta is null || matter.Meta.Count == 0)
            return Array.Empty<string>();

        if (!TryGetMetaValue(matter.Meta, "categories", out var value) || value is null)
            return Array.Empty<string>();

        if (value is string single)
        {
            if (string.IsNullOrWhiteSpace(single))
                return Array.Empty<string>();

            if (single.Contains(',', StringComparison.Ordinal))
            {
                return single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static token => !string.IsNullOrWhiteSpace(token))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            return new[] { single.Trim() };
        }

        if (value is IEnumerable<object?> list)
        {
            return list.Select(static entry => entry?.ToString() ?? string.Empty)
                .Where(static entry => !string.IsNullOrWhiteSpace(entry))
                .Select(static entry => entry.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : new[] { text.Trim() };
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
    {
        return Path.GetFileName(filePath).Equals("index.md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSectionIndex(string filePath)
    {
        return Path.GetFileName(filePath).Equals("_index.md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderAnyRoot(string filePath, HashSet<string> roots)
    {
        if (roots.Count == 0) return false;
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (filePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                filePath.Equals(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string[] BuildContentRoots(WebSitePlan plan)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.ContentRoot))
            roots.Add(Path.GetFullPath(plan.ContentRoot));

        if (plan.ContentRoots is { Length: > 0 })
        {
            roots.AddRange(plan.ContentRoots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath));
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveRelativePath(string? collectionRoot, string filePath)
    {
        if (string.IsNullOrWhiteSpace(collectionRoot))
            return Path.GetFileName(filePath);
        return Path.GetRelativePath(collectionRoot, filePath).Replace('\\', '/');
    }

    private static string ResolveSlugPath(string relativePath, string relativeDir, string? slugOverride)
    {
        var withoutExtension = NormalizePath(Path.ChangeExtension(relativePath, null) ?? string.Empty);
        return ApplySlugOverride(withoutExtension, slugOverride);
    }

    private static string ApplySlugOverride(string basePath, string? slugOverride)
    {
        if (string.IsNullOrWhiteSpace(slugOverride))
            return basePath;

        var normalized = NormalizePath(slugOverride);
        if (normalized.Contains('/'))
            return normalized;

        if (string.IsNullOrWhiteSpace(basePath))
            return normalized;

        var idx = basePath.LastIndexOf('/');
        if (idx < 0)
            return normalized;

        var parent = basePath.Substring(0, idx);
        if (string.IsNullOrWhiteSpace(parent))
            return normalized;

        return parent + "/" + normalized;
    }

    private static string? ResolveCollectionRootForFile(string rootPath, string[] contentRoots, string input, string filePath)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        foreach (var full in BuildCollectionInputCandidates(rootPath, contentRoots, input))
        {
            if (!full.Contains('*', StringComparison.Ordinal))
            {
                var candidateRoot = Path.GetFullPath(full);
                if (IsPathWithinBase(candidateRoot, filePath))
                    return candidateRoot;
                continue;
            }

            var normalized = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var parts = normalized.Split('*');
            if (parts.Length != 2)
                continue;

            var basePath = parts[0].TrimEnd(Path.DirectorySeparatorChar);
            var tail = parts[1].TrimStart(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(tail))
                continue;

            if (!IsPathWithinBase(basePath, filePath))
                continue;

            var relative = Path.GetRelativePath(basePath, filePath);
            var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;

            var wildcardSegment = segments[0];
            var candidate = Path.Combine(basePath, wildcardSegment, tail);
            return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static string ReplaceProjectPlaceholder(string output, string? projectSlug)
    {
        if (string.IsNullOrWhiteSpace(output))
            return output;
        if (string.IsNullOrWhiteSpace(projectSlug))
            return output.Replace("{project}", string.Empty, StringComparison.OrdinalIgnoreCase);
        return output.Replace("{project}", projectSlug, StringComparison.OrdinalIgnoreCase);
    }

    private static PageKind ResolvePageKind(string route, CollectionSpec collection, bool isSectionIndex)
    {
        if (isSectionIndex) return PageKind.Section;
        if (string.Equals(route, "/", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(collection.Output, "/", StringComparison.OrdinalIgnoreCase))
            return PageKind.Home;
        return PageKind.Page;
    }

    private static PageResource[] BuildBundleResources(string bundleRoot)
    {
        if (string.IsNullOrWhiteSpace(bundleRoot) || !Directory.Exists(bundleRoot))
            return Array.Empty<PageResource>();

        var resources = new List<PageResource>();
        foreach (var file in Directory.EnumerateFiles(bundleRoot, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = Path.GetRelativePath(bundleRoot, file).Replace('\\', '/');
            resources.Add(new PageResource
            {
                SourcePath = file,
                Name = Path.GetFileName(file),
                RelativePath = relative,
                MediaType = ResolveMediaType(file)
            });
        }

        return resources.ToArray();
    }

    private static string? ResolveMediaType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html",
            ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".json" => "application/json",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".ico" => "image/x-icon",
            _ => null
        };
    }

    private static string[] ResolveAliasesForLanguage(
        FrontMatter? matter,
        string resolvedLanguage,
        ResolvedLocalizationConfig localization)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddAlias(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return;

            if (seen.Add(trimmed))
                values.Add(trimmed);
        }

        void AddAliases(IEnumerable<string> aliases)
        {
            foreach (var alias in aliases)
                AddAlias(alias);
        }

        if (matter?.Aliases is { Length: > 0 })
            AddAliases(matter.Aliases);

        if (matter?.Meta is null || matter.Meta.Count == 0)
            return values.ToArray();

        var language = ResolveEffectiveLanguageCode(localization, resolvedLanguage);
        foreach (var key in BuildLocalizedAliasMetaKeys(language))
            AddAliases(ReadMetaAliases(matter.Meta, key));

        return values.ToArray();
    }

    private static IEnumerable<string> BuildLocalizedAliasMetaKeys(string languageCode)
    {
        var normalized = NormalizeLanguageToken(languageCode);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalized };
        if (normalized.Contains('-', StringComparison.Ordinal))
            variants.Add(normalized.Replace('-', '_'));
        if (normalized.Contains('_', StringComparison.Ordinal))
            variants.Add(normalized.Replace('_', '-'));

        yield return "i18n.aliases.default";
        yield return "i18n.aliases.all";
        yield return "aliases.default";
        yield return "aliases.all";
        foreach (var variant in variants)
        {
            yield return $"i18n.aliases.{variant}";
            yield return $"aliases.{variant}";
            yield return $"translations.{variant}.aliases";
            yield return $"translations.{variant}.alias";
        }
    }

    private static IEnumerable<string> ReadMetaAliases(Dictionary<string, object?> meta, string key)
    {
        if (meta is null || string.IsNullOrWhiteSpace(key))
            return Array.Empty<string>();
        if (!TryGetMetaValue(meta, key, out var value) || value is null)
            return Array.Empty<string>();

        if (value is string single)
            return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };

        if (value is IReadOnlyDictionary<string, object?>)
            return Array.Empty<string>();

        if (value is IEnumerable<object?> list)
        {
            return list
                .Select(v => v?.ToString() ?? string.Empty)
                .Where(static v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
        }

        var fallback = value.ToString();
        return string.IsNullOrWhiteSpace(fallback) ? Array.Empty<string>() : new[] { fallback };
    }

    private static string[] ResolveOutputs(Dictionary<string, object?>? meta, CollectionSpec collection)
    {
        var outputs = TryGetMetaStringList(meta, "outputs");
        if (outputs.Length > 0)
            return outputs;
        if (collection.Outputs.Length > 0)
            return collection.Outputs;
        return Array.Empty<string>();
    }

    private static string[] TryGetMetaStringList(Dictionary<string, object?>? meta, string key)
    {
        if (meta is null || meta.Count == 0) return Array.Empty<string>();
        if (!TryGetMetaValue(meta, key, out var value) || value is null)
            return Array.Empty<string>();

        if (value is IEnumerable<object?> list)
        {
            return list.Select(v => v?.ToString() ?? string.Empty)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
        }

        if (value is string s && !string.IsNullOrWhiteSpace(s))
            return new[] { s };

        return Array.Empty<string>();
    }

    private static DateTimeOffset? ResolveContentLastModifiedUtc(
        string rootPath,
        string sourcePath,
        FrontMatter? matter,
        IReadOnlyDictionary<string, object?>? meta,
        CollectionSpec? collection,
        Dictionary<string, DateTimeOffset?> gitLastModifiedCache)
    {
        if (TryReadMetaDateOffset(meta, out var explicitLastModified,
                "lastmod",
                "last_modified",
                "lastModified",
                "modified",
                "updated",
                "updated_at",
                "updatedAt",
                "dateModified",
                "date_modified",
                "seo.lastmod",
                "sitemap.lastmod",
                "sitemap.lastModified"))
        {
            return explicitLastModified.ToUniversalTime();
        }

        var published = matter?.Date is DateTime date
            ? NormalizeDateTimeOffset(date)
            : (DateTimeOffset?)null;

        var policy = ResolveSitemapLastModifiedPolicy(collection);
        if (policy == SitemapLastModifiedPolicy.Auto &&
            CollectionPresetDefaults.IsEditorialCollection(collection, collection?.Name))
        {
            policy = SitemapLastModifiedPolicy.PublishedDate;
        }
        else if (policy == SitemapLastModifiedPolicy.Auto)
        {
            policy = SitemapLastModifiedPolicy.SourceDate;
        }

        if (policy == SitemapLastModifiedPolicy.ExplicitOnly)
            return null;

        if (policy == SitemapLastModifiedPolicy.PublishedDate)
        {
            if (published.HasValue)
                return published.Value;
            if (TryGetGitLastModifiedUtc(rootPath, sourcePath, gitLastModifiedCache, out var fallbackGitLastModified))
                return fallbackGitLastModified;
            return null;
        }

        if (TryGetGitLastModifiedUtc(rootPath, sourcePath, gitLastModifiedCache, out var gitLastModified))
            return gitLastModified;

        return published;
    }

    private static DateTimeOffset? MaxLastModifiedUtc(IEnumerable<DateTimeOffset?> values)
    {
        DateTimeOffset? max = null;
        foreach (var value in values)
        {
            if (!value.HasValue)
                continue;
            if (!max.HasValue || value.Value > max.Value)
                max = value.Value;
        }

        return max;
    }

    private static SitemapLastModifiedPolicy ResolveSitemapLastModifiedPolicy(CollectionSpec? collection) =>
        collection?.SitemapLastModified ?? SitemapLastModifiedPolicy.Auto;

    private static bool TryReadMetaDateOffset(
        IReadOnlyDictionary<string, object?>? meta,
        out DateTimeOffset value,
        params string[] keys)
    {
        value = default;
        if (meta is null || meta.Count == 0)
            return false;

        var dictionary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in meta)
        {
            if (!dictionary.ContainsKey(pair.Key))
                dictionary[pair.Key] = pair.Value;
        }

        foreach (var key in keys)
        {
            if (!TryGetMetaValue(dictionary, key, out var raw) || raw is null)
                continue;
            if (TryConvertToDateTimeOffset(raw, out value))
                return true;
        }

        return false;
    }

    private static bool TryConvertToDateTimeOffset(object value, out DateTimeOffset date)
    {
        date = default;
        switch (value)
        {
            case DateTimeOffset dto:
                date = dto;
                return true;
            case DateTime dt:
                date = NormalizeDateTimeOffset(dt);
                return true;
            default:
                var text = value.ToString();
                if (string.IsNullOrWhiteSpace(text))
                    return false;
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    date = parsed;
                    return true;
                }

                return false;
        }
    }

    private static DateTimeOffset NormalizeDateTimeOffset(DateTime value)
    {
        if (value.Kind == DateTimeKind.Unspecified)
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        return new DateTimeOffset(value).ToUniversalTime();
    }

    private static bool TryGetGitLastModifiedUtc(
        string rootPath,
        string sourcePath,
        Dictionary<string, DateTimeOffset?> cache,
        out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(sourcePath))
            return false;

        var fullSource = Path.GetFullPath(sourcePath);
        if (cache.TryGetValue(fullSource, out var cached))
        {
            if (cached.HasValue)
                value = cached.Value;
            return cached.HasValue;
        }

        try
        {
            var fullRoot = Path.GetFullPath(rootPath);
            var relative = Path.GetRelativePath(fullRoot, fullSource).Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
            {
                cache[fullSource] = null;
                return false;
            }

            EnsureGitLastModifiedCache(fullRoot, cache);
            if (cache.TryGetValue(fullSource, out cached) && cached.HasValue)
            {
                value = cached.Value;
                return true;
            }
        }
        catch
        {
            // Git freshness is optional; fall back to other signals.
        }

        cache[fullSource] = null;
        return false;
    }

    private static void EnsureGitLastModifiedCache(string rootPath, Dictionary<string, DateTimeOffset?> cache)
    {
        if (cache.ContainsKey(GitLastModifiedCacheLoadedKey))
            return;

        cache[GitLastModifiedCacheLoadedKey] = null;
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return;

        try
        {
            using var process = new Process();
            var outputBuffer = new StringBuilder();
            var stdoutClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderrClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null)
                    stdoutClosed.TrySetResult();
                else
                    outputBuffer.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                    stderrClosed.TrySetResult();
            };
            process.StartInfo.FileName = "git";
            process.StartInfo.WorkingDirectory = rootPath;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("core.quotePath=false");
            process.StartInfo.ArgumentList.Add("log");
            process.StartInfo.ArgumentList.Add("--format=@@POWERFORGE_DATE@@%aI");
            process.StartInfo.ArgumentList.Add("--name-only");
            process.StartInfo.ArgumentList.Add("--diff-filter=AMR");
            process.StartInfo.ArgumentList.Add("--");
            process.StartInfo.ArgumentList.Add(".");
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(15000))
            {
                Trace.TraceWarning($"Timed out while reading git history for sitemap freshness under '{rootPath}'. Sitemap lastmod values will use explicit metadata or be omitted.");
                try { process.Kill(entireProcessTree: true); } catch { }
                return;
            }

            var drainTask = Task.WhenAll(stdoutClosed.Task, stderrClosed.Task);
            if (Task.WhenAny(drainTask, Task.Delay(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult() != drainTask)
                Trace.TraceWarning($"Timed out while draining git history output for sitemap freshness under '{rootPath}'. Sitemap lastmod values may be incomplete.");

            if (process.ExitCode != 0)
                return;

            var output = outputBuffer.ToString();
            DateTimeOffset? currentCommitDate = null;
            foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("@@POWERFORGE_DATE@@", StringComparison.Ordinal))
                {
                    var rawDate = line["@@POWERFORGE_DATE@@".Length..];
                    currentCommitDate = DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                        ? parsed.ToUniversalTime()
                        : null;
                    continue;
                }

                if (!currentCommitDate.HasValue)
                    continue;

                var fullPath = Path.GetFullPath(Path.Combine(rootPath, line.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsPathInsideRoot(rootPath, fullPath) || cache.ContainsKey(fullPath))
                    continue;

                cache[fullPath] = currentCommitDate.Value;
            }
        }
        catch
        {
            // Git freshness is optional; fall back to explicit page metadata or no lastmod.
        }
    }

    private static bool IsPathInsideRoot(string rootPath, string fullPath)
    {
        var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveCacheRoot(SiteSpec spec, string rootPath)
    {
        if (spec.Cache?.Enabled != true)
            return null;

        var root = spec.Cache.Root;
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(rootPath, ".cache", "powerforge-web");

        var full = Path.IsPathRooted(root) ? root : Path.Combine(rootPath, root);
        Directory.CreateDirectory(full);
        return full;
    }
}

