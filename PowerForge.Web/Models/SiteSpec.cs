namespace PowerForge.Web;

public sealed class SiteSpec
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? DefaultTheme { get; set; }
    public string? ThemeEngine { get; set; }
    public TrailingSlashMode TrailingSlash { get; set; } = TrailingSlashMode.Ignore;
    public string? ContentRoot { get; set; }
    public string? ProjectsRoot { get; set; }
    public string? ThemesRoot { get; set; }
    public string? SharedRoot { get; set; }
    public string? DataRoot { get; set; }

    public CollectionSpec[] Collections { get; set; } = Array.Empty<CollectionSpec>();

    public EditLinksSpec? EditLinks { get; set; }
    public RedirectSpec[] RouteOverrides { get; set; } = Array.Empty<RedirectSpec>();
    public RedirectSpec[] Redirects { get; set; } = Array.Empty<RedirectSpec>();

    public AssetRegistrySpec? AssetRegistry { get; set; }
    public A11ySpec? A11y { get; set; }
    public LinkRulesSpec? LinkRules { get; set; }
    public AnalyticsSpec? Analytics { get; set; }
}
