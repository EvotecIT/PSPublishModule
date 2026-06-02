namespace PowerForge.Web;

/// <summary>Result payload for exporting bundled sitemap protocol schemas.</summary>
public sealed class WebSitemapProtocolSchemaExportResult
{
    /// <summary>Output directory containing the exported schemas.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Path to the exported sitemap.xsd file.</summary>
    public string SitemapSchemaPath { get; set; } = string.Empty;

    /// <summary>Path to the exported siteindex.xsd file.</summary>
    public string SitemapIndexSchemaPath { get; set; } = string.Empty;

    /// <summary>Compatibility notes for consumers using the exported schemas.</summary>
    public string[] Notes { get; set; } = Array.Empty<string>();
}
