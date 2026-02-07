namespace PowerForge.Web;

internal sealed class ThemeRenderContext
{
    public SiteSpec Site { get; init; } = new();
    public ContentItem Page { get; init; } = new();
    public IReadOnlyList<ContentItem> Items { get; init; } = Array.Empty<ContentItem>();
    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
    public ProjectSpec? Project { get; init; }
    public NavigationRuntime Navigation { get; init; } = new();
    public VersioningRuntime Versioning { get; init; } = new();
    public BreadcrumbItem[] Breadcrumbs { get; init; } = Array.Empty<BreadcrumbItem>();
    public string CurrentPath { get; init; } = string.Empty;
    public ShortcodeContext? Shortcode { get; init; }
    public TaxonomySpec? Taxonomy { get; init; }
    public string? Term { get; init; }
    public string CssHtml { get; init; } = string.Empty;
    public string JsHtml { get; init; } = string.Empty;
    public string PreloadsHtml { get; init; } = string.Empty;
    public string CriticalCssHtml { get; init; } = string.Empty;
    public string CanonicalHtml { get; init; } = string.Empty;
    public string DescriptionMetaHtml { get; init; } = string.Empty;
    public string HeadHtml { get; init; } = string.Empty;
    public string OpenGraphHtml { get; init; } = string.Empty;
    public string StructuredDataHtml { get; init; } = string.Empty;
    public string ExtraCssHtml { get; init; } = string.Empty;
    public string ExtraScriptsHtml { get; init; } = string.Empty;
    public string BodyClass { get; init; } = string.Empty;
}
