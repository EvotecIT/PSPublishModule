using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Verifies site content and routing integrity.</summary>
public static partial class WebSiteVerifier
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex MarkdownFenceRegex = new("```[\\s\\S]*?```", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex MarkdownRawHtmlRegex = new("<\\s*(b|i|strong|em|u|h[1-6]|p|ul|ol|li|br)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>Validates the site spec against discovered content.</summary>
    /// <param name="spec">Site configuration.</param>
    /// <param name="plan">Resolved site plan.</param>
    /// <returns>Verification result.</returns>
    public static WebVerifyResult Verify(SiteSpec spec, WebSitePlan plan)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var errors = new List<string>();
        var warnings = new List<string>();
        var localization = ResolveLocalizationConfig(spec, warnings);

        if (spec.Collections is null || spec.Collections.Length == 0)
        {
            warnings.Add("No collections defined.");
            return new WebVerifyResult { Success = true, Warnings = warnings.ToArray(), Errors = Array.Empty<string>() };
        }

        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var collectionRoutes = new Dictionary<string, List<CollectionRoute>>(StringComparer.OrdinalIgnoreCase);
        var taxonomyTermsByLanguage = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
        var usedTaxonomyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var collection in spec.Collections)
        {
            if (collection is null) continue;
            var files = EnumerateCollectionFiles(plan.RootPath, collection.Input).ToArray();
            if (files.Length == 0)
            {
                warnings.Add($"Collection '{collection.Name}' has no files.");
                continue;
            }

            var leafBundleRoots = BuildLeafBundleRoots(files);
            foreach (var file in files)
            {
                if (IsUnderAnyRoot(file, leafBundleRoots) && !IsLeafBundleIndex(file))
                    continue;

                var markdown = File.ReadAllText(file);
                var (matter, body) = FrontMatterParser.Parse(markdown);
                var title = matter?.Title ?? FrontMatterParser.ExtractTitleFromMarkdown(body) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title))
                {
                    errors.Add($"Missing title in: {file}");
                }
                ValidateMarkdownHygiene(plan.RootPath, file, collection.Name, body, warnings);

                var collectionRoot = ResolveCollectionRootForFile(plan.RootPath, collection.Input, file);
                var relativePath = ResolveRelativePath(collectionRoot, file);
                var resolvedLanguage = ResolveItemLanguage(spec, localization, relativePath, matter, out var localizedRelativePath, out var localizedRelativeDir);
                var relativeDir = localizedRelativeDir;
                CollectTaxonomyTerms(spec.Taxonomies, matter, resolvedLanguage, taxonomyTermsByLanguage, usedTaxonomyNames);
                var isSectionIndex = IsSectionIndex(file);
                var isBundleIndex = IsLeafBundleIndex(file);
                var slugPath = ResolveSlugPath(localizedRelativePath, relativeDir, matter?.Slug);
                if (isSectionIndex || isBundleIndex)
                    slugPath = ApplySlugOverride(relativeDir, matter?.Slug);
                if (string.IsNullOrWhiteSpace(slugPath))
                {
                    errors.Add($"Missing slug in: {file}");
                    continue;
                }

                var projectSlug = ResolveProjectSlug(plan, file);
                var baseOutput = ReplaceProjectPlaceholder(collection.Output, projectSlug);
                var route = BuildRoute(baseOutput, slugPath, spec.TrailingSlash);
                route = ApplyLanguagePrefixToRoute(spec, localization, route, resolvedLanguage);
                if (routes.TryGetValue(route, out var existing))
                {
                    errors.Add($"Duplicate route '{route}' from '{file}' and '{existing}'.");
                }
                else
                {
                    routes[route] = file;
                }

                if (!collectionRoutes.TryGetValue(collection.Name, out var list))
                {
                    list = new List<CollectionRoute>();
                    collectionRoutes[collection.Name] = list;
                }
                list.Add(new CollectionRoute(route, file, matter?.Draft ?? false, resolvedLanguage));
            }
        }

        AddSyntheticTaxonomyRoutes(spec, localization, routes, taxonomyTermsByLanguage, warnings);

        ValidateDataFiles(spec, plan, warnings);
        ValidateThemeAssets(spec, plan, warnings);
        ValidateThemeContract(spec, plan, warnings);
        ValidateLayoutHooks(spec, plan, warnings);
        ValidateThemeTokens(spec, plan, warnings);
        ValidatePrismAssets(spec, plan, warnings);
        ValidateTocCoverage(spec, plan, collectionRoutes, warnings);
        ValidateNavigationDefaults(spec, warnings);
        ValidateBlogAndTaxonomySupport(spec, localization, collectionRoutes, usedTaxonomyNames, warnings);
        ValidateVersioning(spec, warnings);
        ValidateNavigationLint(spec, plan, routes.Keys, warnings);
        ValidateSiteNavExport(spec, plan, warnings);
        ValidateNotFoundAssetBundles(spec, routes.Keys, warnings);

        return new WebVerifyResult
        {
            Success = errors.Count == 0,
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    private static string BuildRoute(string baseOutput, string slug, TrailingSlashMode slashMode)
    {
        var basePath = NormalizePath(baseOutput);
        var slugPath = NormalizePath(slug);
        string combined;

        if (string.IsNullOrWhiteSpace(slugPath) || slugPath == "index")
        {
            combined = basePath;
        }
        else if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
        {
            combined = slugPath;
        }
        else
        {
            combined = $"{basePath}/{slugPath}";
        }

        combined = "/" + combined.Trim('/');
        return EnsureTrailingSlash(combined, slashMode);
    }

    private static string EnsureTrailingSlash(string path, TrailingSlashMode mode)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        if (mode == TrailingSlashMode.Never)
            return path.EndsWith("/") && path.Length > 1 ? path.TrimEnd('/') : path;

        if (mode == TrailingSlashMode.Always && !path.EndsWith("/"))
            return path + "/";

        return path;
    }
}

