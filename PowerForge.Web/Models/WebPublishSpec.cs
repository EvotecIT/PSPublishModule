namespace PowerForge.Web;

public sealed class WebPublishSpec
{
    public int SchemaVersion { get; set; } = 1;
    public WebPublishBuildSpec Build { get; set; } = new();
    public WebPublishOverlaySpec? Overlay { get; set; }
    public WebPublishDotNetSpec Publish { get; set; } = new();
    public WebPublishOptimizeSpec? Optimize { get; set; }
}

public sealed class WebPublishBuildSpec
{
    public string Config { get; set; } = string.Empty;
    public string Out { get; set; } = string.Empty;
}

public sealed class WebPublishOverlaySpec
{
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string[]? Include { get; set; }
    public string[]? Exclude { get; set; }
}

public sealed class WebPublishDotNetSpec
{
    public string Project { get; set; } = string.Empty;
    public string Out { get; set; } = string.Empty;
    public string? Configuration { get; set; }
    public string? Framework { get; set; }
    public string? Runtime { get; set; }
    public bool SelfContained { get; set; }
    public bool NoBuild { get; set; }
    public bool NoRestore { get; set; }
    public string? BaseHref { get; set; }
    public bool ApplyBlazorFixes { get; set; } = true;
}

public sealed class WebPublishOptimizeSpec
{
    public string? SiteRoot { get; set; }
    public string? CriticalCss { get; set; }
    public string? CssPattern { get; set; }
    public bool MinifyHtml { get; set; }
    public bool MinifyCss { get; set; }
    public bool MinifyJs { get; set; }
}
