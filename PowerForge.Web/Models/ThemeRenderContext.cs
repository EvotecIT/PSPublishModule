namespace PowerForge.Web;

internal sealed class ThemeRenderContext
{
    public SiteSpec Site { get; init; } = new();
    public ContentItem Page { get; init; } = new();
    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
    public ProjectSpec? Project { get; init; }
    public string CssHtml { get; init; } = string.Empty;
    public string JsHtml { get; init; } = string.Empty;
    public string PreloadsHtml { get; init; } = string.Empty;
    public string CriticalCssHtml { get; init; } = string.Empty;
    public string CanonicalHtml { get; init; } = string.Empty;
    public string DescriptionMetaHtml { get; init; } = string.Empty;
}
