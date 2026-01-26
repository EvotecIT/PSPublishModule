namespace PowerForge.Web;

/// <summary>Publish pipeline configuration.</summary>
public sealed class WebPublishSpec
{
    /// <summary>Schema version for publish spec.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Build step configuration.</summary>
    public WebPublishBuildSpec Build { get; set; } = new();
    /// <summary>Optional static overlay configuration.</summary>
    public WebPublishOverlaySpec? Overlay { get; set; }
    /// <summary>Dotnet publish configuration.</summary>
    public WebPublishDotNetSpec Publish { get; set; } = new();
    /// <summary>Optional optimization step configuration.</summary>
    public WebPublishOptimizeSpec? Optimize { get; set; }
}

/// <summary>Build step configuration.</summary>
public sealed class WebPublishBuildSpec
{
    /// <summary>Build configuration (Debug/Release).</summary>
    public string Config { get; set; } = string.Empty;
    /// <summary>Output directory.</summary>
    public string Out { get; set; } = string.Empty;
}

/// <summary>Static overlay copy configuration.</summary>
public sealed class WebPublishOverlaySpec
{
    /// <summary>Source directory.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Destination directory.</summary>
    public string Destination { get; set; } = string.Empty;
    /// <summary>Optional include patterns.</summary>
    public string[]? Include { get; set; }
    /// <summary>Optional exclude patterns.</summary>
    public string[]? Exclude { get; set; }
}

/// <summary>Dotnet publish configuration.</summary>
public sealed class WebPublishDotNetSpec
{
    /// <summary>Project path.</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>Output directory.</summary>
    public string Out { get; set; } = string.Empty;
    /// <summary>Build configuration.</summary>
    public string? Configuration { get; set; }
    /// <summary>Target framework.</summary>
    public string? Framework { get; set; }
    /// <summary>Runtime identifier.</summary>
    public string? Runtime { get; set; }
    /// <summary>Whether to publish as self-contained.</summary>
    public bool SelfContained { get; set; }
    /// <summary>Skip build step.</summary>
    public bool NoBuild { get; set; }
    /// <summary>Skip restore step.</summary>
    public bool NoRestore { get; set; }
    /// <summary>Optional base href override for SPA output.</summary>
    public string? BaseHref { get; set; }
    /// <summary>Apply Blazor-specific fixes after publish.</summary>
    public bool ApplyBlazorFixes { get; set; } = true;
}

/// <summary>Post-publish optimization configuration.</summary>
public sealed class WebPublishOptimizeSpec
{
    /// <summary>Site root used for optimization.</summary>
    public string? SiteRoot { get; set; }
    /// <summary>Path to critical CSS file.</summary>
    public string? CriticalCss { get; set; }
    /// <summary>CSS file glob pattern.</summary>
    public string? CssPattern { get; set; }
    /// <summary>When true, minify HTML output.</summary>
    public bool MinifyHtml { get; set; }
    /// <summary>When true, minify CSS output.</summary>
    public bool MinifyCss { get; set; }
    /// <summary>When true, minify JavaScript output.</summary>
    public bool MinifyJs { get; set; }
}
