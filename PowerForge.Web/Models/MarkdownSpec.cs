namespace PowerForge.Web;

/// <summary>Markdown rendering options.</summary>
public sealed class MarkdownSpec
{
    /// <summary>
    /// When true, inject default loading/decoding hints on rendered image tags if missing.
    /// </summary>
    public bool AutoImageHints { get; set; } = true;

    /// <summary>Default value for img loading attribute when missing.</summary>
    public string DefaultImageLoading { get; set; } = "lazy";

    /// <summary>Default value for img decoding attribute when missing.</summary>
    public string DefaultImageDecoding { get; set; } = "async";
}
