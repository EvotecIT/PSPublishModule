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
    /// <summary>
    /// Maximum total file count under site root (0 disables).
    /// This is useful as a "bundle explosion" / site bloat guardrail.
    /// </summary>
    public int MaxTotalFiles { get; set; }
    /// <summary>
    /// Optional glob patterns to exclude from budget counters (for example <c>api/**</c>).
    /// This is applied to file-count budgets like <see cref="MaxTotalFiles"/> without affecting
    /// the HTML audit scope (<see cref="Include"/>/<see cref="Exclude"/>).
    /// </summary>
    public string[] BudgetExclude { get; set; } = Array.Empty<string>();
    /// <summary>When true, apply built-in exclude patterns for partial HTML files.</summary>
    public bool UseDefaultExcludes { get; set; } = true;
    /// <summary>When true, validate HTML structure.</summary>
    public bool CheckHtmlStructure { get; set; } = true;
    /// <summary>When true, enforce non-empty page titles.</summary>
    public bool CheckTitles { get; set; } = true;
    /// <summary>When true, detect duplicate element IDs.</summary>
    public bool CheckDuplicateIds { get; set; } = true;
    /// <summary>When true, validate media/embed performance and accessibility hints.</summary>
    public bool CheckMediaEmbeds { get; set; } = true;
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
    /// <summary>Skip media/embed checks on pages that match these glob patterns.</summary>
    public string[] IgnoreMediaFor { get; set; } = new[]
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
    /// <summary>Optional per-path media/embed behavior overrides.</summary>
    public WebAuditMediaProfile[] MediaProfiles { get; set; } = Array.Empty<WebAuditMediaProfile>();
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
    /// <summary>
    /// Optional root directory used to resolve <see cref="BaselinePath"/> when it is relative.
    /// Defaults to <see cref="SiteRoot"/> when not set.
    /// </summary>
    public string? BaselineRoot { get; set; }
    /// <summary>
    /// Optional list of issue suppressions (do not emit, do not count, do not gate).
    /// Entries may be:
    /// - a code (matches <c>[CODE]</c> prefixes; audit issues use codes like <c>PFAUDIT.NAV</c>, <c>PFAUDIT.BUDGET</c>)
    /// - a substring (case-insensitive)
    /// - a wildcard pattern with <c>*</c> and <c>?</c>
    /// - a regex prefixed with <c>re:</c>
    /// </summary>
    public string[] SuppressIssues { get; set; } = Array.Empty<string>();
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
