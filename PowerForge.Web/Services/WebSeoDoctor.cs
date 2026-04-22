using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Runs SEO-focused checks on generated HTML output.</summary>
public static class WebSeoDoctor
{
    private static readonly string[] DefaultExcludePatterns =
    {
        "*.scripts.html",
        "**/*.scripts.html",
        "*.head.html",
        "**/*.head.html",
        "**/api-fragments/**",
        "api-fragments/**"
    };

    private static readonly string[] DefaultHtmlExtensions = { ".html", ".htm" };
    private static readonly string[] NoIndexMetaNames = { "robots", "googlebot", "bingbot", "slurp" };
    private static readonly string[] IgnoreLinkPrefixes = { "#", "mailto:", "tel:", "javascript:", "data:", "blob:" };
    private static readonly Regex HreflangTokenPattern = new(
        "^(x-default|[a-z]{2,3}(?:-[a-z0-9]{2,8})*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FrontMatterLeakPattern = new(
        @"(?:^|\s)(?:--\s*)?(?<key>title|description|slug|language|layout|translation_key|meta\.raw_html)\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex VisibleMarkdownLeakPattern = new(
        @"(?:^|[\s(])(?:!\[[^\]\r\n]*\]\([^)]+\)|\[[^\]\r\n]+\]\([^)]+\)|#{1,6}\s+\S+|```)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly TimeSpan GlobMatchRegexTimeout = TimeSpan.FromMilliseconds(100);
    private const int MaxJsonLdPayloadLength = 1_000_000;
    private static readonly StringComparison FileSystemPathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>Runs SEO doctor checks for a generated site output.</summary>
    public static WebSeoDoctorResult Analyze(WebSeoDoctorOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.SiteRoot))
            throw new ArgumentException("SiteRoot is required.", nameof(options));

