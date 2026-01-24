namespace PowerForge.Web;

public sealed class WebScaffoldResult
{
    public string OutputPath { get; set; } = string.Empty;
    public int CreatedFileCount { get; set; }
    public string ThemeEngine { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}
