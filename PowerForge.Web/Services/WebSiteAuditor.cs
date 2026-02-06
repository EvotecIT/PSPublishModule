using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Options for static site audit.</summary>
public sealed class WebAuditOptions
{
    /// <summary>Root directory of the generated site.</summary>
    public string SiteRoot { get; set; } = ".";
    /// <summary>Optional include glob patterns (relative to site root).</summary>
    public string[] Include { get; set; } = Array.Empty<string>();
    /// <summary>Optional exclude glob patterns (relative to site root).</summary>
    public string[] Exclude { get; set; } = Array.Empty<string>();
    /// <summary>When true, apply built-in exclude patterns for partial HTML files.</summary>
    public bool UseDefaultExcludes { get; set; } = true;
    /// <summary>When true, validate HTML structure.</summary>
    public bool CheckHtmlStructure { get; set; } = true;
    /// <summary>When true, enforce non-empty page titles.</summary>
    public bool CheckTitles { get; set; } = true;
    /// <summary>When true, detect duplicate element IDs.</summary>
    public bool CheckDuplicateIds { get; set; } = true;
    /// <summary>When true, detect heading level skips (for example h2 -> h4).</summary>
    public bool CheckHeadingOrder { get; set; } = true;
    /// <summary>When true, warn when the same link label points to multiple destinations.</summary>
    public bool CheckLinkPurposeConsistency { get; set; } = true;
    /// <summary>When true, validate internal links.</summary>
    public bool CheckLinks { get; set; } = true;
    /// <summary>When true, validate local assets (CSS/JS/images).</summary>
    public bool CheckAssets { get; set; } = true;
    /// <summary>When true, validate external origin hints (preconnect/dns-prefetch).</summary>
    public bool CheckNetworkHints { get; set; } = true;
    /// <summary>When true, warn when too many render-blocking resources are in document head.</summary>
    public bool CheckRenderBlockingResources { get; set; } = true;
    /// <summary>Maximum allowed render-blocking resources in head before warning.</summary>
    public int MaxHeadBlockingResources { get; set; } = 6;
    /// <summary>When true, check nav consistency across pages.</summary>
    public bool CheckNavConsistency { get; set; } = true;
    /// <summary>CSS selector used to identify the nav container.</summary>
    public string NavSelector { get; set; } = "nav";
    /// <summary>Optional glob patterns to skip nav checks for specific pages.</summary>
    public string[] IgnoreNavFor { get; set; } = new[]
    {
        "api-docs/**",
        "docs/api/**",
        "api/**"
    };
    /// <summary>Require all pages to contain a nav element.</summary>
    public bool NavRequired { get; set; } = true;
    /// <summary>Skip nav checks on pages that match a prefix list (path-based).</summary>
    public string[] NavIgnorePrefixes { get; set; } = Array.Empty<string>();
    /// <summary>Optional list of links that must be present in the nav (for example "/").</summary>
    public string[] NavRequiredLinks { get; set; } = Array.Empty<string>();
    /// <summary>Optional per-path nav behavior overrides.</summary>
    public WebAuditNavProfile[] NavProfiles { get; set; } = Array.Empty<WebAuditNavProfile>();
    /// <summary>Minimum allowed percentage of nav-covered pages (checked / (checked + ignored)). 0 disables the gate.</summary>
    public int MinNavCoveragePercent { get; set; }
    /// <summary>Routes that must resolve to generated HTML output (for example "/", "/404.html", "/api/").</summary>
    public string[] RequiredRoutes { get; set; } = Array.Empty<string>();
    /// <summary>When true, run rendered (Playwright) checks.</summary>
    public bool CheckRendered { get; set; }
    /// <summary>Maximum number of pages to render (0 = all).</summary>
    public int RenderedMaxPages { get; set; } = 20;
    /// <summary>Optional include glob patterns for rendered checks.</summary>
    public string[] RenderedInclude { get; set; } = Array.Empty<string>();
    /// <summary>Optional exclude glob patterns for rendered checks.</summary>
    public string[] RenderedExclude { get; set; } = Array.Empty<string>();
    /// <summary>Browser engine name for rendered checks.</summary>
    public string RenderedEngine { get; set; } = "Chromium";
    /// <summary>When true, auto-install Playwright browsers before rendered checks.</summary>
    public bool RenderedEnsureInstalled { get; set; }
    /// <summary>Base URL for rendered checks (if set, uses HTTP instead of file://).</summary>
    public string? RenderedBaseUrl { get; set; }
    /// <summary>When true, start a local static server for rendered checks if no base URL is provided.</summary>
    public bool RenderedServe { get; set; } = true;
    /// <summary>Host for the local rendered server.</summary>
    public string RenderedServeHost { get; set; } = "localhost";
    /// <summary>Port for the local rendered server (0 = auto).</summary>
    public int RenderedServePort { get; set; }
    /// <summary>Run rendered checks in headless mode.</summary>
    public bool RenderedHeadless { get; set; } = true;
    /// <summary>Rendered check timeout in milliseconds.</summary>
    public int RenderedTimeoutMs { get; set; } = 30000;
    /// <summary>When true, flag console errors during rendered checks.</summary>
    public bool RenderedCheckConsoleErrors { get; set; } = true;
    /// <summary>When true, record console warnings during rendered checks.</summary>
    public bool RenderedCheckConsoleWarnings { get; set; } = true;
    /// <summary>When true, flag failed network requests during rendered checks.</summary>
    public bool RenderedCheckFailedRequests { get; set; } = true;
    /// <summary>Optional path to write audit summary JSON (relative to site root if not rooted).</summary>
    public string? SummaryPath { get; set; }
    /// <summary>Optional path to write SARIF output (relative to site root if not rooted).</summary>
    public string? SarifPath { get; set; }
    /// <summary>Maximum number of issues to include in the summary.</summary>
    public int SummaryMaxIssues { get; set; } = 10;
    /// <summary>Optional path to canonical nav HTML used as the baseline signature.</summary>
    public string? NavCanonicalPath { get; set; }
    /// <summary>CSS selector used to identify nav in the canonical nav file.</summary>
    public string? NavCanonicalSelector { get; set; }
    /// <summary>When true, fail if the canonical nav file is not found or invalid.</summary>
    public bool NavCanonicalRequired { get; set; }
    /// <summary>When true, validate UTF-8 decoding strictly for HTML files.</summary>
    public bool CheckUtf8 { get; set; } = true;
    /// <summary>When true, check for UTF-8 meta charset declaration.</summary>
    public bool CheckMetaCharset { get; set; } = true;
    /// <summary>When true, warn when replacement characters are present in output.</summary>
    public bool CheckUnicodeReplacementChars { get; set; } = true;
    /// <summary>Optional baseline file path for issue key suppression/diffing.</summary>
    public string? BaselinePath { get; set; }
    /// <summary>When true, warnings make audit fail.</summary>
    public bool FailOnWarnings { get; set; }
    /// <summary>When true, newly introduced issues (not present in baseline) make audit fail.</summary>
    public bool FailOnNewIssues { get; set; }
    /// <summary>Maximum allowed errors (-1 disables threshold).</summary>
    public int MaxErrors { get; set; } = -1;
    /// <summary>Maximum allowed warnings (-1 disables threshold).</summary>
    public int MaxWarnings { get; set; } = -1;
    /// <summary>Fail audit when any issue in selected categories is found.</summary>
    public string[] FailOnCategories { get; set; } = Array.Empty<string>();
}