        var siteRoot = Path.GetFullPath(options.SiteRoot);
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");
        var referenceSiteRoots = (options.ReferenceSiteRoots ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Where(path => Directory.Exists(path))
            .Where(path => !path.Equals(siteRoot, FileSystemPathComparison))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();

        var issues = new List<WebSeoDoctorIssue>();
        var errors = new List<string>();
        var warnings = new List<string>();
        var pages = new List<PageScan>(capacity: 256);

        var allHtmlFiles = EnumerateHtmlFiles(siteRoot, options.Include, options.Exclude, options.UseDefaultExcludes)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var htmlFiles = allHtmlFiles;
        if (options.MaxHtmlFiles > 0 && htmlFiles.Count > options.MaxHtmlFiles)
            htmlFiles = htmlFiles.Take(options.MaxHtmlFiles).ToList();

        var titleMin = Math.Max(0, options.MinTitleLength);
        var titleMax = Math.Max(titleMin, options.MaxTitleLength);
        var descriptionMin = Math.Max(0, options.MinDescriptionLength);
        var descriptionMax = Math.Max(descriptionMin, options.MaxDescriptionLength);
        var minFocusMentions = Math.Max(0, options.MinFocusKeyphraseMentions);
        var focusMetaNames = (options.FocusKeyphraseMetaNames ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (focusMetaNames.Length == 0)
            focusMetaNames = new[] { "pf:focus-keyphrase" };

        void AddIssue(string severity, string category, string? path, string message, string? hintOverride = null, string? keyHint = null)
        {
            var normalizedSeverity = string.IsNullOrWhiteSpace(severity)
                ? "warning"
                : severity.Trim().ToLowerInvariant();
            var normalizedCategory = string.IsNullOrWhiteSpace(category)
                ? "general"
                : NormalizeIssueToken(category);
            var issuePath = string.IsNullOrWhiteSpace(path) ? null : path.Replace('\\', '/');
            var normalizedHint = NormalizeIssueToken(hintOverride ?? message);
            var code = BuildIssueCode(normalizedCategory, normalizedHint);
            var key = BuildIssueKey(normalizedSeverity, normalizedCategory, issuePath, normalizedHint, keyHint);
            var line = string.IsNullOrWhiteSpace(issuePath)
                ? $"[{code}] {message}"
                : $"[{code}] {issuePath}: {message}";

            issues.Add(new WebSeoDoctorIssue
            {
                Severity = normalizedSeverity,
                Category = normalizedCategory,
                Code = code,
                Hint = normalizedHint,
                Path = issuePath,
                Message = message,
                Key = key
            });

            if (normalizedSeverity.Equals("error", StringComparison.OrdinalIgnoreCase))
                errors.Add(line);
            else
                warnings.Add(line);
        }

        foreach (var file in htmlFiles)
        {
            var relativePath = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
            string html;
            try
            {
                html = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                AddIssue("error", "html", relativePath, $"failed to read HTML ({ex.Message}).", "html-read", ex.GetType().Name);
                continue;
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                AddIssue("warning", "html", relativePath, "empty HTML file.", "html-empty");
                continue;
            }

            AngleSharp.Dom.IDocument doc;
            try
            {
                doc = HtmlParser.ParseWithAngleSharp(html);
            }
            catch (Exception ex)
            {
                AddIssue("error", "html", relativePath, $"failed to parse HTML ({ex.Message}).", "html-parse", ex.GetType().Name);
                continue;
            }

            var hasNoIndexRobots = HasNoIndexRobots(doc);
            if (TryResolveDirectoryAliasRoute(siteRoot, relativePath, out var canonicalAliasRoute) && !hasNoIndexRobots)
            {
                AddIssue("warning", "canonical", relativePath,
                    $"flat HTML alias has matching directory route '{canonicalAliasRoute}' but is missing a noindex robots meta tag.",
                    "canonical-alias-noindex",
                    canonicalAliasRoute);
            }

            if (!options.IncludeNoIndexPages && hasNoIndexRobots)
                continue;

            var route = ToRoute(relativePath);
            var title = NormalizeWhitespace(doc.Title);
            var description = GetMetaNameValue(doc, "description");
            var bodyText = GetVisibleBodyText(doc.Body);
            var canonicalLinks = GetCanonicalLinks(doc);
            var hreflangAlternates = GetHreflangAlternates(doc);

            if (options.CheckContentLeaks)
            {
                ValidateContentLeaks(relativePath, bodyText, AddIssue);
            }

            var page = new PageScan
            {
                RelativePath = relativePath,
                Route = route,
                Title = title,
                Description = description,
                BodyText = bodyText,
                CanonicalHref = canonicalLinks.FirstOrDefault() ?? string.Empty
            };
            page.FocusKeyphrase = options.CheckFocusKeyphrase
                ? GetFocusKeyphrase(doc, focusMetaNames)
                : string.Empty;
            page.HreflangAlternates.AddRange(hreflangAlternates);
            pages.Add(page);

            if (options.CheckTitleLength)
            {
                if (string.IsNullOrWhiteSpace(title))
                {
                    AddIssue("warning", "title", relativePath, "missing <title>.", "title-missing");
                }
                else
                {
                    if (title.Length < titleMin)
                    {
                        AddIssue("warning", "title", relativePath,
                            $"title is short ({title.Length} chars). Recommended {titleMin}-{titleMax}.",
                            "title-short");
                    }

                    if (title.Length > titleMax)
                    {
                        AddIssue("warning", "title", relativePath,
                            $"title is long ({title.Length} chars). Recommended {titleMin}-{titleMax}.",
                            "title-long");
                    }
                }
            }

            if (options.CheckDescriptionLength)
            {
                if (string.IsNullOrWhiteSpace(description))
                {
                    AddIssue("warning", "description", relativePath, "missing meta description.", "description-missing");
                }
                else
                {
                    if (description.Length < descriptionMin)
                    {
                        AddIssue("warning", "description", relativePath,
                            $"meta description is short ({description.Length} chars). Recommended {descriptionMin}-{descriptionMax}.",
                            "description-short");
                    }

                    if (description.Length > descriptionMax)
                    {
                        AddIssue("warning", "description", relativePath,
                            $"meta description is long ({description.Length} chars). Recommended {descriptionMin}-{descriptionMax}.",
                            "description-long");
                    }
                }
            }

            if (options.CheckH1)
            {
                var visibleH1Count = doc.QuerySelectorAll("h1")
                    .Count(heading => !IsElementHidden(heading));
                if (visibleH1Count == 0)
                    AddIssue("warning", "heading", relativePath, "missing visible <h1>.", "h1-missing");
                else if (visibleH1Count > 1)
                    AddIssue("warning", "heading", relativePath, $"multiple visible <h1> elements ({visibleH1Count}).", "h1-multiple");
            }

            if (options.CheckImageAlt)
            {
                var missingAlt = doc.QuerySelectorAll("img[src]")
                    .Where(image => !IsElementHidden(image))
                    .Where(image => !image.HasAttribute("alt"))
                    .Select(image => (image.GetAttribute("src") ?? string.Empty).Trim())
                    .Where(src => !string.IsNullOrWhiteSpace(src))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToArray();
                if (missingAlt.Length > 0)
                {
                    AddIssue("warning", "image-alt", relativePath,
                        $"image(s) missing alt attribute. Sample: {string.Join(", ", missingAlt)}.",
                        "image-alt-missing");
                }
            }

            if (options.CheckFocusKeyphrase && !string.IsNullOrWhiteSpace(page.FocusKeyphrase))
            {
                if (string.IsNullOrWhiteSpace(title) ||
                    title.IndexOf(page.FocusKeyphrase, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    AddIssue("warning", "focus-keyphrase", relativePath,
                        $"focus keyphrase '{page.FocusKeyphrase}' is not present in title.",
                        "focus-keyphrase-title");
                }

                var mentionCount = CountCaseInsensitiveOccurrences(bodyText, page.FocusKeyphrase);
                if (mentionCount < minFocusMentions)
                {
                    AddIssue("warning", "focus-keyphrase", relativePath,
                        $"focus keyphrase '{page.FocusKeyphrase}' appears {mentionCount} time(s) in body text (min {minFocusMentions}).",
                        "focus-keyphrase-body");
                }
            }

            if (options.CheckCanonical)
            {
                ValidateCanonical(
                    relativePath,
                    options.RequireCanonical,
                    canonicalLinks,
                    AddIssue);
            }

            if (options.CheckHreflang)
            {
                ValidateHreflang(
                    relativePath,
                    options.RequireHreflang,
                    options.RequireHreflangXDefault,
                    hreflangAlternates,
                    AddIssue);
            }

            if (options.CheckStructuredData)
            {
                ValidateStructuredData(
                    doc,
                    relativePath,
                    options.RequireStructuredData,
                    AddIssue);
            }

            var baseDir = ResolveBaseDirectory(siteRoot, file, doc);
            foreach (var href in doc.QuerySelectorAll("a[href]")
                         .Select(link => link.GetAttribute("href"))
                         .Where(static href => !string.IsNullOrWhiteSpace(href))
                         .Select(static href => href!.Trim()))
            {
                if (ShouldSkipLink(href) || IsExternalLink(href))
                    continue;
                if (!TryResolveLocalTarget(siteRoot, baseDir, href, out var resolvedPath))
                    continue;
                if (!File.Exists(resolvedPath))
                    continue;

                var targetRelativePath = Path.GetRelativePath(siteRoot, resolvedPath).Replace('\\', '/');
                var targetRoute = ToRoute(targetRelativePath);
                if (!string.IsNullOrWhiteSpace(targetRoute))
                    page.OutboundRoutes.Add(targetRoute);
            }
        }

        ValidatePageAssertions(siteRoot, options.PageAssertions, AddIssue);

        if (pages.Count == 0)
            AddIssue("warning", "general", null, "No HTML pages selected for SEO doctor checks.", "no-pages");

        if (options.CheckDuplicateTitles)
        {
            var duplicateTitleGroups = pages
                .Where(page => !string.IsNullOrWhiteSpace(page.Title))
                .GroupBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var group in duplicateTitleGroups)
            {
                var sampleRoutes = group
                    .Select(page => page.Route)
                    .Where(route => !string.IsNullOrWhiteSpace(route))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToArray();
                var title = group.Key;
                AddIssue("warning", "duplicate-intent", null,
                    $"duplicate title intent detected for '{title}' across {group.Count()} pages. Sample routes: {string.Join(", ", sampleRoutes)}.",
                    "duplicate-title-intent",
                    title);
            }
        }

        var orphanPageCount = 0;
        if (options.CheckOrphanPages && pages.Count > 0)
        {
            var routeSet = pages
                .Select(page => page.Route)
                .Where(route => !string.IsNullOrWhiteSpace(route))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var inboundRouteCounts = routeSet.ToDictionary(route => route, _ => 0, StringComparer.OrdinalIgnoreCase);

            foreach (var page in pages)
            {
                foreach (var route in page.OutboundRoutes)
                {
                    if (!routeSet.Contains(route))
                        continue;
                    if (route.Equals(page.Route, StringComparison.OrdinalIgnoreCase))
                        continue;
                    inboundRouteCounts[route]++;
                }
            }

            var orphanRoutes = pages
                .Where(page => IsOrphanCandidateRoute(page.Route))
                .Where(page => inboundRouteCounts.TryGetValue(page.Route, out var inbound) && inbound == 0)
                .Select(page => page.Route)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(route => route, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var route in orphanRoutes)
            {
                AddIssue("warning", "orphan", route, "orphan page candidate (no inbound links from scanned pages).", "orphan-page");
            }

            orphanPageCount = orphanRoutes.Length;
        }

        if ((options.CheckCanonical || options.CheckHreflang) && pages.Count > 0)
        {
            var localizedValidationRoots = new[] { siteRoot }
                .Concat(referenceSiteRoots)
                .ToArray();
            var localizedValidationPages = pages.ToList();
            localizedValidationPages.AddRange(LoadReferencePages(localizedValidationRoots));
            ValidateLocalizedAlternateTargets(
                pages,
                localizedValidationPages,
                AddIssue);
        }

        return new WebSeoDoctorResult
        {
            Success = errors.Count == 0,
            HtmlFileCount = allHtmlFiles.Count,
            HtmlSelectedFileCount = htmlFiles.Count,
            PageCount = pages.Count,
            OrphanPageCount = orphanPageCount,
            IssueCount = issues.Count,
            ErrorCount = errors.Count,
            WarningCount = warnings.Count,
            NewIssueCount = issues.Count(issue => issue.IsNew),
            NewErrorCount = issues.Count(issue =>
                issue.IsNew && issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)),
            NewWarningCount = issues.Count(issue =>
                issue.IsNew && issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)),
            Issues = issues.ToArray(),
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    private sealed class PageScan
    {
        public string RelativePath { get; init; } = string.Empty;
        public string Route { get; init; } = "/";
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string BodyText { get; init; } = string.Empty;
        public string FocusKeyphrase { get; set; } = string.Empty;
        public string CanonicalHref { get; set; } = string.Empty;
        public List<HreflangAlternateScan> HreflangAlternates { get; } = new();
        public HashSet<string> OutboundRoutes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class HreflangAlternateScan
    {
        public string HrefLang { get; init; } = string.Empty;
        public string Href { get; init; } = string.Empty;
    }

    private static IEnumerable<string> EnumerateHtmlFiles(string root, string[] includePatterns, string[] excludePatterns, bool useDefaultExcludes)
    {
        var includes = NormalizePatterns(includePatterns);
        var excludes = BuildExcludePatterns(excludePatterns, useDefaultExcludes);
        var files = Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.htm", SearchOption.AllDirectories));

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (excludes.Length > 0 && MatchesAny(excludes, relative))
                continue;
            if (includes.Length > 0 && !MatchesAny(includes, relative))
                continue;
            yield return file;
        }
    }

    private static string[] NormalizePatterns(string[] patterns)
    {
        return (patterns ?? Array.Empty<string>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Replace('\\', '/').Trim())
            .ToArray();
    }

    private static string[] BuildExcludePatterns(string[] patterns, bool useDefaults)
    {
        var list = NormalizePatterns(patterns).ToList();
        if (useDefaults)
            list.AddRange(DefaultExcludePatterns);
        return list
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesAny(string[] patterns, string value)
    {
        foreach (var pattern in patterns)
        {
            if (GlobMatch(pattern, value))
                return true;
        }
        return false;
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";
        try
        {
            return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, GlobMatchRegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool HasNoIndexRobots(AngleSharp.Dom.IDocument doc)
    {
        if (doc.Head is null)
            return false;

        foreach (var meta in doc.Head.QuerySelectorAll("meta[name]"))
        {
            var name = meta.GetAttribute("name");
            if (string.IsNullOrWhiteSpace(name) ||
                !NoIndexMetaNames.Any(known => known.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var content = meta.GetAttribute("content");
            if (!string.IsNullOrWhiteSpace(content) &&
                content.IndexOf("noindex", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string[] GetCanonicalLinks(AngleSharp.Dom.IDocument doc)
    {
        if (doc.Head is null)
            return Array.Empty<string>();

        return doc.Head.QuerySelectorAll("link[rel][href]")
            .Where(link => ContainsRelToken(link.GetAttribute("rel"), "canonical"))
            .Select(link => NormalizeWhitespace(link.GetAttribute("href")))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .ToArray();
    }

    private static void ValidateCanonical(
        string relativePath,
        bool requireCanonical,
        string[] canonicalLinks,
        Action<string, string, string?, string, string?, string?> addIssue)
    {
        canonicalLinks ??= Array.Empty<string>();

        if (canonicalLinks.Length == 0)
        {
            if (requireCanonical)
                addIssue("warning", "canonical", relativePath, "missing canonical link.", "canonical-missing", null);
            return;
        }

        if (canonicalLinks.Length > 1)
        {
            addIssue("warning", "canonical", relativePath,
                $"duplicate canonical links detected ({canonicalLinks.Length}).",
                "canonical-duplicate",
                canonicalLinks[0]);
        }

        foreach (var href in canonicalLinks.Where(href => !IsAbsoluteHttpUrl(href)))
        {
            addIssue("warning", "canonical", relativePath,
                $"canonical link should be an absolute http(s) URL but was '{href}'.",
                "canonical-absolute",
                href);
        }
    }

    private static HreflangAlternateScan[] GetHreflangAlternates(AngleSharp.Dom.IDocument doc)
    {
        if (doc.Head is null)
            return Array.Empty<HreflangAlternateScan>();

        return doc.Head.QuerySelectorAll("link[rel][hreflang][href]")
            .Where(link => ContainsRelToken(link.GetAttribute("rel"), "alternate"))
            .Select(link => new HreflangAlternateScan
            {
                HrefLang = NormalizeWhitespace(link.GetAttribute("hreflang")).ToLowerInvariant(),
                Href = NormalizeWhitespace(link.GetAttribute("href"))
            })
            .Where(value => !string.IsNullOrWhiteSpace(value.HrefLang) && !string.IsNullOrWhiteSpace(value.Href))
            .ToArray();
    }

    private static void ValidateHreflang(
        string relativePath,
        bool requireHreflang,
        bool requireXDefault,
        HreflangAlternateScan[] alternates,
        Action<string, string, string?, string, string?, string?> addIssue)
    {
        alternates ??= Array.Empty<HreflangAlternateScan>();

        if (alternates.Length == 0)
        {
            if (requireHreflang)
                addIssue("warning", "hreflang", relativePath, "missing hreflang alternates.", "hreflang-missing", null);
            return;
        }

        var duplicateLanguageGroups = alternates
            .GroupBy(value => value.HrefLang, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var group in duplicateLanguageGroups)
        {
            addIssue("warning", "hreflang", relativePath,
                $"duplicate hreflang '{group.Key}' entries detected ({group.Count()}).",
                "hreflang-duplicate",
                group.Key);
        }

        foreach (var alternate in alternates)
        {
            if (!HreflangTokenPattern.IsMatch(alternate.HrefLang))
            {
                addIssue("warning", "hreflang", relativePath,
                    $"invalid hreflang value '{alternate.HrefLang}'.",
                    "hreflang-invalid",
                    alternate.HrefLang);
            }

            if (!IsAbsoluteHttpUrl(alternate.Href))
            {
                addIssue("warning", "hreflang", relativePath,
                    $"hreflang '{alternate.HrefLang}' should use an absolute http(s) URL but was '{alternate.Href}'.",
                    "hreflang-absolute",
                    $"{alternate.HrefLang}|{alternate.Href}");
            }
        }

        if (requireXDefault &&
            alternates.All(value => !value.HrefLang.Equals("x-default", StringComparison.OrdinalIgnoreCase)))
        {
            addIssue("warning", "hreflang", relativePath,
                "hreflang alternates are present but x-default is missing.",
                "hreflang-x-default-missing",
                null);
        }
    }

    private static void ValidateLocalizedAlternateTargets(
        IReadOnlyList<PageScan> sourcePages,
        IReadOnlyList<PageScan> lookupPages,
        Action<string, string, string?, string, string?, string?> addIssue)
    {
        var routeIndex = lookupPages
            .Where(page => !string.IsNullOrWhiteSpace(page.Route))
            .GroupBy(page => page.Route, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var canonicalIndex = lookupPages
            .Where(page => !string.IsNullOrWhiteSpace(page.CanonicalHref))
            .Where(IsCanonicalConsistentWithPageRoute)
            .Select(page => (Page: page, Key: TryGetComparableUrlKey(page.CanonicalHref)))
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
            .GroupBy(pair => pair.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Page, StringComparer.OrdinalIgnoreCase);

        foreach (var page in sourcePages.Where(page => page.HreflangAlternates.Count > 0))
        {
            if (TryResolveReferencedPage(routeIndex, canonicalIndex, page.CanonicalHref, out var canonicalPage, out var canonicalRoute) &&
                canonicalPage is null)
            {
                addIssue("warning", "canonical", page.RelativePath,
                    $"canonical '{page.CanonicalHref}' resolves to '{canonicalRoute}' but no generated page exists at that route.",
                    "canonical-route-missing",
                    canonicalRoute);
            }

            var hasSelfAlternate = page.HreflangAlternates
                .Where(alternate => !alternate.HrefLang.Equals("x-default", StringComparison.OrdinalIgnoreCase))
                .Any(alternate => HrefMatchesPage(page, alternate.Href, routeIndex, canonicalIndex));
            if (!hasSelfAlternate)
            {
                addIssue("warning", "hreflang", page.RelativePath,
                    "hreflang alternates are present but none point back to this page's canonical/current route.",
                    "hreflang-self-missing",
                    page.Route);
            }

            foreach (var alternate in page.HreflangAlternates)
            {
                if (!TryResolveReferencedPage(routeIndex, canonicalIndex, alternate.Href, out var targetPage, out var alternateRoute))
                    continue;

                if (targetPage is null)
                {
                    addIssue("warning", "hreflang", page.RelativePath,
                        $"hreflang '{alternate.HrefLang}' points to '{alternate.Href}' which resolves to '{alternateRoute}' but no generated page exists at that route.",
                        "hreflang-route-missing",
                        $"{alternate.HrefLang}|{alternateRoute}");
                    continue;
                }

                if (!alternate.HrefLang.Equals("x-default", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(targetPage.CanonicalHref) &&
                    !UrlsReferenceSamePage(alternate.Href, targetPage.CanonicalHref, routeIndex))
                {
                    addIssue("warning", "hreflang", page.RelativePath,
                        $"hreflang '{alternate.HrefLang}' points to '{alternate.Href}' but the target page canonical is '{targetPage.CanonicalHref}'.",
                        "hreflang-target-canonical-mismatch",
                        $"{alternate.HrefLang}|{alternateRoute}");
                }

                if (targetPage.Route.Equals(page.Route, StringComparison.OrdinalIgnoreCase) ||
                    alternate.HrefLang.Equals("x-default", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hasReturnLink = targetPage.HreflangAlternates.Any(targetAlternate =>
                    HrefMatchesPage(page, targetAlternate.Href, routeIndex, canonicalIndex));
                if (!hasReturnLink)
                {
                    addIssue("warning", "hreflang", page.RelativePath,
                        $"hreflang '{alternate.HrefLang}' points to '{alternate.Href}' but the target page does not return-link to this page.",
                        "hreflang-return-link-missing",
                        $"{alternate.HrefLang}|{alternateRoute}");
                }
            }
        }
    }

    private static void ValidateContentLeaks(
        string relativePath,
        string bodyText,
        Action<string, string, string?, string, string?, string?> addIssue)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
            return;

        var looksLikeEscapedHtmlBlock =
            bodyText.IndexOf("<div", StringComparison.OrdinalIgnoreCase) >= 0 ||
            bodyText.IndexOf("<section", StringComparison.OrdinalIgnoreCase) >= 0 ||
            bodyText.IndexOf("<article", StringComparison.OrdinalIgnoreCase) >= 0 ||
            bodyText.IndexOf("<h1", StringComparison.OrdinalIgnoreCase) >= 0 ||
            bodyText.IndexOf("<h2", StringComparison.OrdinalIgnoreCase) >= 0;

        var frontMatterKeys = FrontMatterLeakPattern.Matches(bodyText)
            .Select(match => match.Groups["key"].Value)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => key.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasFrontMatterTokens = frontMatterKeys.Length > 0;
        var hasHighConfidenceFrontMatterTokens = frontMatterKeys.Any(static key =>
            key.Equals("translation_key", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("meta.raw_html", StringComparison.OrdinalIgnoreCase));
        var looksLikeFrontMatterDump =
            hasHighConfidenceFrontMatterTokens ||
            frontMatterKeys.Length >= 3 ||
            bodyText.IndexOf("translation_key:", StringComparison.OrdinalIgnoreCase) >= 0 ||
            bodyText.IndexOf("meta.raw_html", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (bodyText.IndexOf("layout:", StringComparison.OrdinalIgnoreCase) >= 0 && hasFrontMatterTokens);

        var looksLikeMarkdownLeak = looksLikeEscapedHtmlBlock && VisibleMarkdownLeakPattern.IsMatch(bodyText);
        if (!looksLikeFrontMatterDump && !looksLikeMarkdownLeak)
            return;

        var sample = BuildLeakSample(bodyText);
        if (looksLikeFrontMatterDump)
        {
            addIssue("error", "content", relativePath,
                $"rendered page appears to expose front matter or raw HTML as visible text. Sample: {sample}",
                "content-frontmatter-leak",
                sample);
            return;
        }

        if (looksLikeMarkdownLeak)
        {
            addIssue("error", "content", relativePath,
                $"rendered page appears to expose Markdown syntax as visible text. Sample: {sample}",
                "content-markdown-leak",
                sample);
        }
    }

    private static void ValidatePageAssertions(
        string siteRoot,
        IReadOnlyList<WebSeoDoctorPageAssertion>? assertions,
        Action<string, string, string?, string, string?, string?> addIssue)
    {
        if (assertions is null || assertions.Count == 0)
            return;

        foreach (var assertion in assertions)
        {
            if (assertion is null || string.IsNullOrWhiteSpace(assertion.Path))
                continue;

            var displayName = GetPageAssertionDisplayName(assertion);
            if (!TryResolvePageAssertionPath(siteRoot, assertion.Path, out var assertedFile, out var relativePath))
            {
                if (assertion.MustExist)
                {
                    AddPageAssertionIssue(
                        addIssue,
                        relativePath,
                        displayName,
                        NormalizePageAssertionScope(assertion.Scope),
                        "page-assertion-missing-page",
                        "asserted page is missing.",
                        assertion.Path);
                }

                continue;
            }

            string html;
            try
            {
                html = File.ReadAllText(assertedFile);
            }
            catch (Exception ex)
            {
                AddPageAssertionIssue(
                    addIssue,
                    relativePath,
                    displayName,
                    NormalizePageAssertionScope(assertion.Scope),
                    "page-assertion-read",
                    $"failed to read asserted page ({ex.Message}).",
                    ex.GetType().Name);
                continue;
            }

            string inspectedText;
            var scope = NormalizePageAssertionScope(assertion.Scope);
            if (scope.Equals("html", StringComparison.OrdinalIgnoreCase))
            {
                inspectedText = NormalizeWhitespace(html);
            }
            else
            {
                AngleSharp.Dom.IDocument doc;
                try
                {
                    doc = HtmlParser.ParseWithAngleSharp(html);
                }
                catch (Exception ex)
                {
                    AddPageAssertionIssue(
                        addIssue,
                        relativePath,
                        displayName,
                        scope,
                        "page-assertion-parse",
                        $"failed to parse asserted page ({ex.Message}).",
                        ex.GetType().Name);
                    continue;
                }

                inspectedText = GetVisibleBodyText(doc.Body);
            }

            foreach (var expected in NormalizePageAssertionValues(assertion.Contains))
            {
                if (inspectedText.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                AddPageAssertionIssue(
                    addIssue,
                    relativePath,
                    displayName,
                    scope,
                    "page-assertion-contains",
                    $"missing expected {scope} text '{expected}'.",
                    expected);
            }

            foreach (var forbidden in NormalizePageAssertionValues(assertion.NotContains))
            {
                if (inspectedText.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                AddPageAssertionIssue(
                    addIssue,
                    relativePath,
                    displayName,
                    scope,
                    "page-assertion-not-contains",
                    $"contains forbidden {scope} text '{forbidden}'.",
                    forbidden);
            }
        }
    }

    private static void AddPageAssertionIssue(
        Action<string, string, string?, string, string?, string?> addIssue,
        string relativePath,
        string displayName,
        string scope,
        string hint,
        string detail,
        string? keyHint)
    {
        addIssue(
            "error",
            "page-assertion",
            relativePath,
            $"page assertion '{displayName}' failed: {detail}",
            hint,
            $"{scope}|{keyHint}");
    }

    private static string[] NormalizePageAssertionValues(string[]? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeWhitespace)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePageAssertionScope(string? scope)
    {
        var normalized = NormalizeWhitespace(scope);
        if (normalized.Equals("html", StringComparison.OrdinalIgnoreCase))
            return "html";
        if (normalized.Equals("rendered", StringComparison.OrdinalIgnoreCase))
            return "body";
        return "body";
    }

    private static string GetPageAssertionDisplayName(WebSeoDoctorPageAssertion assertion)
    {
        var label = NormalizeWhitespace(assertion.Label);
        if (!string.IsNullOrWhiteSpace(label))
            return label;

        var normalizedPath = NormalizeAssertionRelativePath(assertion.Path);
        return string.IsNullOrWhiteSpace(normalizedPath) ? assertion.Path.Trim() : normalizedPath;
    }

    private static bool TryResolvePageAssertionPath(
        string siteRoot,
        string assertionPath,
        out string resolvedPath,
        out string relativePath)
    {
        resolvedPath = string.Empty;
        relativePath = NormalizeAssertionRelativePath(assertionPath);
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var candidate = Path.GetFullPath(Path.Combine(siteRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithinRoot(NormalizeRootPath(siteRoot), candidate))
            return false;

        resolvedPath = candidate;
        relativePath = Path.GetRelativePath(siteRoot, candidate).Replace('\\', '/');
        return File.Exists(candidate);
    }

    private static string NormalizeAssertionRelativePath(string? assertionPath)
    {
        var normalized = StripQueryAndFragment(assertionPath ?? string.Empty)
            .Replace('\\', '/')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Equals("/", StringComparison.Ordinal))
            return "index.html";

        normalized = normalized.TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return "index.html";

        var explicitDirectory = normalized.EndsWith("/", StringComparison.Ordinal);
        normalized = normalized.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return "index.html";

        if (explicitDirectory)
            return normalized + "/index.html";

        if (Path.HasExtension(normalized))
            return normalized;

        return normalized + "/index.html";
    }

    private static void ValidateStructuredData(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        bool requireStructuredData,
        Action<string, string, string?, string, string?, string?> addIssue)
    {
        var scripts = doc.QuerySelectorAll("script[type='application/ld+json']")
            .Select(script => NormalizeWhitespace(script.TextContent))
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToArray();

        if (scripts.Length == 0)
        {
            if (requireStructuredData)
                addIssue("warning", "structured-data", relativePath, "missing JSON-LD structured data block.", "structured-data-missing", null);
            return;
        }

        for (var i = 0; i < scripts.Length; i++)
        {
            var content = scripts[i];
            var itemLabel = $"item-{i + 1}";
            if (content.Length > MaxJsonLdPayloadLength)
            {
                addIssue("warning", "structured-data", relativePath,
                    $"JSON-LD payload ({itemLabel}) exceeds {MaxJsonLdPayloadLength} characters and was skipped.",
                    "structured-data-payload-too-large",
                    itemLabel);
                continue;
            }

            JsonDocument parsed;
            try
            {
                parsed = JsonDocument.Parse(content);
            }
            catch (Exception ex)
            {
                addIssue("warning", "structured-data", relativePath,
                    $"invalid JSON-LD payload ({itemLabel}): {ex.Message}.",
                    "structured-data-json-invalid",
                    itemLabel);
                continue;
            }

            using (parsed)
            {
                if (!TryValidateJsonLdElement(parsed.RootElement, out var hasContext, out var hasType))
                {
                    addIssue("warning", "structured-data", relativePath,
                        $"JSON-LD payload ({itemLabel}) should be an object or array of objects.",
                        "structured-data-shape",
                        itemLabel);
                    continue;
                }

                if (!hasContext)
                {
                    addIssue("warning", "structured-data", relativePath,
                        $"JSON-LD payload ({itemLabel}) is missing @context.",
                        "structured-data-missing-context",
                        itemLabel);
                }

                if (!hasType)
                {
                    addIssue("warning", "structured-data", relativePath,
                        $"JSON-LD payload ({itemLabel}) is missing @type.",
                        "structured-data-missing-type",
                        itemLabel);
                }

                ValidateStructuredDataProfiles(parsed.RootElement, relativePath, itemLabel, addIssue);
            }
        }
    }

    private static string BuildLeakSample(string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
            return string.Empty;

        var sample = NormalizeWhitespace(bodyText);
        if (sample.Length <= 160)
            return sample;

        return sample[..160] + "…";
    }

    private static List<PageScan> LoadReferencePages(IReadOnlyList<string> siteRoots)
    {
        var pages = new List<PageScan>();
        foreach (var siteRoot in siteRoots)
        {
            foreach (var file in EnumerateHtmlFiles(siteRoot, Array.Empty<string>(), Array.Empty<string>(), useDefaultExcludes: true))
            {
                string html;
                try
                {
                    html = File.ReadAllText(file);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(html))
                    continue;

                AngleSharp.Dom.IDocument doc;
                try
                {
                    doc = HtmlParser.ParseWithAngleSharp(html);
                }
                catch
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
                var page = new PageScan
                {
                    RelativePath = relativePath,
                    Route = ToRoute(relativePath),
                    Title = NormalizeWhitespace(doc.Title),
                    Description = GetMetaNameValue(doc, "description"),
                    BodyText = string.Empty,
                    CanonicalHref = GetCanonicalLinks(doc).FirstOrDefault() ?? string.Empty
                };
                page.HreflangAlternates.AddRange(GetHreflangAlternates(doc));
                pages.Add(page);
            }
        }

        return pages;
    }

    private static void ValidateStructuredDataProfiles(
        JsonElement root,
        string relativePath,
        string itemLabel,
        Action<string, string, string?, string, string?, string?> addIssue)
    {
        var objectIndex = 0;
        foreach (var obj in EnumerateJsonLdObjects(root))
        {
            objectIndex++;
            var objectLabel = $"{itemLabel}-obj-{objectIndex}";
            var types = GetJsonLdTypes(obj);
            if (types.Length == 0)
                continue;

            foreach (var type in types)
            {
                if (type.Equals("FAQPage", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateFaqProfile(obj, relativePath, objectLabel, addIssue);
                    continue;
                }

                if (type.Equals("HowTo", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateHowToProfile(obj, relativePath, objectLabel, addIssue);
                    continue;
                }

                if (type.Equals("Product", StringComparison.OrdinalIgnoreCase))
                {
                    if (!JsonLdObjectHasNonEmptyValue(obj, "name"))
                    {
                        addIssue("warning", "structured-data", relativePath,
                            $"JSON-LD payload ({objectLabel}) type Product should include name.",
                            "structured-data-product-name",
                            objectLabel);
                    }
                    continue;
                }

                if (type.Equals("SoftwareApplication", StringComparison.OrdinalIgnoreCase))
                {
                    if (!JsonLdObjectHasNonEmptyValue(obj, "name"))
                    {
                        addIssue("warning", "structured-data", relativePath,
                            $"JSON-LD payload ({objectLabel}) type SoftwareApplication should include name.",
                            "structured-data-software-name",
                            objectLabel);
                    }
                    continue;
                }

                if (type.Equals("Article", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("NewsArticle", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateArticleLikeProfile(obj, type, relativePath, objectLabel, addIssue);
                }
            }
        }
    }

    private static void ValidateFaqProfile(
        JsonElement obj,
        string relativePath,
        string objectLabel,
        Action<string, string, string?, string, string?, string?> addIssue)
    {
        if (!obj.TryGetProperty("mainEntity", out var mainEntity))
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) type FAQPage should include mainEntity.",
                "structured-data-faq-main-entity",
                objectLabel);
            return;
        }

        var questions = mainEntity.ValueKind switch
        {
            JsonValueKind.Array => mainEntity.EnumerateArray().Where(static element => element.ValueKind == JsonValueKind.Object).ToArray(),
            JsonValueKind.Object => new[] { mainEntity },
            _ => Array.Empty<JsonElement>()
        };

        if (questions.Length == 0)
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) FAQPage mainEntity should contain Question entries.",
                "structured-data-faq-empty",
                objectLabel);
            return;
        }

        var hasInvalidQuestion = questions.Any(question =>
            !JsonLdObjectHasNonEmptyValue(question, "name") ||
            !TryGetFaqAnswerText(question));
        if (hasInvalidQuestion)
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) FAQPage questions should include name and acceptedAnswer.text.",
                "structured-data-faq-question-shape",
                objectLabel);
        }
    }

    private static bool TryGetFaqAnswerText(JsonElement question)
    {
        if (!question.TryGetProperty("acceptedAnswer", out var answer))
            return false;

        if (answer.ValueKind == JsonValueKind.Object)
            return JsonLdObjectHasNonEmptyValue(answer, "text");

        if (answer.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var item in answer.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && JsonLdObjectHasNonEmptyValue(item, "text"))
                return true;
        }

        return false;
    }

    private static void ValidateHowToProfile(
        JsonElement obj,
        string relativePath,
        string objectLabel,
        Action<string, string, string?, string, string?, string?> addIssue)
    {
        if (!JsonLdObjectHasNonEmptyValue(obj, "name"))
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) type HowTo should include name.",
                "structured-data-howto-name",
                objectLabel);
        }

        if (!obj.TryGetProperty("step", out var step))
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) type HowTo should include step.",
                "structured-data-howto-step",
                objectLabel);
            return;
        }

        var steps = step.ValueKind switch
        {
            JsonValueKind.Array => step.EnumerateArray().Where(static element => element.ValueKind == JsonValueKind.Object).ToArray(),
            JsonValueKind.Object => new[] { step },
            _ => Array.Empty<JsonElement>()
        };

        if (steps.Length == 0)
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) HowTo step should contain HowToStep entries.",
                "structured-data-howto-empty-step",
                objectLabel);
            return;
        }

        var hasInvalidStep = steps.Any(item =>
            !JsonLdObjectHasNonEmptyValue(item, "name") &&
            !JsonLdObjectHasNonEmptyValue(item, "text"));
        if (hasInvalidStep)
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) HowTo steps should include name or text.",
                "structured-data-howto-step-shape",
                objectLabel);
        }
    }

    private static void ValidateArticleLikeProfile(
        JsonElement obj,
        string type,
        string relativePath,
        string objectLabel,
        Action<string, string, string?, string, string?, string?> addIssue)
    {
        if (!JsonLdObjectHasNonEmptyValue(obj, "headline"))
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) type {type} should include headline.",
                "structured-data-article-headline",
                objectLabel);
        }

        if (!JsonLdObjectHasNonEmptyValue(obj, "author"))
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) type {type} should include author.",
                "structured-data-article-author",
                objectLabel);
        }

        if (!JsonLdObjectHasNonEmptyValue(obj, "publisher"))
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) type {type} should include publisher.",
                "structured-data-article-publisher",
                objectLabel);
        }

        if (type.Equals("NewsArticle", StringComparison.OrdinalIgnoreCase) &&
            !JsonLdObjectHasNonEmptyValue(obj, "datePublished"))
        {
            addIssue("warning", "structured-data", relativePath,
                $"JSON-LD payload ({objectLabel}) type NewsArticle should include datePublished.",
                "structured-data-news-date-published",
                objectLabel);
        }
    }

    private static JsonElement[] EnumerateJsonLdObjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
            return new[] { root };

        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray()
                .Where(static value => value.ValueKind == JsonValueKind.Object)
                .ToArray();
        }

        return Array.Empty<JsonElement>();
    }

    private static string[] GetJsonLdTypes(JsonElement obj)
    {
        if (!obj.TryGetProperty("@type", out var typeValue))
            return Array.Empty<string>();

        if (typeValue.ValueKind == JsonValueKind.String)
        {
            var type = NormalizeWhitespace(typeValue.GetString());
            return string.IsNullOrWhiteSpace(type) ? Array.Empty<string>() : new[] { type };
        }

        if (typeValue.ValueKind == JsonValueKind.Array)
        {
            return typeValue.EnumerateArray()
                .Where(static value => value.ValueKind == JsonValueKind.String)
                .Select(value => NormalizeWhitespace(value.GetString()))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static bool TryValidateJsonLdElement(JsonElement root, out bool hasContext, out bool hasType)
    {
        hasContext = false;
        hasType = false;

        if (root.ValueKind == JsonValueKind.Object)
        {
            hasContext = JsonLdObjectHasNonEmptyValue(root, "@context");
            hasType = JsonLdObjectHasNonEmptyValue(root, "@type");
            return true;
        }

        if (root.ValueKind != JsonValueKind.Array)
            return false;

        var anyObject = false;
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            anyObject = true;
            if (JsonLdObjectHasNonEmptyValue(item, "@context"))
                hasContext = true;
            if (JsonLdObjectHasNonEmptyValue(item, "@type"))
                hasType = true;
        }

        return anyObject;
    }

    private static bool JsonLdObjectHasNonEmptyValue(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.EnumerateArray().Any(item => item.ValueKind switch
            {
                JsonValueKind.String => !string.IsNullOrWhiteSpace(item.GetString()),
                JsonValueKind.Object => true,
                _ => false
            }),
            JsonValueKind.Object => value.EnumerateObject().Any(),
            JsonValueKind.Number => true,
            JsonValueKind.True => true,
            JsonValueKind.False => true,
            _ => false
        };
    }

    private static bool ContainsRelToken(string? relValue, string token)
    {
        if (string.IsNullOrWhiteSpace(relValue) || string.IsNullOrWhiteSpace(token))
            return false;

        foreach (var part in relValue.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsAbsoluteHttpUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveReferencedRoute(
        IReadOnlyDictionary<string, PageScan> routeIndex,
        string href,
        out string route)
    {
        route = string.Empty;
        if (!TryGetUrlRouteCandidate(href, out var candidate))
            return false;

        foreach (var possibleRoute in EnumerateRouteCandidates(candidate))
        {
            if (routeIndex.ContainsKey(possibleRoute))
            {
                route = possibleRoute;
                return true;
            }
        }

        route = candidate;
        return true;
    }

    private static bool TryResolveReferencedPage(
        IReadOnlyDictionary<string, PageScan> routeIndex,
        IReadOnlyDictionary<string, PageScan> canonicalIndex,
        string href,
        out PageScan? page,
        out string route)
    {
        page = null;
        route = string.Empty;

        var comparableKey = TryGetComparableUrlKey(href);
        if (!string.IsNullOrWhiteSpace(comparableKey) &&
            canonicalIndex.TryGetValue(comparableKey, out var canonicalPage))
        {
            page = canonicalPage;
            route = canonicalPage.Route;
            return true;
        }

        if (!TryResolveReferencedRoute(routeIndex, href, out route))
            return false;

        routeIndex.TryGetValue(route, out page);
        return true;
    }

    private static bool TryGetUrlRouteCandidate(string href, out string route)
    {
        route = string.Empty;
        if (string.IsNullOrWhiteSpace(href))
            return false;

        string path;
        if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            path = absoluteUri.AbsolutePath;
        }
        else if (href.StartsWith("/", StringComparison.Ordinal))
        {
            path = StripQueryAndFragment(href);
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
            return false;

        path = Uri.UnescapeDataString(path).Replace('\\', '/');
        route = ToRouteFromUrlPath(path);
        return !string.IsNullOrWhiteSpace(route);
    }

    private static IEnumerable<string> EnumerateRouteCandidates(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            yield break;

        yield return route;

        if (route.Equals("/", StringComparison.OrdinalIgnoreCase))
            yield break;

        if (route.EndsWith("/", StringComparison.Ordinal))
        {
            var trimmed = route.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
            yield break;
        }

        if (!Path.HasExtension(route))
            yield return route + "/";
    }

    private static string ToRouteFromUrlPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var normalizedPath = StripQueryAndFragment(path).Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath.Equals("/", StringComparison.Ordinal))
            return "/";

        return ToRoute(normalizedPath.TrimStart('/'));
    }

    private static bool HrefMatchesPage(
        PageScan page,
        string href,
        IReadOnlyDictionary<string, PageScan> routeIndex,
        IReadOnlyDictionary<string, PageScan> canonicalIndex)
    {
        if (TryResolveReferencedPage(routeIndex, canonicalIndex, href, out var referencedPage, out var route) &&
            ((referencedPage is not null && referencedPage.Route.Equals(page.Route, StringComparison.OrdinalIgnoreCase)) ||
             route.Equals(page.Route, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(page.CanonicalHref) ||
            !TryResolveReferencedRoute(routeIndex, page.CanonicalHref, out var canonicalRoute) ||
            !canonicalRoute.Equals(page.Route, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return UrlsReferenceSamePage(href, page.CanonicalHref, routeIndex);
    }

    private static string? TryGetComparableUrlKey(string value)
    {
        return TryNormalizeAbsoluteComparableUrl(value, out var comparable) ? comparable : null;
    }

    private static bool IsCanonicalConsistentWithPageRoute(PageScan page)
    {
        if (string.IsNullOrWhiteSpace(page.CanonicalHref) || string.IsNullOrWhiteSpace(page.Route))
            return false;

        if (!TryGetUrlRouteCandidate(page.CanonicalHref, out var canonicalRoute))
            return false;

        return EnumerateRouteCandidates(canonicalRoute)
            .Any(candidate => candidate.Equals(page.Route, StringComparison.OrdinalIgnoreCase));
    }

    private static bool UrlsReferenceSamePage(
        string left,
        string right,
        IReadOnlyDictionary<string, PageScan> routeIndex)
    {
        if (TryNormalizeAbsoluteComparableUrl(left, out var leftComparable) &&
            TryNormalizeAbsoluteComparableUrl(right, out var rightComparable))
        {
            return leftComparable.Equals(rightComparable, StringComparison.OrdinalIgnoreCase);
        }

        return TryResolveReferencedRoute(routeIndex, left, out var leftRoute) &&
               TryResolveReferencedRoute(routeIndex, right, out var rightRoute) &&
               leftRoute.Equals(rightRoute, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeAbsoluteComparableUrl(string value, out string comparable)
    {
        comparable = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = ToRouteFromUrlPath(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(path))
            path = "/";

        var host = uri.IdnHost.ToLowerInvariant();
        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
        comparable = host + port + path;
        return true;
    }

    private static string GetMetaNameValue(AngleSharp.Dom.IDocument doc, string metaName)
    {
        if (doc.Head is null || string.IsNullOrWhiteSpace(metaName))
            return string.Empty;

        foreach (var meta in doc.Head.QuerySelectorAll("meta[name]"))
        {
            var name = meta.GetAttribute("name");
            if (!string.Equals(name, metaName, StringComparison.OrdinalIgnoreCase))
                continue;
            return NormalizeWhitespace(meta.GetAttribute("content"));
        }

        return string.Empty;
    }

    private static string GetVisibleBodyText(AngleSharp.Dom.IElement? body)
    {
        if (body is null)
            return string.Empty;

        var clone = body.Clone(true) as AngleSharp.Dom.IElement;
        if (clone is null)
            return NormalizeWhitespace(body.TextContent);

        foreach (var element in clone.QuerySelectorAll("script,style,template,noscript"))
            element.Remove();

        return NormalizeWhitespace(clone.TextContent);
    }

    private static string GetFocusKeyphrase(AngleSharp.Dom.IDocument doc, string[] metaNames)
    {
        foreach (var metaName in metaNames)
        {
            var value = GetMetaNameValue(doc, metaName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static int CountCaseInsensitiveOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
            return 0;

        var count = 0;
        var index = 0;
        while (true)
        {
            index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static bool IsElementHidden(AngleSharp.Dom.IElement element)
    {
        if (element is null)
            return true;
        if (element.HasAttribute("hidden"))
            return true;
        var ariaHidden = element.GetAttribute("aria-hidden");
        return !string.IsNullOrWhiteSpace(ariaHidden) &&
               ariaHidden.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkipLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return true;

        foreach (var prefix in IgnoreLinkPrefixes)
        {
            if (href.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsExternalLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return false;
        if (href.StartsWith("//", StringComparison.Ordinal))
            return true;
        return href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               href.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripQueryAndFragment(string href)
    {
        var trimmed = href.Trim();
        var queryIndex = trimmed.IndexOf('?');
        if (queryIndex >= 0)
            trimmed = trimmed.Substring(0, queryIndex);
        var hashIndex = trimmed.IndexOf('#');
        if (hashIndex >= 0)
            trimmed = trimmed.Substring(0, hashIndex);
        return trimmed;
    }

    private static string ResolveBaseDirectory(string siteRoot, string filePath, AngleSharp.Dom.IDocument doc)
    {
        var baseHref = ResolveBaseHref(doc);
        if (string.IsNullOrWhiteSpace(baseHref))
            return Path.GetDirectoryName(filePath) ?? siteRoot;

        return Path.GetFullPath(Path.Combine(siteRoot, baseHref.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string? ResolveBaseHref(AngleSharp.Dom.IDocument doc)
    {
        var baseElement = doc.QuerySelector("base[href]");
        if (baseElement is null) return null;
        var href = baseElement.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (IsExternalLink(href)) return null;
        if (!href.StartsWith("/", StringComparison.Ordinal))
            return null;
        return href.TrimEnd('/');
    }

    private static string NormalizeRootPath(string rootPath)
    {
        var full = Path.GetFullPath(rootPath);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsPathWithinRoot(string normalizedRoot, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(normalizedRoot, FileSystemPathComparison);
    }

    private static bool TryResolveLocalTarget(string siteRoot, string baseDir, string href, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(href)) return false;

        var cleaned = StripQueryAndFragment(href);
        if (string.IsNullOrWhiteSpace(cleaned)) return false;

        var isRooted = cleaned.StartsWith("/");
        var isExplicitDir = cleaned.EndsWith("/");
        var relative = isRooted ? cleaned.TrimStart('/') : cleaned;
        relative = relative.Replace('/', Path.DirectorySeparatorChar);

        var candidateBase = isRooted ? siteRoot : baseDir;
        var candidate = Path.GetFullPath(Path.Combine(candidateBase, relative));
        if (!IsPathWithinRoot(NormalizeRootPath(siteRoot), candidate))
            return false;

        if (isExplicitDir)
        {
            var dir = candidate.TrimEnd(Path.DirectorySeparatorChar);
            var indexPath = Path.Combine(dir, "index.html");
            resolvedPath = indexPath;
            return true;
        }

        if (Path.HasExtension(candidate))
        {
            resolvedPath = candidate;
            return true;
        }

        foreach (var ext in DefaultHtmlExtensions)
        {
            var htmlCandidate = candidate + ext;
            if (File.Exists(htmlCandidate))
            {
                resolvedPath = htmlCandidate;
                return true;
            }
        }

        resolvedPath = Path.Combine(candidate, "index.html");
        return true;
    }

    private static string ToRoute(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "/";

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return "/";

        if (normalized.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("index.htm", StringComparison.OrdinalIgnoreCase))
        {
            return "/";
        }

        if (normalized.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            return "/" + normalized[..^"/index.html".Length] + "/";
        if (normalized.EndsWith("/index.htm", StringComparison.OrdinalIgnoreCase))
            return "/" + normalized[..^"/index.htm".Length] + "/";

        return "/" + normalized;
    }

    private static bool TryResolveDirectoryAliasRoute(string siteRoot, string relativePath, out string canonicalRoute)
    {
        canonicalRoute = string.Empty;
        if (string.IsNullOrWhiteSpace(siteRoot) || string.IsNullOrWhiteSpace(relativePath))
            return false;

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;
        if (normalized.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("index.htm", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/index.htm", StringComparison.OrdinalIgnoreCase))
            return false;
        if (normalized.Equals("404.html", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("404.htm", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/404.html", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/404.htm", StringComparison.OrdinalIgnoreCase))
            return false;
        if (normalized.EndsWith(".scripts.html", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".scripts.htm", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".head.html", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".head.htm", StringComparison.OrdinalIgnoreCase))
            return false;

        var ext = Path.GetExtension(normalized);
        if (!ext.Equals(".html", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            return false;

        var withoutExtension = normalized[..^ext.Length];
        if (string.IsNullOrWhiteSpace(withoutExtension))
            return false;

        var directoryRoutePath = $"{withoutExtension}/index.html";
        var candidateFile = Path.Combine(siteRoot, directoryRoutePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(candidateFile))
            return false;

        canonicalRoute = $"/{withoutExtension.Trim('/')}/";
        return true;
    }

    private static bool IsOrphanCandidateRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return false;
        if (route.Equals("/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (route.Equals("/404.html", StringComparison.OrdinalIgnoreCase))
            return false;
        if (route.Equals("/sitemap/", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static string NormalizeIssueToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "general";
        var token = value.Trim().ToLowerInvariant();
        token = Regex.Replace(token, @"[^a-z0-9]+", "-");
        token = token.Trim('-');
        return string.IsNullOrWhiteSpace(token) ? "general" : token;
    }

    private static string BuildIssueCode(string category, string hint)
    {
        var categoryToken = NormalizeIssueToken(category).Replace('-', '_').ToUpperInvariant();
        var hintToken = NormalizeIssueToken(hint).Replace('-', '_').ToUpperInvariant();
        return $"PFSEO.{categoryToken}.{hintToken}";
    }

    private static string BuildIssueKey(string severity, string category, string? path, string hint, string? keyHint)
    {
        var pathToken = string.IsNullOrWhiteSpace(path) ? "-" : path.Replace('\\', '/').Trim().ToLowerInvariant();
        var hintToken = string.IsNullOrWhiteSpace(keyHint) ? hint : keyHint.Trim();
        return $"{NormalizeIssueToken(severity)}|{NormalizeIssueToken(category)}|{pathToken}|{NormalizeIssueToken(hintToken)}";
    }
}
