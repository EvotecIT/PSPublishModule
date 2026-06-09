namespace PowerForge.Web;

/// <summary>Resolved output format metadata for the current page.</summary>
public sealed class OutputRuntime
{
    /// <summary>Format name (html/rss/json).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Public URL for this output.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>MIME type for this output.</summary>
    public string MediaType { get; set; } = "text/html";

    /// <summary>Optional rel attribute for link tags.</summary>
    public string? Rel { get; set; }

    /// <summary>True when this output is the rendered HTML page.</summary>
    public bool IsCurrent { get; set; }
}
