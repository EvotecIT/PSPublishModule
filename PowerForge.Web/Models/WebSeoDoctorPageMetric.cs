namespace PowerForge.Web;

/// <summary>Per-page SEO backlog metrics emitted by SEO doctor.</summary>
public sealed class WebSeoDoctorPageMetric
{
    /// <summary>Page path relative to the generated site root, prefixed with slash for backlog reports.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Resolved title text.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Number of title tags found in the document.</summary>
    public int TitleTagCount { get; set; }
    /// <summary>Meta description length in characters.</summary>
    public int DescriptionLength { get; set; }
    /// <summary>Whether the page is missing a non-empty meta description.</summary>
    public bool MissingDescription { get; set; }
    /// <summary>Whether the meta description is shorter than the configured minimum.</summary>
    public bool ShortDescription { get; set; }
    /// <summary>Whether the meta description is longer than the configured maximum.</summary>
    public bool LongDescription { get; set; }
    /// <summary>Number of visible h1 elements.</summary>
    public int H1Count { get; set; }
    /// <summary>Whether the page is missing a visible h1.</summary>
    public bool MissingH1 { get; set; }
    /// <summary>Whether the page has more than one visible h1.</summary>
    public bool MultipleH1 { get; set; }
    /// <summary>Number of visible images with src attributes.</summary>
    public int ImageCount { get; set; }
    /// <summary>Number of visible images missing alt attributes.</summary>
    public int MissingAltCount { get; set; }
    /// <summary>Number of visible images with explicit empty alt text.</summary>
    public int EmptyAltCount { get; set; }
    /// <summary>Semicolon-separated sample sources for images missing alt attributes.</summary>
    public string MissingAltSamples { get; set; } = string.Empty;
    /// <summary>Semicolon-separated sample sources for images with empty alt text.</summary>
    public string EmptyAltSamples { get; set; } = string.Empty;
    /// <summary>Whether the page declares robots noindex.</summary>
    public bool NoIndex { get; set; }
}
