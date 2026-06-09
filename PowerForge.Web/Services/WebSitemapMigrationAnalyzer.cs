using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Reusable sitemap migration heuristics for legacy-site redirect planning.</summary>
public static class WebSitemapMigrationAnalyzer
{
    private static readonly Regex SlugNumericSuffixRegex = new(@"-\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SlugNumericSegmentRegex = new(@"-\d+(?=/|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SlugLanguageSuffixRegex = new(
        @"-(en|pl|fr|de|es|pt|pt-br|it|nl|sv|no|da|fi|cs|sk|uk|ru|ja|ko|zh|zh-cn|zh-tw|tr|ro|hu|bg|hr|sl|lt|lv|et)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SlugNonTokenRegex = new(@"[^a-z0-9/]+", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BlogLikeTargetPathRegex = new(@"^/(blog|categories|tags)(/|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex AuthorArchivePathRegex = NewMigrationRegex(@"^/author/[^/]+/?$");
    private static readonly Regex HubScriptsDetailPathRegex = NewMigrationRegex(@"^/hub/scripts/(.+?)/?$");
    private static readonly Regex PowerShellModulesDetailPathRegex = NewMigrationRegex(@"^/powershell-modules/(.+?)/?$");
    private static readonly Regex NetProductsDetailPathRegex = NewMigrationRegex(@"^/net-products/(.+?)/?$");
    private static readonly Regex OfferDetailPathRegex = NewMigrationRegex(@"^/offer/(.+?)/?$");
    private static readonly Regex StartDetailPathRegex = NewMigrationRegex(@"^/start/(.+?)/?$");

    /// <summary>Compare legacy and new URLs and produce redirect/review candidates.</summary>
    public static WebSitemapMigrationResult Analyze(WebSitemapMigrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var legacyUrls = NormalizeInputUrls(options.LegacyUrls);
        var newUrls = NormalizeInputUrls(options.NewUrls);
        var newLookup = BuildUrlLookup(newUrls);
        var pathAliasLookup = BuildPathAliasLookup(newLookup.Values);
        var newNormalized = new HashSet<string>(newUrls.Select(NormalizeUrl), StringComparer.OrdinalIgnoreCase);
        var candidates = new List<WebSitemapMigrationCandidate>();

        foreach (var legacyUrl in legacyUrls)
        {
            if (newNormalized.Contains(NormalizeUrl(legacyUrl)))
                continue;

            candidates.Add(GetRedirectCandidate(legacyUrl, newLookup, pathAliasLookup, options.NewSiteRoot));
        }

        var redirects = new List<WebSitemapMigrationRedirectRow>();
        var reviews = new List<WebSitemapMigrationReviewRow>();
        foreach (var candidate in candidates)
        {
            if (!candidate.NeedsReview && !string.IsNullOrWhiteSpace(candidate.TargetUrl))
            {
                redirects.Add(NewRedirectExportRow(candidate.LegacyUrl, candidate.TargetUrl, candidate.MatchKind, candidate.Notes));
                continue;
            }

            reviews.Add(NewReviewRow(candidate.LegacyUrl, candidate.TargetUrl, candidate.MatchKind, candidate.Notes));
        }

        if (options.IncludeSyntheticAmpRedirects)
        {
            foreach (var candidate in candidates.Where(IsSyntheticAmpRedirectCandidate))
            {
                var ampLegacyUrl = GetSyntheticAmpLegacyUrl(candidate.LegacyUrl);
                if (!string.IsNullOrWhiteSpace(ampLegacyUrl))
                {
                    redirects.Add(NewRedirectExportRow(
                        ampLegacyUrl,
                        candidate.TargetUrl,
                        "synthetic-amp-to-" + candidate.MatchKind,
                        "Synthetic AMP continuity redirect generated from the resolved canonical legacy route."));
                }
            }
        }

        if (options.IncludeAmpListingRoots)
            AddAmpListingRoots(legacyUrls, newLookup, options.NewSiteRoot, redirects, reviews);

        var distinctRedirects = redirects
            .GroupBy(static row => $"{row.LegacyUrl}|{row.TargetUrl}|{row.Status}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static row => row.LegacyUrl, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.TargetUrl, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var distinctReviews = reviews
            .GroupBy(static row => $"{row.LegacyUrl}|{row.TargetUrl}|{row.MatchKind}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static row => row.LegacyUrl, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.MatchKind, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WebSitemapMigrationResult
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            LegacyUrlCount = legacyUrls.Length,
            NewUrlCount = newUrls.Length,
            MissingLegacyCount = candidates.Count,
            RedirectCount = distinctRedirects.Length,
            ReviewCount = distinctReviews.Length,
            Candidates = candidates.ToArray(),
            Redirects = distinctRedirects,
            Reviews = distinctReviews
        };
    }

    /// <summary>Normalize a URL for sitemap comparison by lowercasing origin/path and dropping query/fragment.</summary>
    public static string NormalizeUrl(string url)
    {
        if (!TryCreateHttpUri(url, out var uri))
            return string.Empty;

        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
            path = path.TrimEnd('/');

        var builder = new UriBuilder(uri.Scheme, uri.Host.ToLowerInvariant())
        {
            Path = path.ToLowerInvariant(),
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    /// <summary>Return the lowercased URL path, without a trailing slash except for root.</summary>
    public static string GetUrlPath(string url)
    {
        if (!TryCreateHttpUri(url, out var uri))
            return string.Empty;

        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        return path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal)
            ? path.TrimEnd('/').ToLowerInvariant()
            : path.ToLowerInvariant();
    }

    /// <summary>Generate common slug variants used by migration matching.</summary>
    public static string[] GetSlugVariants(string slug)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        void Add(string? value)
        {
            var normalized = value?.Trim().Trim('/') ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalized) && variants.Add(normalized))
                queue.Enqueue(normalized);
        }

        Add(slug);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            Add(SlugNumericSuffixRegex.Replace(current, string.Empty));
            Add(SlugNumericSegmentRegex.Replace(current, string.Empty));
            Add(SlugLanguageSuffixRegex.Replace(current, string.Empty));
            Add(SlugNonTokenRegex.Replace(current, "-").Trim('-'));
            Add(RemoveDiacritics(current));
        }

        return variants.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Check whether a generated route exists as a file, .html file, or directory index under a site root.</summary>
    public static bool GeneratedRouteExists(string siteRoot, string url)
    {
        if (string.IsNullOrWhiteSpace(siteRoot) || !Directory.Exists(siteRoot))
            return false;

        if (!TryCreateHttpUri(url, out var uri))
            return false;

        var root = Path.GetFullPath(siteRoot);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = uri.AbsolutePath.Trim('/');
        var candidate = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(root, "index.html")
            : Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

        return new[]
            {
                candidate,
                candidate + ".html",
                Path.Combine(candidate, "index.html")
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(filePath =>
            {
                var fullPath = Path.GetFullPath(filePath);
                return (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) &&
                       File.Exists(fullPath);
            });
    }

    private static WebSitemapMigrationCandidate GetRedirectCandidate(
        string legacyUrl,
        IReadOnlyDictionary<string, UrlEntry> newLookup,
        IReadOnlyDictionary<string, string> pathAliasLookup,
        string? newSiteRoot)
    {
        var legacyNormalized = NormalizeUrl(legacyUrl);
        if (newLookup.TryGetValue(legacyNormalized, out var exactEntry))
            return Candidate(legacyUrl, exactEntry.Url, "exact", false, "Already present in the new sitemap.");

        if (!TryCreateHttpUri(legacyUrl, out var legacyUri))
            return Candidate(legacyUrl, string.Empty, "invalid-url", true, "Legacy URL is not a valid absolute HTTP(S) URL.");

        var legacyHost = legacyUri.Host.ToLowerInvariant();
        var legacyPath = GetUrlPath(legacyUrl);
        var candidates = new List<WebSitemapMigrationCandidate>();

        if (!legacyPath.StartsWith("/blog", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = NormalizeUrl($"{legacyUri.Scheme}://{legacyHost}/blog{legacyPath}");
            if (newLookup.TryGetValue(candidate, out var entry))
                candidates.Add(Candidate(legacyUrl, entry.Url, "root-to-blog", false, "Legacy root content moved under /blog/."));
        }

        if (legacyPath.StartsWith("/category/", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = legacyPath["/category/".Length..];
            AddDirectLookup(candidates, legacyUrl, $"{legacyUri.Scheme}://{legacyHost}/categories/{suffix}", newLookup, "category-to-categories", "Legacy WordPress category route moved to /categories/.");
            if (candidates.Count == 0)
            {
                AddCandidateIfFound(
                    candidates,
                    legacyUrl,
                    legacyUri.Scheme,
                    legacyHost,
                    GetSlugVariants(suffix).SelectMany(static variant => new[] { "/categories/" + variant, "/tags/" + variant }),
                    newLookup,
                    pathAliasLookup,
                    newSiteRoot,
                    "category-normalized",
                    "Legacy category route matched a normalized category/tag target.");
            }
        }

        if (legacyPath.StartsWith("/tag/", StringComparison.OrdinalIgnoreCase) ||
            legacyPath.StartsWith("/post_tag/", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = legacyPath.StartsWith("/tag/", StringComparison.OrdinalIgnoreCase)
                ? legacyPath["/tag/".Length..]
                : legacyPath["/post_tag/".Length..];
            AddDirectLookup(candidates, legacyUrl, $"{legacyUri.Scheme}://{legacyHost}/tags/{suffix}", newLookup, "tag-to-tags", "Legacy tag route moved to /tags/.");
            if (candidates.Count == 0)
            {
                AddCandidateIfFound(
                    candidates,
                    legacyUrl,
                    legacyUri.Scheme,
                    legacyHost,
                    GetSlugVariants(suffix).Select(static variant => "/tags/" + variant),
                    newLookup,
                    pathAliasLookup,
                    newSiteRoot,
                    "tag-normalized",
                    "Legacy tag route matched a normalized tag target.");
            }
        }

        if (legacyPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            AddDirectLookup(candidates, legacyUrl, $"{legacyUri.Scheme}://{legacyHost}{legacyPath[..^5]}", newLookup, "drop-html-extension", "Legacy .html route matches the slash-route in the new sitemap.");

        AddSpecialCaseCandidates(candidates, legacyUrl, legacyUri.Scheme, legacyHost, legacyPath, newLookup, pathAliasLookup, newSiteRoot);

        return candidates.Count switch
        {
            0 => Candidate(legacyUrl, string.Empty, "missing", true, "No direct candidate found in the new sitemap."),
            1 => candidates[0],
            _ => Candidate(legacyUrl, candidates[0].TargetUrl, "multiple-candidates", true, "Multiple redirect candidates found. Review manually.")
        };
    }

    private static void AddSpecialCaseCandidates(
        List<WebSitemapMigrationCandidate> candidates,
        string legacyUrl,
        string scheme,
        string legacyHost,
        string legacyPath,
        IReadOnlyDictionary<string, UrlEntry> newLookup,
        IReadOnlyDictionary<string, string> pathAliasLookup,
        string? newSiteRoot)
    {
        if (legacyPath.Equals("/amp", StringComparison.OrdinalIgnoreCase))
        {
            var targetBlog = $"{scheme}://{legacyHost}/blog/";
            var candidate = NormalizeUrl(targetBlog);
            if (newLookup.TryGetValue(candidate, out var blogEntry) || (!string.IsNullOrWhiteSpace(newSiteRoot) && GeneratedRouteExists(newSiteRoot, targetBlog)))
            {
                candidates.Add(Candidate(legacyUrl, blogEntry?.Url ?? targetBlog, "amp-root-to-blog", false, "Legacy AMP index route moved to /blog/."));
            }
            else
            {
                candidates.Add(Candidate(legacyUrl, string.Empty, "amp-review", true, "AMP root route could not be mapped automatically. Review manually."));
            }
            return;
        }

        if (legacyPath.StartsWith("/amp/", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Candidate(legacyUrl, string.Empty, "amp-review", true, "AMP route requires manual review. It may be a listing page rather than a 1:1 canonical page."));
            return;
        }

        if (candidates.Count > 0)
            return;

        (string[] Paths, string Kind, string Notes) special = legacyPath switch
        {
            "/docs" => (new[] { "/projects" }, "docs-to-projects", "Legacy docs hub mapped to the current projects landing page."),
            _ when AuthorArchivePathRegex.IsMatch(legacyPath) => (new[] { "/blog" }, "author-to-blog", "Legacy author archive mapped to the current blog landing page."),
            "/hub/scripts" => (new[] { "/scripts" }, "hub-scripts-root", "Legacy hub scripts landing page matched the current scripts page."),
            _ => (Array.Empty<string>(), string.Empty, string.Empty)
        };

        if (special.Paths.Length > 0)
        {
            AddCandidateIfFound(candidates, legacyUrl, scheme, legacyHost, special.Paths, newLookup, pathAliasLookup, newSiteRoot, special.Kind, special.Notes);
            return;
        }

        AddRegexSpecialCase(candidates, legacyUrl, scheme, legacyHost, legacyPath, newLookup, pathAliasLookup, newSiteRoot, HubScriptsDetailPathRegex, "hub-scripts-detail", "Legacy hub script detail mapped to the current root route.", match => new[] { "/" + match.Groups[1].Value });
        AddRegexSpecialCase(candidates, legacyUrl, scheme, legacyHost, legacyPath, newLookup, pathAliasLookup, newSiteRoot, PowerShellModulesDetailPathRegex, "powershell-modules-detail", "Legacy PowerShell module page mapped to the current route.", match => new[] { "/" + match.Groups[1].Value, "/projects/" + match.Groups[1].Value });
        AddRegexSpecialCase(candidates, legacyUrl, scheme, legacyHost, legacyPath, newLookup, pathAliasLookup, newSiteRoot, NetProductsDetailPathRegex, "net-products-detail", "Legacy .NET product page mapped to the current route.", match => new[] { "/" + match.Groups[1].Value, "/projects/" + match.Groups[1].Value });
        AddRegexSpecialCase(candidates, legacyUrl, scheme, legacyHost, legacyPath, newLookup, pathAliasLookup, newSiteRoot, OfferDetailPathRegex, "offer-detail", "Legacy offer page mapped to the current route.", match => new[] { "/" + match.Groups[1].Value });
        AddRegexSpecialCase(candidates, legacyUrl, scheme, legacyHost, legacyPath, newLookup, pathAliasLookup, newSiteRoot, StartDetailPathRegex, "start-detail", "Legacy start section page mapped to the current route.", match => new[] { "/" + match.Groups[1].Value });
    }

    private static void AddRegexSpecialCase(
        List<WebSitemapMigrationCandidate> candidates,
        string legacyUrl,
        string scheme,
        string legacyHost,
        string legacyPath,
        IReadOnlyDictionary<string, UrlEntry> newLookup,
        IReadOnlyDictionary<string, string> pathAliasLookup,
        string? newSiteRoot,
        Regex pattern,
        string matchKind,
        string notes,
        Func<Match, IEnumerable<string>> candidatePaths)
    {
        if (candidates.Count > 0)
            return;

        var match = pattern.Match(legacyPath);
        if (match.Success)
            AddCandidateIfFound(candidates, legacyUrl, scheme, legacyHost, candidatePaths(match), newLookup, pathAliasLookup, newSiteRoot, matchKind, notes);
    }

    private static Regex NewMigrationRegex(string pattern)
        => new(
            pattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));

    private static void AddDirectLookup(
        List<WebSitemapMigrationCandidate> candidates,
        string legacyUrl,
        string candidateUrl,
        IReadOnlyDictionary<string, UrlEntry> newLookup,
        string matchKind,
        string notes)
    {
        if (newLookup.TryGetValue(NormalizeUrl(candidateUrl), out var entry))
            candidates.Add(Candidate(legacyUrl, entry.Url, matchKind, false, notes));
    }

    private static void AddCandidateIfFound(
        List<WebSitemapMigrationCandidate> candidates,
        string legacyUrl,
        string scheme,
        string hostName,
        IEnumerable<string> candidatePaths,
        IReadOnlyDictionary<string, UrlEntry> newLookup,
        IReadOnlyDictionary<string, string> pathAliasLookup,
        string? newSiteRoot,
        string matchKind,
        string notes)
    {
        foreach (var candidatePath in candidatePaths.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = candidatePath.StartsWith("/", StringComparison.Ordinal) ? candidatePath : "/" + candidatePath;
            var candidateUrl = $"{scheme}://{hostName}{path}";
            if (newLookup.TryGetValue(NormalizeUrl(candidateUrl), out var entry))
            {
                candidates.Add(Candidate(legacyUrl, entry.Url, matchKind, false, notes));
                return;
            }

            var aliasKey = ToComparablePath(GetUrlPath(candidateUrl));
            if (pathAliasLookup.TryGetValue(aliasKey, out var aliasTarget))
            {
                candidates.Add(Candidate(legacyUrl, aliasTarget, matchKind, false, notes));
                return;
            }

            if (!string.IsNullOrWhiteSpace(newSiteRoot))
            {
                var generatedUrl = candidateUrl.EndsWith("/", StringComparison.Ordinal) ? candidateUrl : candidateUrl + "/";
                if (GeneratedRouteExists(newSiteRoot, generatedUrl))
                {
                    candidates.Add(Candidate(legacyUrl, generatedUrl, matchKind, false, notes));
                    return;
                }
            }
        }
    }

    private static void AddAmpListingRoots(
        IReadOnlyCollection<string> legacyUrls,
        IReadOnlyDictionary<string, UrlEntry> newLookup,
        string? newSiteRoot,
        ICollection<WebSitemapMigrationRedirectRow> redirects,
        ICollection<WebSitemapMigrationReviewRow> reviews)
    {
        foreach (var origin in legacyUrls.Select(GetUrlOrigin).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            var ampRoot = origin.TrimEnd('/') + "/amp/";
            var targetBlog = origin.TrimEnd('/') + "/blog/";
            if (newLookup.ContainsKey(NormalizeUrl(targetBlog)) || (!string.IsNullOrWhiteSpace(newSiteRoot) && GeneratedRouteExists(newSiteRoot, targetBlog)))
            {
                redirects.Add(NewRedirectExportRow(ampRoot, targetBlog, "amp-root-to-blog", "Legacy AMP index route moved to /blog/."));
            }
            else
            {
                reviews.Add(NewReviewRow(ampRoot, string.Empty, "amp-review", "AMP index route could not be mapped automatically. Review manually."));
            }
        }
    }

    private static bool IsSyntheticAmpRedirectCandidate(WebSitemapMigrationCandidate candidate)
    {
        if (candidate.NeedsReview || string.IsNullOrWhiteSpace(candidate.TargetUrl))
            return false;

        var legacyPath = GetUrlPath(candidate.LegacyUrl);
        if (string.IsNullOrWhiteSpace(legacyPath) ||
            legacyPath.Equals("/", StringComparison.Ordinal) ||
            legacyPath.Equals("/amp", StringComparison.OrdinalIgnoreCase) ||
            legacyPath.EndsWith("/amp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var targetPath = GetUrlPath(candidate.TargetUrl);
        return BlogLikeTargetPathRegex.IsMatch(targetPath);
    }

    private static string? GetSyntheticAmpLegacyUrl(string legacyUrl)
    {
        if (!TryCreateHttpUri(legacyUrl, out var uri))
            return null;

        var path = GetUrlPath(legacyUrl);
        if (string.IsNullOrWhiteSpace(path) ||
            path.Equals("/", StringComparison.Ordinal) ||
            path.Equals("/amp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/amp", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{path.TrimEnd('/')}/amp/";
    }

    private static Dictionary<string, UrlEntry> BuildUrlLookup(IEnumerable<string> urls)
    {
        var lookup = new Dictionary<string, UrlEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in urls)
        {
            var normalized = NormalizeUrl(url);
            lookup.TryAdd(normalized, new UrlEntry(url, normalized, GetUrlPath(url)));
        }

        return lookup;
    }

    private static Dictionary<string, string> BuildPathAliasLookup(IEnumerable<UrlEntry> entries)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var key = ToComparablePath(entry.Path);
            if (!string.IsNullOrWhiteSpace(key))
                lookup.TryAdd(key, entry.Url);
        }

        return lookup;
    }

    private static string[] NormalizeInputUrls(IEnumerable<string>? urls)
        => (urls ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Where(static value => TryCreateHttpUri(value, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string GetUrlOrigin(string url)
    {
        if (!TryCreateHttpUri(url, out var uri))
            return string.Empty;

        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}";
    }

    private static bool TryCreateHttpUri(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed) ||
            (!parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static string ToComparablePath(string value)
        => RemoveDiacritics(value.ToLowerInvariant());

    private static string RemoveDiacritics(string value)
    {
        var normalized = value
            .Replace('ł', 'l')
            .Replace('Ł', 'L')
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static WebSitemapMigrationCandidate Candidate(string legacyUrl, string targetUrl, string matchKind, bool needsReview, string notes)
        => new()
        {
            LegacyUrl = legacyUrl,
            TargetUrl = targetUrl,
            MatchKind = matchKind,
            NeedsReview = needsReview,
            Notes = notes
        };

    private static WebSitemapMigrationRedirectRow NewRedirectExportRow(string legacyUrl, string targetUrl, string matchKind, string notes)
        => new()
        {
            LegacyUrl = legacyUrl,
            TargetUrl = targetUrl,
            Status = 301,
            MatchKind = matchKind,
            Notes = notes
        };

    private static WebSitemapMigrationReviewRow NewReviewRow(string legacyUrl, string targetUrl, string matchKind, string notes)
        => new()
        {
            LegacyUrl = legacyUrl,
            TargetUrl = targetUrl,
            MatchKind = matchKind,
            Notes = notes
        };

    private sealed record UrlEntry(string Url, string Normalized, string Path);
}