/// <summary>Audits generated HTML output using static checks.</summary>
public static class WebSiteAuditor
{
    private static readonly string[] DefaultHtmlExtensions = { ".html", ".htm" };
    private static readonly string[] IgnoreLinkPrefixes = { "#", "mailto:", "tel:", "javascript:", "data:", "blob:" };
    private static readonly StringComparison FileSystemPathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private const long MaxAuditDataFileSizeBytes = 10 * 1024 * 1024;
    private const int RenderedDetailLimit = 3;
    private static readonly string[] DefaultExcludePatterns =
    {
        "*.scripts.html",
        "**/*.scripts.html",
        "*.head.html",
        "**/*.head.html",
        "**/api-fragments/**",
        "api-fragments/**"
    };

    /// <summary>Runs static audit checks on a generated site output.</summary>
    /// <param name="options">Audit options.</param>
    /// <returns>Audit result.</returns>
    public static WebAuditResult Audit(WebAuditOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.SiteRoot))
            throw new ArgumentException("SiteRoot is required.", nameof(options));

        var siteRoot = Path.GetFullPath(options.SiteRoot);
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");

        var errors = new List<string>();
        var warnings = new List<string>();
        var issues = new List<WebAuditIssue>();
        var htmlFiles = EnumerateHtmlFiles(siteRoot, options.Include, options.Exclude, options.UseDefaultExcludes)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var navIgnorePrefixes = options.NavIgnorePrefixes
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => prefix.Trim().TrimStart('/'))
            .ToArray();
        var failCategories = options.FailOnCategories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var minNavCoveragePercent = Math.Clamp(options.MinNavCoveragePercent, 0, 100);

        void AddIssue(string severity, string category, string? path, string message, string? keyHint = null)
        {
            var normalizedSeverity = string.IsNullOrWhiteSpace(severity)
                ? "warning"
                : severity.Trim().ToLowerInvariant();
            var normalizedCategory = string.IsNullOrWhiteSpace(category)
                ? "general"
                : category.Trim().ToLowerInvariant();
            var issuePath = string.IsNullOrWhiteSpace(path) ? null : path.Replace('\\', '/');
            var issueText = string.IsNullOrWhiteSpace(issuePath)
                ? message
                : $"{issuePath}: {message}";
            var issueKey = BuildIssueKey(normalizedSeverity, normalizedCategory, issuePath, keyHint ?? message);
            issues.Add(new WebAuditIssue
            {
                Severity = normalizedSeverity,
                Category = normalizedCategory,
                Path = issuePath,
                Message = message,
                Key = issueKey
            });

            if (normalizedSeverity == "error")
                errors.Add(issueText);
            else
                warnings.Add(issueText);
        }

        if (htmlFiles.Count == 0)
            AddIssue("warning", "general", null, "No HTML files found to audit.", "no-html");

        var baselineNavSignatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var baselineNavSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pageCount = 0;
        var linkCount = 0;
        var brokenLinkCount = 0;
        var assetCount = 0;
        var missingAssetCount = 0;
        var navMismatchCount = 0;
        var navCheckedCount = 0;
        var navIgnoredCount = 0;
        var duplicateIdCount = 0;
        var requiredRouteCount = 0;
        var missingRequiredRouteCount = 0;
        var renderedPageCount = 0;
        var renderedConsoleErrorCount = 0;
        var renderedConsoleWarningCount = 0;
        var renderedFailedRequestCount = 0;
        var requiredNavLinks = options.NavRequiredLinks
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Select(NormalizeNavHref)
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var navProfiles = NormalizeNavProfiles(options.NavProfiles);
        var canonicalNavLinks = Array.Empty<string>();

        if (options.CheckNavConsistency && !string.IsNullOrWhiteSpace(options.NavCanonicalPath))
        {
            var canonicalPath = ResolveSummaryPath(siteRoot, options.NavCanonicalPath);
            if (!File.Exists(canonicalPath))
            {
                var missingMessage = $"Canonical nav file not found: {ToRelative(siteRoot, canonicalPath)}.";
                if (options.NavCanonicalRequired)
                    AddIssue("error", "nav", null, missingMessage, "nav-canonical-missing");
                else
                    AddIssue("warning", "nav", null, missingMessage, "nav-canonical-missing");
            }
            else
            {
                try
                {
                    var canonicalHtml = File.ReadAllText(canonicalPath);
                    var canonicalDoc = HtmlParser.ParseWithAngleSharp(canonicalHtml);
                    var selector = string.IsNullOrWhiteSpace(options.NavCanonicalSelector)
                        ? options.NavSelector
                        : options.NavCanonicalSelector!;
                    var canonicalNav = canonicalDoc.QuerySelector(selector);
                    if (canonicalNav is null)
                    {
                        var selectorMessage = $"Canonical nav selector '{selector}' was not found in {ToRelative(siteRoot, canonicalPath)}.";
                        if (options.NavCanonicalRequired)
                            AddIssue("error", "nav", null, selectorMessage, "nav-canonical-selector");
                        else
                            AddIssue("warning", "nav", null, selectorMessage, "nav-canonical-selector");
                    }
                    else
                    {
                        var canonicalScope = BuildNavScopeKey(null, selector);
                        baselineNavSignatures[canonicalScope] = BuildNavSignature(canonicalNav);
                        baselineNavSources[canonicalScope] = ToRelative(siteRoot, canonicalPath);
                        canonicalNavLinks = canonicalNav.QuerySelectorAll("a[href]")
                            .Select(anchor => NormalizeNavHref(anchor.GetAttribute("href")))
                            .Where(link => !string.IsNullOrWhiteSpace(link))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                    }
                }
                catch (Exception ex)
                {
                    var parseMessage = $"Canonical nav parse failed ({ex.Message}).";
                    if (options.NavCanonicalRequired)
                        AddIssue("error", "nav", null, parseMessage, "nav-canonical-parse");
                    else
                        AddIssue("warning", "nav", null, parseMessage, "nav-canonical-parse");
                }
            }
        }

        if (canonicalNavLinks.Length > 0)
        {
            requiredNavLinks = requiredNavLinks
                .Concat(canonicalNavLinks)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var requiredRoutes = options.RequiredRoutes
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .Select(route => route.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        requiredRouteCount = requiredRoutes.Length;
        if (requiredRoutes.Length > 0)
        {
            var htmlSet = new HashSet<string>(
                htmlFiles.Select(path => Path.GetRelativePath(siteRoot, path).Replace('\\', '/')),
                StringComparer.OrdinalIgnoreCase);
            foreach (var requiredRoute in requiredRoutes)
            {
                var candidates = ResolveRequiredRouteCandidates(requiredRoute);
                if (candidates.Length == 0)
                    continue;

                var exists = candidates.Any(candidate => htmlSet.Contains(candidate));
                if (exists)
                    continue;

                missingRequiredRouteCount++;
                var expected = string.Join(", ", candidates.Select(path => "/" + path));
                AddIssue("error", "route", null,
                    $"required route '{requiredRoute}' is missing. Expected one of: {expected}.",
                    $"required-route:{requiredRoute}");
            }
        }

        HashSet<string>? baselineIssueKeys = null;
        string? baselinePath = null;
        if (!string.IsNullOrWhiteSpace(options.BaselinePath))
        {
            baselinePath = ResolveSummaryPath(siteRoot, options.BaselinePath);
            baselineIssueKeys = LoadBaselineIssueKeys(baselinePath, AddIssue);
        }

        foreach (var file in htmlFiles)
        {
            pageCount++;
            var relativePath = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
            var html = options.CheckUtf8
                ? ReadFileAsUtf8(file, relativePath, AddIssue)
                : File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(html))
            {
                AddIssue("warning", "html", relativePath, "empty HTML file.", "empty-html");
                continue;
            }

            AngleSharp.Dom.IDocument? doc = null;
            try
            {
                doc = HtmlParser.ParseWithAngleSharp(html);
            }
            catch (Exception ex)
            {
                AddIssue("error", "html", relativePath, $"failed to parse HTML ({ex.Message}).", "parse-html");
                continue;
            }

            if (options.CheckUtf8 && options.CheckUnicodeReplacementChars && html.IndexOf('\uFFFD') >= 0)
                AddIssue("warning", "utf8", relativePath, "contains replacement characters (�).", "replacement-char");

            if (options.CheckUtf8 && options.CheckMetaCharset && !HasUtf8Meta(doc))
                AddIssue("warning", "utf8", relativePath, "missing UTF-8 meta charset declaration.", "meta-charset");

            if (options.CheckHtmlStructure)
            {
                if (doc.DocumentElement is null || !string.Equals(doc.DocumentElement.TagName, "HTML", StringComparison.OrdinalIgnoreCase))
                    AddIssue("warning", "structure", relativePath, "missing <html> root element.", "html-root");
                if (doc.Head is null)
                    AddIssue("warning", "structure", relativePath, "missing <head> section.", "head");
                if (doc.Body is null)
                    AddIssue("warning", "structure", relativePath, "missing <body> section.", "body");
            }

            if (options.CheckNetworkHints)
                ValidateNetworkHints(doc, relativePath, AddIssue);

            if (options.CheckRenderBlockingResources)
            {
                var maxHeadBlockingResources = Math.Max(0, options.MaxHeadBlockingResources);
                ValidateHeadRenderBlocking(doc, relativePath, maxHeadBlockingResources, AddIssue);
            }

            if (options.CheckTitles)
            {
                if (string.IsNullOrWhiteSpace(doc.Title))
                    AddIssue("error", "title", relativePath, "missing <title>.", "title");
            }

            if (options.CheckDuplicateIds)
            {
                var duplicateIds = doc.All
                    .Where(e => e.HasAttribute("id"))
                    .Select(e => e.GetAttribute("id") ?? string.Empty)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToArray();

                if (duplicateIds.Length > 0)
                {
                    duplicateIdCount += duplicateIds.Length;
                    AddIssue("warning", "duplicate-id", relativePath, $"duplicate id(s) detected: {string.Join(", ", duplicateIds)}.", string.Join('|', duplicateIds));
                }
            }

            if (options.CheckHeadingOrder)
                ValidateHeadingOrder(doc, relativePath, AddIssue);

            if (options.CheckLinkPurposeConsistency)
                ValidateLinkPurposeConsistency(doc, relativePath, AddIssue);

            var navProfile = ResolveNavProfile(relativePath, navProfiles);
            var navSelector = !string.IsNullOrWhiteSpace(navProfile?.Selector)
                ? navProfile!.Selector!
                : options.NavSelector;
            var navRequired = navProfile?.Required ?? options.NavRequired;
            var requiredNavLinksForPage = MergeRequiredNavLinks(requiredNavLinks, navProfile);
            var navIgnored = options.IgnoreNavFor.Length > 0 &&
                             MatchesAny(options.IgnoreNavFor, relativePath);
            var prefixIgnored = navIgnorePrefixes.Length > 0 &&
                                navIgnorePrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            var profileIgnored = navProfile?.Ignore ?? false;
            if (options.CheckNavConsistency && (navIgnored || prefixIgnored || profileIgnored))
                navIgnoredCount++;
            if (options.CheckNavConsistency && !navIgnored && !prefixIgnored && !profileIgnored)
            {
                navCheckedCount++;
                var navElement = doc.QuerySelector(navSelector);
                if (navElement is null)
                {
                    if (navRequired)
                        AddIssue("warning", "nav", relativePath, $"nav not found using selector '{navSelector}'.", "nav-missing");
                }
                else
                {
                    var navScope = BuildNavScopeKey(navProfile, navSelector);
                    var signature = BuildNavSignature(navElement);
                    if (!baselineNavSignatures.TryGetValue(navScope, out var baselineNavSignature))
                    {
                        baselineNavSignatures[navScope] = signature;
                        baselineNavSources[navScope] = relativePath;
                    }
                    else if (!string.Equals(baselineNavSignature, signature, StringComparison.Ordinal))
                    {
                        navMismatchCount++;
                        baselineNavSources.TryGetValue(navScope, out var baselineNavSource);
                        var sourceLabel = string.IsNullOrWhiteSpace(baselineNavSource) ? "baseline" : baselineNavSource;
                        AddIssue("warning", "nav", relativePath, $"nav differs from baseline ({sourceLabel}).", "nav-mismatch");
                    }

                    if (requiredNavLinksForPage.Length > 0)
                    {
                        var navLinks = navElement.QuerySelectorAll("a[href]")
                            .Select(a => NormalizeNavHref(a.GetAttribute("href")))
                            .Where(link => !string.IsNullOrWhiteSpace(link))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var missing = requiredNavLinksForPage
                            .Where(required => !navLinks.Contains(required))
                            .ToArray();

                        if (missing.Length > 0)
                            AddIssue("warning", "nav", relativePath, $"nav missing required links: {string.Join(", ", missing)}.", string.Join('|', missing));
                    }
                }
            }

            var baseHref = ResolveBaseHref(doc);
            var baseDir = string.IsNullOrWhiteSpace(baseHref)
                ? Path.GetDirectoryName(file) ?? siteRoot
                : Path.GetFullPath(Path.Combine(siteRoot, baseHref.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

            if (options.CheckLinks)
            {
                foreach (var link in doc.QuerySelectorAll("a[href]"))
                {
                    var href = link.GetAttribute("href") ?? string.Empty;
                    if (ShouldSkipLink(href)) continue;
                    if (IsExternalLink(href)) continue;

                    if (TryResolveLocalTarget(siteRoot, baseDir, href, out var resolvedPath))
                    {
                        linkCount++;
                        if (!File.Exists(resolvedPath))
                        {
                            brokenLinkCount++;
                            AddIssue("error", "link", relativePath, $"broken link '{href}' -> {ToRelative(siteRoot, resolvedPath)}", href);
                        }
                    }
                }
            }

            if (options.CheckAssets)
            {
                foreach (var href in GetAssetHrefs(doc))
                {
                    if (ShouldSkipLink(href)) continue;
                    if (IsExternalLink(href)) continue;
                    if (!TryResolveLocalTarget(siteRoot, baseDir, href, out var resolvedPath)) continue;

                    assetCount++;
                    if (!File.Exists(resolvedPath))
                    {
                        missingAssetCount++;
                        AddIssue("error", "asset", relativePath, $"missing asset '{href}' -> {ToRelative(siteRoot, resolvedPath)}", href);
                    }
                }

                foreach (var srcset in GetAssetSrcSets(doc))
                {
                    var urls = ParseSrcSet(srcset);
                    foreach (var src in urls)
                    {
                        if (ShouldSkipLink(src)) continue;
                        if (IsExternalLink(src)) continue;
                        if (!TryResolveLocalTarget(siteRoot, baseDir, src, out var resolvedPath)) continue;

                        assetCount++;
                        if (!File.Exists(resolvedPath))
                        {
                            missingAssetCount++;
                            AddIssue("error", "asset", relativePath, $"missing asset '{src}' -> {ToRelative(siteRoot, resolvedPath)}", src);
                        }
                    }
                }
            }
        }

        if (options.CheckRendered && htmlFiles.Count > 0)
        {
            var renderedWarnings = new List<string>();
            var engine = ResolveEngine(options.RenderedEngine, renderedWarnings);
            foreach (var warning in renderedWarnings)
                AddIssue("warning", "rendered", null, warning, "rendered-engine");
            if (options.RenderedEnsureInstalled)
            {
                try
                {
                    HtmlBrowser.EnsureInstalledAsync(engine).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    try
                    {
                        HtmlBrowser.RepairInstallationAsync(engine).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // ignore secondary failure, use original message for visibility
                    }
                    AddIssue("warning", "rendered", null, $"Rendered audit install failed: {ex.Message}", "rendered-install");
                }
            }
            var renderedFiles = FilterRenderedFiles(siteRoot, htmlFiles, options.RenderedInclude, options.RenderedExclude);
            var maxPages = options.RenderedMaxPages <= 0
                ? renderedFiles.Count
                : Math.Min(renderedFiles.Count, options.RenderedMaxPages);

            var serveWarnings = new List<string>();
            var (baseUrl, serverCts, serverTask) = EnsureRenderedBaseUrl(siteRoot, options, serveWarnings);
            foreach (var warning in serveWarnings)
                AddIssue("warning", "rendered", null, warning, "rendered-serve");
            foreach (var file in renderedFiles.Take(maxPages))
            {
                var relativePath = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
                try
                {
                    var renderedResult = string.IsNullOrWhiteSpace(baseUrl)
                        ? HtmlBrowserTester.TestFileAsync(
                            file,
                            engine,
                            options.RenderedHeadless,
                            options.RenderedTimeoutMs).GetAwaiter().GetResult()
                        : HtmlBrowserTester.TestUrlAsync(
                            CombineUrl(baseUrl, ToRoutePath(relativePath)),
                            engine,
                            options.RenderedHeadless,
                            options.RenderedTimeoutMs).GetAwaiter().GetResult();

                    if (IsPlaywrightMissing(renderedResult.ConsoleErrors, out var missingMessage))
                    {
                        AddIssue("warning", "rendered", relativePath, string.IsNullOrWhiteSpace(missingMessage)
                            ? "Rendered audit skipped: Playwright browsers not installed."
                            : $"Rendered audit skipped: Playwright browsers not installed. {missingMessage}",
                            "rendered-playwright");
                        break;
                    }

                    renderedPageCount++;

                    if (options.RenderedCheckConsoleErrors && renderedResult.ErrorCount > 0)
                    {
                        renderedConsoleErrorCount += renderedResult.ErrorCount;
                        var detail = BuildConsoleSummary(renderedResult.ConsoleErrors, RenderedDetailLimit);
                        AddIssue("error", "rendered-console-error", relativePath, string.IsNullOrWhiteSpace(detail)
                            ? $"console errors ({renderedResult.ErrorCount})."
                            : $"console errors ({renderedResult.ErrorCount}) -> {detail}",
                            "rendered-console-errors");
                    }

                    if (options.RenderedCheckConsoleWarnings && renderedResult.WarningCount > 0)
                    {
                        renderedConsoleWarningCount += renderedResult.WarningCount;
                        var detail = BuildConsoleSummary(renderedResult.ConsoleWarnings, RenderedDetailLimit);
                        AddIssue("warning", "rendered-console-warning", relativePath, string.IsNullOrWhiteSpace(detail)
                            ? $"console warnings ({renderedResult.WarningCount})."
                            : $"console warnings ({renderedResult.WarningCount}) -> {detail}",
                            "rendered-console-warnings");
                    }

                    if (options.RenderedCheckFailedRequests && renderedResult.FailedRequestCount > 0)
                    {
                        renderedFailedRequestCount += renderedResult.FailedRequestCount;
                        var detail = BuildFailedRequestSummary(renderedResult.FailedRequests, RenderedDetailLimit);
                        AddIssue("error", "rendered-request-failure", relativePath, string.IsNullOrWhiteSpace(detail)
                            ? $"failed network requests ({renderedResult.FailedRequestCount})."
                            : $"failed network requests ({renderedResult.FailedRequestCount}) -> {detail}",
                            "rendered-failed-requests");
                    }
                }
                catch (Exception ex)
                {
                    AddIssue("error", "rendered", relativePath, $"rendered audit failed ({ex.Message}).", "rendered-failed");
                }
            }

            if (serverCts is not null)
            {
                serverCts.Cancel();
                if (serverTask is not null)
                {
                    try { serverTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
                }
            }
        }

        if (baselineIssueKeys is not null)
        {
            foreach (var issue in issues)
                issue.IsNew = !baselineIssueKeys.Contains(issue.Key);
        }

        var navTotalCount = navCheckedCount + navIgnoredCount;
        var navCoveragePercent = navTotalCount == 0
            ? 100d
            : (double)navCheckedCount * 100d / navTotalCount;

        var preGateErrorCount = errors.Count;
        var preGateWarningCount = warnings.Count;
        var preGateNewIssueCount = issues.Count(issue => issue.IsNew);

        if (failCategories.Count > 0)
        {
            var categoryHits = issues.Count(issue => failCategories.Contains(issue.Category));
            if (categoryHits > 0)
            {
                AddIssue("error", "gate", null,
                    $"Audit gate failed: {categoryHits} issue(s) match fail categories [{string.Join(", ", failCategories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))}].",
                    "gate-category");
            }
        }

        if (options.MaxErrors >= 0 && preGateErrorCount > options.MaxErrors)
        {
            AddIssue("error", "gate", null,
                $"Audit gate failed: errors {preGateErrorCount} exceed max-errors {options.MaxErrors}.",
                "gate-max-errors");
        }

        if (options.MaxWarnings >= 0 && preGateWarningCount > options.MaxWarnings)
        {
            AddIssue("error", "gate", null,
                $"Audit gate failed: warnings {preGateWarningCount} exceed max-warnings {options.MaxWarnings}.",
                "gate-max-warnings");
        }

        if (options.CheckNavConsistency && minNavCoveragePercent > 0 && navTotalCount > 0 &&
            navCoveragePercent < minNavCoveragePercent)
        {
            AddIssue("error", "gate", null,
                $"Audit gate failed: nav coverage {navCoveragePercent:0.0}% is below min-nav-coverage {minNavCoveragePercent}%.",
                "gate-nav-coverage");
        }

        if (options.FailOnWarnings && preGateWarningCount > 0)
        {
            AddIssue("error", "gate", null,
                $"Audit gate failed: warnings present ({preGateWarningCount}) and fail-on-warnings is enabled.",
                "gate-fail-warnings");
        }

        if (options.FailOnNewIssues && preGateNewIssueCount > 0)
        {
            AddIssue("error", "gate", null,
                $"Audit gate failed: new issues present ({preGateNewIssueCount}) and fail-on-new is enabled.",
                "gate-fail-new");
        }

        if (baselineIssueKeys is not null)
        {
            foreach (var issue in issues)
                issue.IsNew = !baselineIssueKeys.Contains(issue.Key);
        }

        var errorCount = errors.Count;
        var warningCount = warnings.Count;
        var newIssueCount = issues.Count(issue => issue.IsNew);
        var newErrorCount = issues.Count(issue => issue.IsNew && issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        var newWarningCount = issues.Count(issue => issue.IsNew && issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase));

        var result = new WebAuditResult
        {
            Success = errorCount == 0,
            PageCount = pageCount,
            LinkCount = linkCount,
            BrokenLinkCount = brokenLinkCount,
            AssetCount = assetCount,
            MissingAssetCount = missingAssetCount,
            NavMismatchCount = navMismatchCount,
            NavCheckedCount = navCheckedCount,
            NavIgnoredCount = navIgnoredCount,
            NavTotalCount = navTotalCount,
            NavCoveragePercent = navCoveragePercent,
            DuplicateIdCount = duplicateIdCount,
            RequiredRouteCount = requiredRouteCount,
            MissingRequiredRouteCount = missingRequiredRouteCount,
            RenderedPageCount = renderedPageCount,
            RenderedConsoleErrorCount = renderedConsoleErrorCount,
            RenderedConsoleWarningCount = renderedConsoleWarningCount,
            RenderedFailedRequestCount = renderedFailedRequestCount,
            ErrorCount = errorCount,
            WarningCount = warningCount,
            NewIssueCount = newIssueCount,
            NewErrorCount = newErrorCount,
            NewWarningCount = newWarningCount,
            BaselinePath = baselinePath,
            BaselineIssueCount = baselineIssueKeys?.Count ?? 0,
            Issues = issues.ToArray(),
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };

        if (!string.IsNullOrWhiteSpace(options.SummaryPath))
        {
            var summaryPath = ResolveSummaryPath(siteRoot, options.SummaryPath);
            var summary = new WebAuditSummary
            {
                Success = result.Success,
                PageCount = result.PageCount,
                LinkCount = result.LinkCount,
                BrokenLinkCount = result.BrokenLinkCount,
                AssetCount = result.AssetCount,
                MissingAssetCount = result.MissingAssetCount,
                NavMismatchCount = result.NavMismatchCount,
                NavCheckedCount = result.NavCheckedCount,
                NavIgnoredCount = result.NavIgnoredCount,
                NavTotalCount = result.NavTotalCount,
                NavCoveragePercent = result.NavCoveragePercent,
                DuplicateIdCount = result.DuplicateIdCount,
                RequiredRouteCount = result.RequiredRouteCount,
                MissingRequiredRouteCount = result.MissingRequiredRouteCount,
                RenderedPageCount = result.RenderedPageCount,
                RenderedConsoleErrorCount = result.RenderedConsoleErrorCount,
                RenderedConsoleWarningCount = result.RenderedConsoleWarningCount,
                RenderedFailedRequestCount = result.RenderedFailedRequestCount,
                ErrorCount = result.ErrorCount,
                WarningCount = result.WarningCount,
                NewIssueCount = result.NewIssueCount,
                NewErrorCount = result.NewErrorCount,
                NewWarningCount = result.NewWarningCount,
                BaselinePath = result.BaselinePath,
                BaselineIssueCount = result.BaselineIssueCount,
                Issues = TakeIssues(result.Issues, options.SummaryMaxIssues),
                Errors = TakeIssues(result.Errors, options.SummaryMaxIssues),
                Warnings = TakeIssues(result.Warnings, options.SummaryMaxIssues)
            };
            WriteSummary(summaryPath, summary, warnings);
            result.SummaryPath = summaryPath;
        }

        if (!string.IsNullOrWhiteSpace(options.SarifPath))
        {
            var sarifPath = ResolveSummaryPath(siteRoot, options.SarifPath);
            WriteSarif(sarifPath, result.Issues, warnings);
            result.SarifPath = sarifPath;
        }

        return result;
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
        return patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Replace('\\', '/').Trim())
            .ToArray();
    }

    private static string[] BuildExcludePatterns(string[] patterns, bool useDefaults)
    {
        var list = NormalizePatterns(patterns).ToList();
        if (useDefaults)
        {
            list.AddRange(DefaultExcludePatterns);
        }
        return list
            .Where(p => !string.IsNullOrWhiteSpace(p))
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
            .Replace("\\*", "[^/]*") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
    }

    private static bool ShouldSkipLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return true;
        foreach (var prefix in IgnoreLinkPrefixes)
        {
            if (href.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsExternalLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;
        if (href.StartsWith("//")) return true;
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
        if (!candidate.StartsWith(siteRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        if (isExplicitDir)
        {
            var dir = candidate.TrimEnd(Path.DirectorySeparatorChar);
            var indexPath = Path.Combine(dir, "index.html");
            if (File.Exists(indexPath))
            {
                resolvedPath = indexPath;
                return true;
            }

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

        var indexCandidate = Path.Combine(candidate, "index.html");
        resolvedPath = indexCandidate;
        return true;
    }

    private static string? ResolveBaseHref(AngleSharp.Dom.IDocument doc)
    {
        var baseElement = doc.QuerySelector("base[href]");
        if (baseElement is null) return null;
        var href = baseElement.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (IsExternalLink(href)) return null;
        if (!href.StartsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return href.TrimEnd('/');
    }

    private static string BuildNavSignature(AngleSharp.Dom.IElement nav)
    {
        var tokens = new List<string>();
        foreach (var link in nav.QuerySelectorAll("a"))
        {
            var href = link.GetAttribute("href") ?? string.Empty;
            var text = link.TextContent?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                text = link.GetAttribute("aria-label") ?? link.GetAttribute("title") ?? string.Empty;
            tokens.Add($"{href.Trim()}|{text.Trim()}");
        }
        return string.Join("||", tokens);
    }

    private static WebAuditNavProfile[] NormalizeNavProfiles(WebAuditNavProfile[]? profiles)
    {
        if (profiles is null || profiles.Length == 0)
            return Array.Empty<WebAuditNavProfile>();

        return profiles
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Match))
            .Select(profile => new WebAuditNavProfile
            {
                Match = profile.Match.Replace('\\', '/').Trim(),
                Selector = string.IsNullOrWhiteSpace(profile.Selector) ? null : profile.Selector.Trim(),
                Required = profile.Required,
                RequiredLinks = (profile.RequiredLinks ?? Array.Empty<string>())
                    .Where(link => !string.IsNullOrWhiteSpace(link))
                    .Select(NormalizeNavHref)
                    .Where(link => !string.IsNullOrWhiteSpace(link))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Ignore = profile.Ignore
            })
            .OrderByDescending(profile => profile.Match.Length)
            .ToArray();
    }

    private static WebAuditNavProfile? ResolveNavProfile(string relativePath, WebAuditNavProfile[] profiles)
    {
        if (profiles.Length == 0 || string.IsNullOrWhiteSpace(relativePath))
            return null;

        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var profile in profiles)
        {
            if (GlobMatch(profile.Match, normalizedPath))
                return profile;
        }

        return null;
    }

    private static string[] MergeRequiredNavLinks(string[] defaultRequiredLinks, WebAuditNavProfile? profile)
    {
        if (profile is null || profile.RequiredLinks.Length == 0)
            return defaultRequiredLinks;

        return defaultRequiredLinks
            .Concat(profile.RequiredLinks)
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildNavScopeKey(WebAuditNavProfile? profile, string selector)
    {
        var profileMatch = profile is null || string.IsNullOrWhiteSpace(profile.Match)
            ? "__default__"
            : profile.Match;
        var navSelector = string.IsNullOrWhiteSpace(selector)
            ? "nav"
            : selector.Trim();
        return profileMatch + "|" + navSelector;
    }

    private static void ValidateHeadingOrder(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        var headings = doc.QuerySelectorAll("h1,h2,h3,h4,h5,h6")
            .Select(heading => new
            {
                Element = heading,
                Level = ParseHeadingLevel(heading.TagName)
            })
            .Where(entry => entry.Level > 0 && !IsElementHidden(entry.Element))
            .ToList();

        if (headings.Count < 2)
            return;

        var previousLevel = headings[0].Level;
        for (var index = 1; index < headings.Count; index++)
        {
            var current = headings[index];
            if (current.Level <= previousLevel + 1)
            {
                previousLevel = current.Level;
                continue;
            }

            var text = NormalizeHeadingText(current.Element.TextContent);
            var label = string.IsNullOrWhiteSpace(text)
                ? $"h{current.Level}"
                : $"h{current.Level} \"{text}\"";
            addIssue("warning", "heading-order", relativePath,
                $"heading order skips levels (h{previousLevel} -> h{current.Level}) near {label}.",
                $"heading-order:{index}:{previousLevel}:{current.Level}");
            previousLevel = current.Level;
        }
    }

    private static void ValidateLinkPurposeConsistency(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        var labelTargets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var labelDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in doc.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || ShouldSkipLink(href))
                continue;

            var label = GetAccessibleLinkLabel(anchor);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            var destination = NormalizeLinkPurposeDestination(href);
            if (string.IsNullOrWhiteSpace(destination))
                continue;

            var normalizedLabel = label.Trim();
            if (!labelTargets.TryGetValue(normalizedLabel, out var targets))
            {
                targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                labelTargets[normalizedLabel] = targets;
                labelDisplay[normalizedLabel] = normalizedLabel;
            }
            targets.Add(destination);
        }

        foreach (var pair in labelTargets)
        {
            if (pair.Value.Count <= 1)
                continue;

            var targets = pair.Value
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var label = labelDisplay.TryGetValue(pair.Key, out var value)
                ? value
                : pair.Key;

            addIssue("warning", "link-purpose", relativePath,
                $"link label '{label}' points to multiple destinations: {string.Join(", ", targets)}.",
                $"link-purpose:{label}:{string.Join("|", targets)}");
        }
    }

    private static int ParseHeadingLevel(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName) || tagName.Length != 2)
            return 0;
        if (tagName[0] != 'H' && tagName[0] != 'h')
            return 0;
        return tagName[1] switch
        {
            '1' => 1,
            '2' => 2,
            '3' => 3,
            '4' => 4,
            '5' => 5,
            '6' => 6,
            _ => 0
        };
    }

    private static bool IsElementHidden(AngleSharp.Dom.IElement element)
    {
        if (element.HasAttribute("hidden"))
            return true;

        var ariaHidden = element.GetAttribute("aria-hidden");
        return !string.IsNullOrWhiteSpace(ariaHidden) &&
               ariaHidden.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHeadingText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static string GetAccessibleLinkLabel(AngleSharp.Dom.IElement anchor)
    {
        var ariaLabel = anchor.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
            return Regex.Replace(ariaLabel, "\\s+", " ").Trim();

        var text = anchor.TextContent;
        if (!string.IsNullOrWhiteSpace(text))
            return Regex.Replace(text, "\\s+", " ").Trim();

        var title = anchor.GetAttribute("title");
        if (!string.IsNullOrWhiteSpace(title))
            return Regex.Replace(title, "\\s+", " ").Trim();

        return string.Empty;
    }

    private static string NormalizeLinkPurposeDestination(string href)
    {
        var normalized = StripQueryAndFragment(href).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (normalized.StartsWith("//", StringComparison.Ordinal))
            normalized = "https:" + normalized;

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            var path = string.IsNullOrWhiteSpace(absolute.AbsolutePath) ? "/" : absolute.AbsolutePath;
            if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
                path = path.TrimEnd('/');
            return $"{absolute.Scheme}://{absolute.Host}{(absolute.IsDefaultPort ? string.Empty : ":" + absolute.Port)}{path}";
        }

        return NormalizeNavHref(normalized);
    }

    private static void ValidateNetworkHints(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        var head = doc.Head;
        if (head is null)
            return;

        var requiredOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in head.QuerySelectorAll("link[href]"))
        {
            var rel = (link.GetAttribute("rel") ?? string.Empty).Trim();
            if (!ContainsRelToken(rel, "stylesheet"))
                continue;

            var href = link.GetAttribute("href");
            if (TryGetExternalOrigin(href, out var origin))
                requiredOrigins.Add(origin);
        }

        foreach (var script in head.QuerySelectorAll("script[src]"))
        {
            var src = script.GetAttribute("src");
            if (TryGetExternalOrigin(src, out var origin))
                requiredOrigins.Add(origin);
        }

        var externalImageOriginCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var img in doc.QuerySelectorAll("img[src]"))
        {
            var src = img.GetAttribute("src");
            if (!TryGetExternalOrigin(src, out var origin))
                continue;
            externalImageOriginCounts.TryGetValue(origin, out var count);
            externalImageOriginCounts[origin] = count + 1;
        }
        foreach (var srcset in doc.QuerySelectorAll("img[srcset],source[srcset]")
                     .Select(element => element.GetAttribute("srcset"))
                     .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var candidate in ParseSrcSet(srcset!))
            {
                if (!TryGetExternalOrigin(candidate, out var origin))
                    continue;
                externalImageOriginCounts.TryGetValue(origin, out var count);
                externalImageOriginCounts[origin] = count + 1;
            }
        }
        foreach (var pair in externalImageOriginCounts)
        {
            if (pair.Value >= 2 || pair.Key.Contains("img.shields.io", StringComparison.OrdinalIgnoreCase))
                requiredOrigins.Add(pair.Key);
        }

        if (requiredOrigins.Contains("https://fonts.googleapis.com"))
            requiredOrigins.Add("https://fonts.gstatic.com");

        if (requiredOrigins.Count == 0)
            return;

        var hintedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in head.QuerySelectorAll("link[rel][href]"))
        {
            var rel = (link.GetAttribute("rel") ?? string.Empty).Trim();
            if (!ContainsRelToken(rel, "preconnect") && !ContainsRelToken(rel, "dns-prefetch"))
                continue;

            var href = link.GetAttribute("href");
            if (TryGetExternalOrigin(href, out var origin))
                hintedOrigins.Add(origin);
        }

        var missing = requiredOrigins
            .Where(origin => !hintedOrigins.Contains(origin))
            .OrderBy(origin => origin, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missing.Length == 0)
            return;

        addIssue("warning", "network-hint", relativePath,
            $"missing preconnect/dns-prefetch for external origins: {string.Join(", ", missing)}.",
            "network-hints");
    }

    private static void ValidateHeadRenderBlocking(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        int maxHeadBlockingResources,
        Action<string, string, string?, string, string?> addIssue)
    {
        if (maxHeadBlockingResources <= 0)
            return;

        var head = doc.Head;
        if (head is null)
            return;

        var blockingStyles = head.QuerySelectorAll("link[rel][href]")
            .Count(link =>
            {
                var rel = (link.GetAttribute("rel") ?? string.Empty).Trim();
                if (!ContainsRelToken(rel, "stylesheet"))
                    return false;

                var media = (link.GetAttribute("media") ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(media) ||
                       media.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                       media.Equals("screen", StringComparison.OrdinalIgnoreCase);
            });

        var blockingScripts = head.QuerySelectorAll("script[src]")
            .Count(script =>
            {
                var hasAsync = script.HasAttribute("async");
                var hasDefer = script.HasAttribute("defer");
                var type = (script.GetAttribute("type") ?? string.Empty).Trim();
                var isModule = type.Equals("module", StringComparison.OrdinalIgnoreCase);
                return !hasAsync && !hasDefer && !isModule;
            });

        var totalBlocking = blockingStyles + blockingScripts;
        if (totalBlocking <= maxHeadBlockingResources)
            return;

        addIssue("warning", "render-blocking", relativePath,
            $"head includes {totalBlocking} render-blocking resources (styles {blockingStyles}, scripts {blockingScripts}); max is {maxHeadBlockingResources}.",
            "head-render-blocking");
    }

    private static bool ContainsRelToken(string relValue, string token)
    {
        if (string.IsNullOrWhiteSpace(relValue) || string.IsNullOrWhiteSpace(token))
            return false;

        var parts = relValue.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Equals(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryGetExternalOrigin(string? value, out string origin)
    {
        origin = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (candidate.StartsWith("//", StringComparison.Ordinal))
            candidate = "https:" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;
        if (string.IsNullOrWhiteSpace(uri.Host))
            return false;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return !string.IsNullOrWhiteSpace(origin);
    }

    private static string NormalizeNavHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return string.Empty;
        var normalized = StripQueryAndFragment(href).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        if (IsExternalLink(normalized))
            return normalized;

        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;

        if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            normalized = normalized.TrimEnd('/');

        return normalized;
    }

    private static string[] ResolveRequiredRouteCandidates(string route)
    {
        var normalized = StripQueryAndFragment(route).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();
        if (IsExternalLink(normalized))
            return Array.Empty<string>();

        normalized = normalized.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = normalized.TrimStart('/');

        if (string.IsNullOrWhiteSpace(normalized))
            return new[] { "index.html" };

        var candidates = new List<string>();
        if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            var basePath = normalized.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(basePath))
            {
                candidates.Add("index.html");
            }
            else
            {
                candidates.Add(basePath + "/index.html");
                candidates.Add(basePath + ".html");
            }
        }
        else if (normalized.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(normalized);
        }
        else if (Path.HasExtension(normalized))
        {
            candidates.Add(normalized);
        }
        else
        {
            candidates.Add(normalized + "/index.html");
            candidates.Add(normalized + ".html");
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate.TrimStart('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> GetAssetHrefs(AngleSharp.Dom.IDocument doc)
    {
        foreach (var link in doc.QuerySelectorAll("link[href]"))
        {
            var rel = (link.GetAttribute("rel") ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(rel))
                continue;

            if (rel.Contains("stylesheet") || rel.Contains("icon") || rel.Contains("manifest") || rel.Contains("preload"))
            {
                var href = link.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href))
                    yield return href;
            }
        }

        foreach (var script in doc.QuerySelectorAll("script[src]"))
        {
            var src = script.GetAttribute("src");
            if (!string.IsNullOrWhiteSpace(src))
                yield return src;
        }

        foreach (var img in doc.QuerySelectorAll("img[src]"))
        {
            var src = img.GetAttribute("src");
            if (!string.IsNullOrWhiteSpace(src))
                yield return src;
        }

        foreach (var source in doc.QuerySelectorAll("source[src]"))
        {
            var src = source.GetAttribute("src");
            if (!string.IsNullOrWhiteSpace(src))
                yield return src;
        }
    }

    private static IEnumerable<string> GetAssetSrcSets(AngleSharp.Dom.IDocument doc)
    {
        foreach (var img in doc.QuerySelectorAll("img[srcset]"))
        {
            var srcset = img.GetAttribute("srcset");
            if (!string.IsNullOrWhiteSpace(srcset))
                yield return srcset;
        }

        foreach (var source in doc.QuerySelectorAll("source[srcset]"))
        {
            var srcset = source.GetAttribute("srcset");
            if (!string.IsNullOrWhiteSpace(srcset))
                yield return srcset;
        }
    }

    private static IEnumerable<string> ParseSrcSet(string srcset)
    {
        if (string.IsNullOrWhiteSpace(srcset))
            yield break;

        var parts = srcset.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx > 0)
                trimmed = trimmed.Substring(0, spaceIdx);
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }

    private static string ToRelative(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return fullPath;
        return Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
    }

    private static (string? BaseUrl, CancellationTokenSource? Cts, Task? ServerTask) EnsureRenderedBaseUrl(
        string siteRoot,
        WebAuditOptions options,
        IList<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(options.RenderedBaseUrl))
            return (options.RenderedBaseUrl.TrimEnd('/'), null, null);

        if (!options.RenderedServe)
            return (null, null, null);

        var host = string.IsNullOrWhiteSpace(options.RenderedServeHost) ? "localhost" : options.RenderedServeHost;
        var port = options.RenderedServePort;
        if (port <= 0)
        {
            port = GetFreePort();
        }

        var cts = new CancellationTokenSource();
        var task = Task.Run(() => WebStaticServer.Serve(siteRoot, host, port, cts.Token), cts.Token);
        var baseUrl = $"http://{host}:{port}";
        return (baseUrl, cts, task);
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        var trimmedPath = path.StartsWith("/") ? path : "/" + path;
        return trimmedBase + trimmedPath;
    }

    private static string ToRoutePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "/";

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            var withoutIndex = normalized.Substring(0, normalized.Length - "index.html".Length);
            if (string.IsNullOrWhiteSpace(withoutIndex))
                return "/";
            return "/" + withoutIndex.TrimEnd('/') + "/";
        }

        return "/" + normalized;
    }

    private static List<string> FilterRenderedFiles(string siteRoot, List<string> htmlFiles, string[] includePatterns, string[] excludePatterns)
    {
        var includes = NormalizePatterns(includePatterns);
        var excludes = NormalizePatterns(excludePatterns);
        if (includes.Length == 0 && excludes.Length == 0)
            return htmlFiles;

        var list = new List<string>();
        foreach (var file in htmlFiles)
        {
            var relative = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
            if (excludes.Length > 0 && MatchesAny(excludes, relative))
                continue;
            if (includes.Length > 0 && !MatchesAny(includes, relative))
                continue;
            list.Add(file);
        }
        return list;
    }

    private static string ResolveSummaryPath(string siteRoot, string summaryPath)
    {
        var normalizedRoot = NormalizeRootPath(siteRoot);
        var trimmed = summaryPath.Trim();
        var full = Path.IsPathRooted(trimmed)
            ? trimmed
            : Path.Combine(siteRoot, trimmed);
        var resolved = Path.GetFullPath(full);
        if (!IsPathWithinRoot(normalizedRoot, resolved))
            throw new InvalidOperationException($"Path must resolve under site root: {summaryPath}");
        return resolved;
    }

    private static string NormalizeRootPath(string siteRoot)
    {
        var full = Path.GetFullPath(siteRoot);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsPathWithinRoot(string normalizedRoot, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(normalizedRoot, FileSystemPathComparison);
    }

    private static string BuildIssueKey(string severity, string category, string? path, string hint)
    {
        static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var trimmed = value.Trim().ToLowerInvariant();
            var chars = trimmed.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
            var normalized = new string(chars);
            while (normalized.Contains("--", StringComparison.Ordinal))
                normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
            return normalized.Trim('-');
        }

        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Join("|", new[]
        {
            Normalize(severity),
            Normalize(category),
            Normalize(normalizedPath),
            Normalize(hint)
        });
    }

    private static HashSet<string> LoadBaselineIssueKeys(
        string baselinePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(baselinePath))
        {
            addIssue("warning", "baseline", null, $"Baseline file not found: {baselinePath}.", "baseline-missing");
            return keys;
        }

        var info = new FileInfo(baselinePath);
        if (info.Length > MaxAuditDataFileSizeBytes)
        {
            addIssue("warning", "baseline", null, $"Baseline file is too large ({info.Length} bytes).", "baseline-too-large");
            return keys;
        }

        try
        {
            using var stream = File.OpenRead(baselinePath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (TryGetPropertyIgnoreCase(root, "issueKeys", out var issueKeys) && issueKeys.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in issueKeys.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        keys.Add(value);
                }
            }

            if (TryGetPropertyIgnoreCase(root, "issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    if (issue.ValueKind != JsonValueKind.Object) continue;
                    if (!TryGetPropertyIgnoreCase(issue, "key", out var keyElement) || keyElement.ValueKind != JsonValueKind.String) continue;
                    var value = keyElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        keys.Add(value);
                }
            }

            if (keys.Count == 0)
                addIssue("warning", "baseline", null, $"Baseline file does not contain issue keys: {baselinePath}.", "baseline-empty");
        }
        catch (Exception ex)
        {
            addIssue("warning", "baseline", null, $"Baseline file parse failed ({ex.Message}).", "baseline-parse");
        }

        return keys;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string ReadFileAsUtf8(
        string filePath,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        var bytes = File.ReadAllBytes(filePath);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            var offset = ex.Index >= 0 ? $" at byte offset {ex.Index}" : string.Empty;
            addIssue("error", "utf8", relativePath, $"invalid UTF-8 byte sequence{offset} ({ex.Message}).", "utf8-invalid");
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static bool HasUtf8Meta(AngleSharp.Dom.IDocument doc)
    {
        foreach (var meta in doc.QuerySelectorAll("meta"))
        {
            var charset = meta.GetAttribute("charset");
            if (!string.IsNullOrWhiteSpace(charset) &&
                charset.Trim().Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                return true;

            var httpEquiv = meta.GetAttribute("http-equiv");
            var content = meta.GetAttribute("content");
            if (!string.IsNullOrWhiteSpace(httpEquiv) &&
                httpEquiv.Equals("content-type", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(content) &&
                content.IndexOf("charset=utf-8", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string[] TakeIssues(string[] issues, int max)
    {
        if (issues.Length == 0) return Array.Empty<string>();
        if (max <= 0) return Array.Empty<string>();
        return issues.Take(max).ToArray();
    }

    private static WebAuditIssue[] TakeIssues(WebAuditIssue[] issues, int max)
    {
        if (issues.Length == 0) return Array.Empty<WebAuditIssue>();
        if (max <= 0) return Array.Empty<WebAuditIssue>();
        return issues.Take(max).ToArray();
    }

    private static void WriteSummary(string summaryPath, WebAuditSummary summary, IList<string> warnings)
    {
        try
        {
            var dir = Path.GetDirectoryName(summaryPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(summary, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(summaryPath, json);
        }
        catch (Exception ex)
        {
            warnings.Add($"Audit summary write failed: {ex.Message}");
        }
    }

    private static void WriteSarif(string sarifPath, IReadOnlyList<WebAuditIssue> issues, IList<string> warnings)
    {
        try
        {
            var dir = Path.GetDirectoryName(sarifPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var rules = issues
                .Where(issue => !string.IsNullOrWhiteSpace(issue.Category))
                .Select(issue => issue.Category.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(category => new
                {
                    id = "powerforge.web/" + category,
                    shortDescription = new { text = "PowerForge.Web audit category: " + category }
                })
                .ToArray();

            var results = issues.Select(issue =>
            {
                var ruleId = "powerforge.web/" +
                             (string.IsNullOrWhiteSpace(issue.Category) ? "general" : issue.Category.Trim().ToLowerInvariant());
                var location = string.IsNullOrWhiteSpace(issue.Path)
                    ? null
                    : new[]
                    {
                        new
                        {
                            physicalLocation = new
                            {
                                artifactLocation = new { uri = issue.Path.Replace('\\', '/') }
                            }
                        }
                    };

                return new
                {
                    ruleId,
                    level = MapSarifLevel(issue.Severity),
                    message = new { text = issue.Message },
                    locations = location,
                    properties = new
                    {
                        key = issue.Key,
                        category = issue.Category,
                        severity = issue.Severity,
                        isNew = issue.IsNew
                    }
                };
            }).ToArray();

            var sarif = new Dictionary<string, object?>
            {
                ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
                ["version"] = "2.1.0",
                ["runs"] = new[]
                {
                    new
                    {
                        tool = new
                        {
                            driver = new
                            {
                                name = "PowerForge.Web",
                                fullName = "PowerForge.Web Static Site Audit",
                                version = typeof(WebSiteAuditor).Assembly.GetName().Version?.ToString() ?? "unknown",
                                rules
                            }
                        },
                        results
                    }
                }
            };

            var json = JsonSerializer.Serialize(sarif, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sarifPath, json);
        }
        catch (Exception ex)
        {
            warnings.Add($"Audit SARIF write failed: {ex.Message}");
        }
    }

    private static string MapSarifLevel(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
            return "warning";

        if (severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            return "error";
        if (severity.Equals("info", StringComparison.OrdinalIgnoreCase))
            return "note";

        return "warning";
    }

    private static HtmlBrowserEngine ResolveEngine(string? value, IList<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
            return HtmlBrowserEngine.Chromium;

        if (Enum.TryParse<HtmlBrowserEngine>(value, true, out var engine))
            return engine;

        warnings.Add($"Rendered engine '{value}' not recognized; using Chromium.");
        return HtmlBrowserEngine.Chromium;
    }

    private static string BuildConsoleSummary(IEnumerable<object>? entries, int max)
    {
        return BuildRenderedSummary(entries, max, entry =>
        {
            var text = GetEntryString(entry, "Text") ?? entry?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var location = GetEntryString(entry, "FullLocation") ?? GetEntryString(entry, "Location");
            return string.IsNullOrWhiteSpace(location) ? text : $"{text} ({location})";
        });
    }

    private static string BuildFailedRequestSummary(IEnumerable<object>? entries, int max)
    {
        return BuildRenderedSummary(entries, max, entry =>
        {
            var url = GetEntryString(entry, "Url");
            if (string.IsNullOrWhiteSpace(url))
                url = entry?.ToString();

            var method = GetEntryString(entry, "Method");
            var status = GetEntryString(entry, "Status");
            var error = GetEntryString(entry, "ErrorMessage") ?? GetEntryString(entry, "ErrorType");

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(method))
                parts.Add(method);
            if (!string.IsNullOrWhiteSpace(url))
                parts.Add(url);
            if (!string.IsNullOrWhiteSpace(status))
                parts.Add($"status {status}");
            if (!string.IsNullOrWhiteSpace(error))
                parts.Add(error);

            return parts.Count == 0 ? null : string.Join(" ", parts);
        });
    }

    private static string BuildRenderedSummary(IEnumerable<object>? entries, int max, Func<object?, string?> formatter)
    {
        if (entries is null || max <= 0)
            return string.Empty;

        var list = new List<string>();
        foreach (var entry in entries)
        {
            if (entry is null) continue;
            var formatted = formatter(entry);
            if (string.IsNullOrWhiteSpace(formatted))
                continue;
            list.Add(formatted.Trim());
            if (list.Count >= max)
                break;
        }

        return list.Count == 0 ? string.Empty : string.Join(" | ", list);
    }

    private static string? GetEntryString(object? entry, string property)
    {
        if (entry is null) return null;
        var prop = entry.GetType().GetProperty(property);
        if (prop is null) return null;
        var value = prop.GetValue(entry);
        return value?.ToString();
    }


    private static bool IsPlaywrightMissing(IEnumerable<object>? entries, out string? message)
    {
        message = null;
        if (entries is null) return false;

        foreach (var entry in entries)
        {
            var text = GetEntryString(entry, "Text");
            if (string.IsNullOrWhiteSpace(text))
                continue;
            if (text.IndexOf("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (text.IndexOf("playwright", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 text.IndexOf("install", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                message = text.Trim();
                return true;
            }
        }

        return false;
    }
}
