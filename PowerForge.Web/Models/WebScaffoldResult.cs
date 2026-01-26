namespace PowerForge.Web;

/// <summary>Result payload for site scaffolding.</summary>
public sealed class WebScaffoldResult
{
    /// <summary>Output directory for the scaffolded site.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Number of files created.</summary>
    public int CreatedFileCount { get; set; }
    /// <summary>Template engine used for the scaffold.</summary>
    public string ThemeEngine { get; set; } = string.Empty;
    /// <summary>Site name used during scaffolding.</summary>
    public string SiteName { get; set; } = string.Empty;
    /// <summary>Base URL used during scaffolding.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}
