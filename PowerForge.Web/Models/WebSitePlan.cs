namespace PowerForge.Web;

public sealed class WebSitePlan
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ConfigPath { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string? ContentRoot { get; set; }
    public string? ProjectsRoot { get; set; }
    public string? ThemesRoot { get; set; }
    public string? SharedRoot { get; set; }

    public WebCollectionPlan[] Collections { get; set; } = Array.Empty<WebCollectionPlan>();
    public WebProjectPlan[] Projects { get; set; } = Array.Empty<WebProjectPlan>();

    public int RedirectCount { get; set; }
    public int RouteOverrideCount { get; set; }
}

public sealed class WebCollectionPlan
{
    public string Name { get; set; } = string.Empty;
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int FileCount { get; set; }
}

public sealed class WebProjectPlan
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string? ContentPath { get; set; }
    public int ContentFileCount { get; set; }
}

public sealed class WebBuildResult
{
    public string OutputPath { get; set; } = string.Empty;
    public string PlanPath { get; set; } = string.Empty;
    public string SpecPath { get; set; } = string.Empty;
    public string RedirectsPath { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
}
