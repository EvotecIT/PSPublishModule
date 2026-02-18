namespace PowerForge.Web;

/// <summary>Root site configuration for PowerForge.Web.</summary>
public sealed class SiteSpec
{
    /// <summary>Schema version for the site spec.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Site name used in templates and metadata.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Absolute base URL for the site.</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Default theme name.</summary>
    public string? DefaultTheme { get; set; }
    /// <summary>Template engine identifier (for example, scriban).</summary>
    public string? ThemeEngine { get; set; }
    /// <summary>Trailing slash behavior for generated URLs.</summary>
    public TrailingSlashMode TrailingSlash { get; set; } = TrailingSlashMode.Ignore;
    /// <summary>Optional content root for shared pages.</summary>
    public string? ContentRoot { get; set; }
    /// <summary>Optional additional content roots used as source-of-truth folders.</summary>
    public string[] ContentRoots { get; set; } = Array.Empty<string>();
    /// <summary>Optional projects root.</summary>
    public string? ProjectsRoot { get; set; }

    /// <summary>
    /// Optional repository sources that can be synchronized into ProjectsRoot via `powerforge-web sources-sync`
    /// (or `powerforge-web build --sync-sources`). This keeps `build` deterministic by default while allowing
    /// one-command bootstrap when explicitly requested.
    /// </summary>
    public SourceRepoSpec[] Sources { get; set; } = Array.Empty<SourceRepoSpec>();
    /// <summary>Optional themes root.</summary>
    public string? ThemesRoot { get; set; }
    /// <summary>Optional shared content root.</summary>
    public string? SharedRoot { get; set; }
    /// <summary>Optional data root.</summary>
    public string? DataRoot { get; set; }
    /// <summary>Optional archetypes root for content scaffolding.</summary>
    public string? ArchetypesRoot { get; set; }
    /// <summary>Static asset mappings to copy.</summary>
    public StaticAssetSpec[] StaticAssets { get; set; } = Array.Empty<StaticAssetSpec>();

    /// <summary>Content collections (pages, docs, blog, etc.).</summary>
    public CollectionSpec[] Collections { get; set; } = Array.Empty<CollectionSpec>();

    /// <summary>Output format configuration.</summary>
    public OutputsSpec? Outputs { get; set; }
    /// <summary>Pagination defaults for section/taxonomy pages.</summary>
    public PaginationSpec? Pagination { get; set; }
    /// <summary>Feed generation settings for RSS outputs.</summary>
    public FeedSpec? Feed { get; set; }
    /// <summary>Taxonomy definitions (tags, categories).</summary>
    public TaxonomySpec[] Taxonomies { get; set; } = Array.Empty<TaxonomySpec>();

    /// <summary>Head configuration applied to all pages.</summary>
    public HeadSpec? Head { get; set; }
    /// <summary>OpenGraph/Twitter card configuration.</summary>
    public SocialSpec? Social { get; set; }
    /// <summary>Structured data configuration.</summary>
    public StructuredDataSpec? StructuredData { get; set; }

    /// <summary>Git-based edit link configuration.</summary>
    public EditLinksSpec? EditLinks { get; set; }
    /// <summary>Route overrides applied before redirects.</summary>
    public RedirectSpec[] RouteOverrides { get; set; } = Array.Empty<RedirectSpec>();
    /// <summary>Redirect rules for legacy URLs.</summary>
    public RedirectSpec[] Redirects { get; set; } = Array.Empty<RedirectSpec>();
    /// <summary>When enabled, emit legacy /amp redirects to canonical routes for generated pages.</summary>
    public bool EnableLegacyAmpRedirects { get; set; }

    /// <summary>Asset registry configuration for bundling/preloading.</summary>
    public AssetRegistrySpec? AssetRegistry { get; set; }
    /// <summary>Global asset policy (local vs CDN, hashing, headers).</summary>
    public AssetPolicySpec? AssetPolicy { get; set; }
    /// <summary>Prism syntax highlighting configuration.</summary>
    public PrismSpec? Prism { get; set; }
    /// <summary>Accessibility strings.</summary>
    public A11ySpec? A11y { get; set; }
    /// <summary>Rules for external links.</summary>
    public LinkRulesSpec? LinkRules { get; set; }
    /// <summary>Analytics configuration.</summary>
    public AnalyticsSpec? Analytics { get; set; }
    /// <summary>Navigation menus.</summary>
    public NavigationSpec? Navigation { get; set; }
    /// <summary>Localization and multi-language routing.</summary>
    public LocalizationSpec? Localization { get; set; }

    /// <summary>
    /// Explicit feature flags for engine and theme contract verification (for example: docs, apiDocs, blog, search).
    /// Prefer declaring these to avoid implicit inference and cross-site regressions.
    /// </summary>
    public string[] Features { get; set; } = Array.Empty<string>();

    /// <summary>Documentation versioning configuration.</summary>
    public VersioningSpec? Versioning { get; set; }
    /// <summary>Link checking configuration.</summary>
    public LinkCheckSpec? LinkCheck { get; set; }
    /// <summary>Cross-reference link configuration (docs/API xref map).</summary>
    public XrefSpec? Xref { get; set; }
    /// <summary>Verification policy defaults for verify/doctor commands.</summary>
    public VerifyPolicySpec? Verify { get; set; }
    /// <summary>Build cache configuration.</summary>
    public BuildCacheSpec? Cache { get; set; }
}
