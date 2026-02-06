namespace PowerForge.Web;

/// <summary>Resolved site plan from configuration.</summary>
public sealed class WebSitePlan
{
    /// <summary>Site name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Base URL.</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Path to the site configuration file.</summary>
    public string ConfigPath { get; set; } = string.Empty;
    /// <summary>Root path of the site.</summary>
    public string RootPath { get; set; } = string.Empty;
    /// <summary>Resolved content root.</summary>
    public string? ContentRoot { get; set; }
    /// <summary>Resolved additional content roots.</summary>
    public string[] ContentRoots { get; set; } = Array.Empty<string>();
    /// <summary>Resolved projects root.</summary>
    public string? ProjectsRoot { get; set; }
    /// <summary>Resolved themes root.</summary>
    public string? ThemesRoot { get; set; }
    /// <summary>Resolved shared content root.</summary>
    public string? SharedRoot { get; set; }

    /// <summary>Resolved collections.</summary>
    public WebCollectionPlan[] Collections { get; set; } = Array.Empty<WebCollectionPlan>();
    /// <summary>Resolved projects.</summary>
    public WebProjectPlan[] Projects { get; set; } = Array.Empty<WebProjectPlan>();

    /// <summary>Total redirect count.</summary>
    public int RedirectCount { get; set; }
    /// <summary>Total route override count.</summary>
    public int RouteOverrideCount { get; set; }
}

/// <summary>Resolved plan for a collection.</summary>
public sealed class WebCollectionPlan
{
    /// <summary>Collection name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Input path for the collection.</summary>
    public string InputPath { get; set; } = string.Empty;
    /// <summary>Output path for generated content.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Number of input files.</summary>
    public int FileCount { get; set; }
}

/// <summary>Resolved plan for a project.</summary>
public sealed class WebProjectPlan
{
    /// <summary>Project name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Project slug.</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary>Project root path.</summary>
    public string RootPath { get; set; } = string.Empty;
    /// <summary>Optional content root for the project.</summary>
    public string? ContentPath { get; set; }
    /// <summary>Number of content files.</summary>
    public int ContentFileCount { get; set; }
}

/// <summary>Result payload for site build.</summary>
public sealed class WebBuildResult
{
    /// <summary>Output directory path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Path to the generated plan file.</summary>
    public string PlanPath { get; set; } = string.Empty;
    /// <summary>Path to the generated spec file.</summary>
    public string SpecPath { get; set; } = string.Empty;
    /// <summary>Path to the generated redirects file.</summary>
    public string RedirectsPath { get; set; } = string.Empty;
    /// <summary>UTC timestamp when build was generated.</summary>
    public DateTime GeneratedAtUtc { get; set; }
}
