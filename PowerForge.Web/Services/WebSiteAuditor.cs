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
    /// <summary>
    /// Maximum number of HTML files to audit (0 disables).
    /// When set, files are selected in alphabetical order by path.
    /// </summary>
    public int MaxHtmlFiles { get; set; }
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
    /// <summary>When true, write the summary file only when audit fails.</summary>
    public bool SummaryOnFailOnly { get; set; }
    /// <summary>Optional path to write SARIF output (relative to site root if not rooted).</summary>
    public string? SarifPath { get; set; }
    /// <summary>When true, write the SARIF file only when audit fails.</summary>
    public bool SarifOnFailOnly { get; set; }
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
public static partial class WebSiteAuditor
{
    private static readonly string[] DefaultHtmlExtensions = { ".html", ".htm" };
    private static readonly string[] IgnoreLinkPrefixes = { "#", "mailto:", "tel:", "javascript:", "data:", "blob:" };
    private static readonly StringComparison FileSystemPathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private const long MaxAuditDataFileSizeBytes = 10 * 1024 * 1024;
    private const int RenderedDetailLimit = 3;

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
        var allHtmlFiles = EnumerateHtmlFiles(siteRoot, options.Include, options.Exclude, options.UseDefaultExcludes)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var htmlFiles = allHtmlFiles;
        if (options.MaxHtmlFiles > 0 && htmlFiles.Count > options.MaxHtmlFiles)
            htmlFiles = htmlFiles.Take(options.MaxHtmlFiles).ToList();
        var htmlFileCount = allHtmlFiles.Count;
        var htmlSelectedFileCount = htmlFiles.Count;
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
            HtmlFileCount = htmlFileCount,
            HtmlSelectedFileCount = htmlSelectedFileCount,
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

        if (!string.IsNullOrWhiteSpace(options.SummaryPath) &&
            (!options.SummaryOnFailOnly || !result.Success))
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

        if (!string.IsNullOrWhiteSpace(options.SarifPath) &&
            (!options.SarifOnFailOnly || !result.Success))
        {
            var sarifPath = ResolveSummaryPath(siteRoot, options.SarifPath);
            WriteSarif(sarifPath, result.Issues, warnings);
            result.SarifPath = sarifPath;
        }

        return result;
    }
}
