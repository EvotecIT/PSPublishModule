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
    /// <summary>When true, validate internal links.</summary>
    public bool CheckLinks { get; set; } = true;
    /// <summary>When true, validate local assets (CSS/JS/images).</summary>
    public bool CheckAssets { get; set; } = true;
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

        var baselineNavSignature = (string?)null;
        var baselineNavSource = (string?)null;
        var pageCount = 0;
        var linkCount = 0;
        var brokenLinkCount = 0;
        var assetCount = 0;
        var missingAssetCount = 0;
        var navMismatchCount = 0;
        var duplicateIdCount = 0;
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
                        baselineNavSignature = BuildNavSignature(canonicalNav);
                        baselineNavSource = ToRelative(siteRoot, canonicalPath);
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

            var navIgnored = options.IgnoreNavFor.Length > 0 &&
                             MatchesAny(options.IgnoreNavFor, relativePath);
            var prefixIgnored = navIgnorePrefixes.Length > 0 &&
                                navIgnorePrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (options.CheckNavConsistency && !navIgnored && !prefixIgnored)
            {
                var navElement = doc.QuerySelector(options.NavSelector);
                if (navElement is null)
                {
                    if (options.NavRequired)
                        AddIssue("warning", "nav", relativePath, $"nav not found using selector '{options.NavSelector}'.", "nav-missing");
                }
                else
                {
                    var signature = BuildNavSignature(navElement);
                    if (baselineNavSignature is null)
                    {
                        baselineNavSignature = signature;
                        baselineNavSource = relativePath;
                    }
                    else if (!string.Equals(baselineNavSignature, signature, StringComparison.Ordinal))
                    {
                        navMismatchCount++;
                        var sourceLabel = string.IsNullOrWhiteSpace(baselineNavSource) ? "baseline" : baselineNavSource;
                        AddIssue("warning", "nav", relativePath, $"nav differs from baseline ({sourceLabel}).", "nav-mismatch");
                    }

                    if (requiredNavLinks.Length > 0)
                    {
                        var navLinks = navElement.QuerySelectorAll("a[href]")
                            .Select(a => NormalizeNavHref(a.GetAttribute("href")))
                            .Where(link => !string.IsNullOrWhiteSpace(link))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var missing = requiredNavLinks
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
            DuplicateIdCount = duplicateIdCount,
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
                DuplicateIdCount = result.DuplicateIdCount,
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
