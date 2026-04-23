namespace PowerForge.Web;

/// <summary>Source Markdown image-alt backlog metrics emitted by SEO doctor.</summary>
public sealed class WebSeoDoctorSourceMarkdownMetric
{
    /// <summary>Markdown file path relative to the scanned content root.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Number of raw Markdown image references with empty alt text.</summary>
    public int EmptyMarkdownAltCount { get; set; }
    /// <summary>Semicolon-separated sample image targets.</summary>
    public string SampleTargets { get; set; } = string.Empty;
    /// <summary>Semicolon-separated sample line numbers.</summary>
    public string SampleLineNumbers { get; set; } = string.Empty;
}
