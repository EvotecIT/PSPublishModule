using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PowerForge.Web;

/// <summary>Content discovery and item construction helpers.</summary>
public static partial class WebSiteBuilder
{
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

        var contentRoots = BuildContentRoots(plan);

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

                if (matter?.Aliases is { Length: > 0 })
                {
                    var seenAliasSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var alias in matter.Aliases)
                    {
                        if (string.IsNullOrWhiteSpace(alias)) continue;
                        foreach (var aliasSource in ExpandAliasRedirectSources(alias))
                        {
                            if (!seenAliasSources.Add(aliasSource))
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
                    ? RenderMarkdown(processedBody, file, spec.Cache, cacheRoot)
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
                    Order = matter?.Order,
                    Slug = slugPath,
                    Tags = matter?.Tags ?? Array.Empty<string>(),
                    Aliases = matter?.Aliases ?? Array.Empty<string>(),
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
                        ? $"collection:{collection.Name}:index"
                        : $"collection:{collection.Name}:{projectSlug}:index",
                    Title = generatedTitle,
                    Description = generatedDescription,
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

